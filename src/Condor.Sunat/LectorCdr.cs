using System.IO.Compression;
using System.Xml.Linq;
using Condor.Dominio.Sunat;

namespace Condor.Sunat;

/// <summary>Lo que dice el CDR (ApplicationResponse) de la SUNAT.</summary>
public sealed record Cdr(string ReferenciaId, string Codigo, string Descripcion, IReadOnlyList<string> Notas);

/// <summary>
/// Lee la Constancia de Recepción (CDR) que devuelve la SUNAT y decide el
/// estado del comprobante (HU-07, T-43). Equivale a DomCdrReader de greenter.
/// </summary>
/// <remarks>
/// Códigos de la SUNAT: 0 aceptado; 0100 a 1999 excepciones (el envío no se
/// procesó: se corrige y se vuelve a enviar); 2000 a 3999 rechazo; 4000 en
/// adelante, observaciones de un comprobante aceptado.
/// </remarks>
public static class LectorCdr
{
    private static readonly XNamespace Cac = GeneradorXmlUbl.Cac;
    private static readonly XNamespace Cbc = GeneradorXmlUbl.Cbc;

    /// <summary>Extrae el XML del CDR (R-...xml) del ZIP que devuelve sendBill.</summary>
    public static Cdr LeerZip(byte[] zip)
    {
        try
        {
            using var archivo = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
            var entrada = archivo.Entries.FirstOrDefault(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && e.Length > 0)
                          ?? throw new CdrInvalidoException("El ZIP de la SUNAT no trae el XML del CDR.");
            using var flujo = entrada.Open();
            return Leer(XDocument.Load(flujo));
        }
        catch (InvalidDataException ex)
        {
            throw new CdrInvalidoException("La SUNAT devolvió un CDR que no es un ZIP válido.", ex);
        }
    }

    public static Cdr Leer(XDocument cdr)
    {
        var raiz = cdr.Root ?? throw new CdrInvalidoException("El CDR está vacío.");
        var respuesta = raiz.Element(Cac + "DocumentResponse")?.Element(Cac + "Response")
                        ?? throw new CdrInvalidoException("El CDR no trae cac:DocumentResponse/cac:Response.");

        return new Cdr(
            respuesta.Element(Cbc + "ReferenceID")?.Value.Trim() ?? string.Empty,
            respuesta.Element(Cbc + "ResponseCode")?.Value.Trim() ?? string.Empty,
            respuesta.Element(Cbc + "Description")?.Value.Trim() ?? string.Empty,
            raiz.Elements(Cbc + "Note").Select(n => n.Value.Trim()).Where(n => n.Length > 0).ToList());
    }

    /// <summary>Estado del comprobante según el código del CDR.</summary>
    public static string Estado(Cdr cdr)
    {
        var codigo = Numero(cdr.Codigo);
        return codigo switch
        {
            0 => cdr.Notas.Count > 0 ? EstadoSunat.Observado : EstadoSunat.Aceptado,
            >= 2000 and <= 3999 => EstadoSunat.Rechazado,
            >= 4000 => EstadoSunat.Observado,
            _ => EstadoSunat.Pendiente,
        };
    }

    /// <summary>
    /// Estado cuando la SUNAT responde con un error SOAP (soap:Fault) en vez
    /// del CDR: 2000 a 3999 es un rechazo; lo demás (0100 a 1999: clave SOL,
    /// archivo, servicio) deja el comprobante pendiente para reenviarlo.
    /// </summary>
    public static string EstadoDeError(string codigo) =>
        Numero(codigo) is >= 2000 and <= 3999 ? EstadoSunat.Rechazado : EstadoSunat.Pendiente;

    /// <summary>
    /// Código numérico de un faultcode como "soap-env:Client.0111" o "2335";
    /// -1 si no tiene número.
    /// </summary>
    public static int Numero(string? codigo)
    {
        var digitos = new string((codigo ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digitos.Length is > 0 and <= 9 ? int.Parse(digitos) : -1;
    }
}

/// <summary>La respuesta de la SUNAT no se pudo leer.</summary>
public sealed class CdrInvalidoException : Exception
{
    public CdrInvalidoException(string mensaje, Exception? causa = null) : base(mensaje, causa)
    {
    }
}
