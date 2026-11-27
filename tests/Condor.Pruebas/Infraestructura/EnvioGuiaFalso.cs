using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;

namespace Condor.Pruebas.Infraestructura;

/// <summary>
/// API de guías de la SUNAT falsa (HU-10): acepta al instante, o deja la guía
/// en proceso con un ticket y la acepta en la consulta siguiente; también
/// simula que no hay internet o que la SUNAT la rechaza.
/// </summary>
public sealed class EnvioGuiaFalso : IEnvioGuiaSunat
{
    public string Ambiente => "prueba";

    /// <summary>Si es true, EnviarAsync lanza SunatNoDisponibleException.</summary>
    public bool SinInternet { get; set; }

    /// <summary>Si es true, la primera vez responde "en proceso" con un ticket; la consulta con ticket la acepta.</summary>
    public bool Demora { get; set; }

    /// <summary>Respuesta fija (por ejemplo, un rechazo); null acepta.</summary>
    public RespuestaSunat? Respuesta { get; set; }

    public List<GuiaRemision> Enviadas { get; } = new();

    /// <summary>Tickets con que se consultó (guías que ya se habían enviado).</summary>
    public List<string> Consultas { get; } = new();

    public Task<RespuestaSunat> EnviarAsync(Empresa empresa, GuiaRemision guia, CancellationToken ct = default)
    {
        if (SinInternet)
            throw new SunatNoDisponibleException("No hay conexión con la SUNAT (prueba).");

        if (guia.Ticket is { } ticket)
        {
            Consultas.Add(ticket);
            return Task.FromResult(Respuesta ?? Aceptada(guia, ticket));
        }

        Enviadas.Add(guia);
        var nuevo = $"ticket-{guia.Numero}";
        if (Demora)
            return Task.FromResult(new RespuestaSunat
            {
                Estado = EstadoSunat.Pendiente, Codigo = "98", Ticket = nuevo,
                Descripcion = $"La SUNAT está procesando la guía (ticket {nuevo}).",
            });
        return Task.FromResult(Respuesta ?? Aceptada(guia, nuevo));
    }

    public static RespuestaSunat Rechazada(string codigo, string descripcion) =>
        new() { Estado = EstadoSunat.Rechazado, Codigo = codigo, Descripcion = descripcion };

    private static RespuestaSunat Aceptada(GuiaRemision guia, string ticket) => new()
    {
        Estado = EstadoSunat.Aceptado,
        Codigo = "0",
        Descripcion = $"La Guia numero {guia.SerieCodigo}-{guia.Numero}, ha sido aceptada",
        Ticket = ticket,
        CdrZip = new byte[] { 0x50, 0x4B },
    };
}
