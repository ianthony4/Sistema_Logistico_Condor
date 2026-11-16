using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;

namespace Condor.Pruebas.Infraestructura;

/// <summary>
/// SUNAT falsa (HU-07): responde al instante sin internet. Por defecto acepta
/// todo; puede rechazar, observar, simular que no hay internet o tardar.
/// Desde el Sprint 4 también simula que internet se corta a mitad de un
/// reenvío (HU-17) y admite envíos desde varios hilos a la vez.
/// </summary>
public sealed class EnvioSunatFalso : IEnvioSunat
{
    public string Ambiente => "prueba";

    /// <summary>Respuesta que dará a los envíos; null acepta con el código 0.</summary>
    public RespuestaSunat? Respuesta { get; set; }

    /// <summary>Si es true, EnviarAsync lanza SunatNoDisponibleException (sin internet).</summary>
    public bool SinInternet { get; set; }

    /// <summary>Si se asigna, EnviarAsync no responde hasta que se complete (para probar que no bloquea la venta).</summary>
    public TaskCompletionSource? Demora { get; set; }

    /// <summary>Si se asigna, acepta esa cantidad de envíos más y después se queda sin internet (HU-17).</summary>
    public int? SinInternetDespuesDe { get; set; }

    private readonly List<Comprobante> _enviados = new();

    /// <summary>Comprobantes que llegaron a la SUNAT, en el orden en que llegaron.</summary>
    public IReadOnlyList<Comprobante> Enviados
    {
        get
        {
            lock (_enviados)
                return _enviados.ToList();
        }
    }

    public async Task<RespuestaSunat> EnviarAsync(Empresa empresa, Comprobante comprobante, CancellationToken ct = default)
    {
        if (Demora is not null)
            await Demora.Task.WaitAsync(ct);

        lock (_enviados)
        {
            if (SinInternetDespuesDe is <= 0)
                SinInternet = true;
            if (SinInternet)
                throw new SunatNoDisponibleException("No hay conexión a internet.");
            if (SinInternetDespuesDe is { } quedan)
                SinInternetDespuesDe = quedan - 1;
            _enviados.Add(comprobante);
        }
        var nombre = $"{empresa.Ruc}-{comprobante.TipoComprobante}-{comprobante.SerieCodigo}-{comprobante.Numero}";
        return Respuesta ?? Aceptada(nombre);
    }

    public static RespuestaSunat Aceptada(string nombreArchivo = "") => new()
    {
        Estado = EstadoSunat.Aceptado,
        Codigo = "0",
        Descripcion = $"La Boleta numero {nombreArchivo}, ha sido aceptada",
        NombreArchivo = nombreArchivo,
        XmlFirmado = "<Invoice/>",
        Hash = "hS0bHq3cJ0aZyb0dA1Y4mWm3x1A=",
        CdrZip = new byte[] { 0x50, 0x4B, 0x05, 0x06 },
    };

    public static RespuestaSunat Rechazada(string codigo = "2017", string descripcion = "El numero de documento de identidad del receptor debe ser RUC") => new()
    {
        Estado = EstadoSunat.Rechazado,
        Codigo = codigo,
        Descripcion = descripcion,
    };

    public static RespuestaSunat Observada() => new()
    {
        Estado = EstadoSunat.Observado,
        Codigo = "0",
        Descripcion = "La Boleta ha sido aceptada",
        Notas = new[] { "4252 - El dato ingresado como atributo @listName es incorrecto." },
    };
}
