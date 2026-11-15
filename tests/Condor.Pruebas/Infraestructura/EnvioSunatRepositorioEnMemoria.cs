using Condor.Dominio.Contratos;
using Condor.Dominio.Sunat;

namespace Condor.Pruebas.Infraestructura;

/// <summary>
/// Respuestas de la SUNAT en memoria. Como el real, un estado definitivo ya
/// no se cambia, cada respuesta o falla suma un intento y libera la reserva,
/// y un pendiente apartado por una PC no se le da a otra (HU-17).
/// </summary>
public sealed class EnvioSunatRepositorioEnMemoria : IEnvioSunatRepositorio
{
    /// <summary>Si se asigna, las operaciones lanzan esta excepción (para simular la red caída).</summary>
    public Exception? Error { get; set; }

    public Dictionary<long, RespuestaSunat> Respuestas { get; } = new();

    public Dictionary<long, string> UltimoError { get; } = new();

    public Dictionary<long, int> Intentos { get; } = new();

    /// <summary>PC que apartó cada pendiente para reenviarlo (HU-17).</summary>
    public Dictionary<long, string> Reservados { get; } = new();

    /// <summary>Pendientes que aún no tienen ningún intento (la PC principal no respondió al emitir).</summary>
    public HashSet<long> SinIntentos { get; } = new();

    public string Estado(long id) => Respuestas.TryGetValue(id, out var r) ? r.Estado : EstadoSunat.Pendiente;

    public Task RegistrarRespuestaAsync(long comprobanteId, RespuestaSunat respuesta, CancellationToken ct = default)
    {
        if (Error is not null)
            throw Error;

        Intentos[comprobanteId] = Intentos.GetValueOrDefault(comprobanteId) + 1;
        Reservados.Remove(comprobanteId);
        SinIntentos.Remove(comprobanteId);
        if (!EstadoSunat.EsDefinitivo(Estado(comprobanteId)))
            Respuestas[comprobanteId] = respuesta;
        if (EstadoSunat.EsDefinitivo(respuesta.Estado))
            UltimoError.Remove(comprobanteId);
        else
            UltimoError[comprobanteId] = respuesta.Descripcion;
        return Task.CompletedTask;
    }

    public Task RegistrarFallaAsync(long comprobanteId, string motivo, CancellationToken ct = default)
    {
        if (Error is not null)
            throw Error;

        Intentos[comprobanteId] = Intentos.GetValueOrDefault(comprobanteId) + 1;
        Reservados.Remove(comprobanteId);
        SinIntentos.Remove(comprobanteId);
        if (!EstadoSunat.EsDefinitivo(Estado(comprobanteId)))
            UltimoError[comprobanteId] = motivo;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<long>> ListarPendientesAsync(int limite = 100, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<long>>(Pendientes().Take(limite).ToList());

    /// <summary>Sin reloj: la espera no se simula, solo la reserva entre PC.</summary>
    public Task<IReadOnlyList<long>> ReservarPendientesAsync(string terminal, TimeSpan espera, int limite = 20, CancellationToken ct = default)
    {
        if (Error is not null)
            throw Error;

        var apartados = Pendientes().Where(id => !Reservados.ContainsKey(id)).Take(limite).ToList();
        foreach (var id in apartados)
            Reservados[id] = terminal;
        return Task.FromResult<IReadOnlyList<long>>(apartados);
    }

    public Task LiberarAsync(string terminal, IReadOnlyCollection<long> comprobanteIds, CancellationToken ct = default)
    {
        if (Error is not null)
            throw Error;

        foreach (var id in comprobanteIds)
        {
            if (Reservados.TryGetValue(id, out var pc) && pc == terminal)
                Reservados.Remove(id);
        }
        return Task.CompletedTask;
    }

    public Task<ResumenPendientes> ResumenPendientesAsync(CancellationToken ct = default)
    {
        if (Error is not null)
            throw Error;

        var cantidad = Pendientes().Count();
        return Task.FromResult(cantidad == 0 ? ResumenPendientes.Ninguno : new ResumenPendientes(cantidad, MasAntiguo));
    }

    /// <summary>Fecha que devuelve el resumen como la del pendiente más antiguo.</summary>
    public DateTimeOffset? MasAntiguo { get; set; } = DateTimeOffset.Now;

    private IEnumerable<long> Pendientes() =>
        Intentos.Keys.Concat(SinIntentos).Distinct().Where(id => Estado(id) == EstadoSunat.Pendiente).Order();
}
