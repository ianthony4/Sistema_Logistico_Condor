using Condor.Dominio.Entidades;

namespace Condor.Dominio.Sunat;

/// <summary>
/// Estado de un comprobante ante la SUNAT (columna comprobante.estado_sunat).
/// </summary>
public static class EstadoSunat
{
    /// <summary>Emitido pero aún sin CDR: sin internet, la SUNAT no respondió o falta configurarla.</summary>
    public const string Pendiente = "PENDIENTE";

    /// <summary>CDR con código 0: la SUNAT lo aceptó.</summary>
    public const string Aceptado = "ACEPTADO";

    /// <summary>Aceptado, pero el CDR trae observaciones (códigos 4000 en adelante).</summary>
    public const string Observado = "OBSERVADO";

    /// <summary>La SUNAT lo rechazó (códigos 2000 a 3999): no tiene validez y hay que corregirlo.</summary>
    public const string Rechazado = "RECHAZADO";

    /// <summary>True si la SUNAT ya respondió algo definitivo y no hay que reenviarlo.</summary>
    public static bool EsDefinitivo(string estado) => estado is Aceptado or Observado or Rechazado;
}

/// <summary>
/// Lo que respondió la SUNAT a un comprobante (HU-07): el estado, el código y
/// la descripción del CDR, y los archivos para guardarlos en la base central.
/// </summary>
public sealed class RespuestaSunat
{
    public string Estado { get; init; } = EstadoSunat.Pendiente;

    /// <summary>ResponseCode del CDR ("0" aceptado) o código del error devuelto por la SUNAT.</summary>
    public string Codigo { get; init; } = string.Empty;

    public string Descripcion { get; init; } = string.Empty;

    /// <summary>Observaciones del CDR (cbc:Note), por ejemplo "4252 - El dato ingresado como ...".</summary>
    public IReadOnlyList<string> Notas { get; init; } = Array.Empty<string>();

    /// <summary>Nombre del archivo enviado, sin extensión: 20600000005-03-B001-12.</summary>
    public string NombreArchivo { get; init; } = string.Empty;

    /// <summary>XML UBL 2.1 firmado tal como se envió.</summary>
    public string? XmlFirmado { get; init; }

    /// <summary>Valor resumen (DigestValue de la firma) que va en la representación impresa.</summary>
    public string? Hash { get; init; }

    /// <summary>ZIP del CDR (R-...zip) devuelto por la SUNAT; null si no lo envió.</summary>
    public byte[]? CdrZip { get; init; }

    public bool Aceptado => Estado is EstadoSunat.Aceptado or EstadoSunat.Observado;

    /// <summary>Texto corto para la pantalla de venta y el historial.</summary>
    public string Resumen => Estado switch
    {
        EstadoSunat.Aceptado => "Aceptada por la SUNAT.",
        EstadoSunat.Observado => $"Aceptada por la SUNAT con observaciones: {string.Join(" ", Notas)}",
        EstadoSunat.Rechazado => $"Rechazada por la SUNAT ({Codigo}): {Descripcion}",
        _ => $"Pendiente de envío a la SUNAT: {Descripcion}",
    };
}

/// <summary>
/// Envía un comprobante emitido a la SUNAT y devuelve su CDR (HU-07). Lo
/// implementa Condor.Sunat: arma el XML UBL 2.1, lo firma, lo comprime y lo
/// manda al servicio sendBill.
/// </summary>
public interface IEnvioSunat
{
    /// <summary>"beta" o "producción", para mostrarlo en la pantalla.</summary>
    string Ambiente { get; }

    /// <summary>
    /// Envía el comprobante y devuelve lo que respondió la SUNAT (aceptado,
    /// observado o rechazado). Lanza SunatNoDisponibleException si no hay
    /// internet o la SUNAT no responde: el comprobante queda pendiente.
    /// </summary>
    Task<RespuestaSunat> EnviarAsync(Empresa empresa, Comprobante comprobante, CancellationToken ct = default);
}

/// <summary>
/// No se pudo hablar con la SUNAT (sin internet, tiempo agotado o servicio
/// caído). El comprobante sigue siendo válido para imprimirse y queda
/// pendiente de envío (HU-07, criterio 2).
/// </summary>
public sealed class SunatNoDisponibleException : Exception
{
    public SunatNoDisponibleException(string mensaje, Exception? causa = null) : base(mensaje, causa)
    {
    }
}
