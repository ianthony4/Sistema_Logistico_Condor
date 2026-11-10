using Condor.Dominio.Contratos;
using Condor.Dominio.Sunat;

namespace Condor.Pruebas.Infraestructura;

/// <summary>
/// Respuestas de la SUNAT en memoria. Como el real, un estado definitivo ya
/// no se cambia y cada respuesta o falla suma un intento.
/// </summary>
public sealed class EnvioSunatRepositorioEnMemoria : IEnvioSunatRepositorio
{
    /// <summary>Si se asigna, las operaciones lanzan esta excepción (para simular la red caída).</summary>
    public Exception? Error { get; set; }

    public Dictionary<long, RespuestaSunat> Respuestas { get; } = new();

    public Dictionary<long, string> UltimoError { get; } = new();

    public Dictionary<long, int> Intentos { get; } = new();

    public string Estado(long id) => Respuestas.TryGetValue(id, out var r) ? r.Estado : EstadoSunat.Pendiente;

    public Task RegistrarRespuestaAsync(long comprobanteId, RespuestaSunat respuesta, CancellationToken ct = default)
    {
        if (Error is not null)
            throw Error;

        Intentos[comprobanteId] = Intentos.GetValueOrDefault(comprobanteId) + 1;
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
        if (!EstadoSunat.EsDefinitivo(Estado(comprobanteId)))
            UltimoError[comprobanteId] = motivo;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<long>> ListarPendientesAsync(int limite = 100, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<long>>(Intentos.Keys.Where(id => Estado(id) == EstadoSunat.Pendiente).Order().Take(limite).ToList());
}
