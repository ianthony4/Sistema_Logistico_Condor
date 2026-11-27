using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Dominio.Ventas;

namespace Condor.Pruebas.Infraestructura;

/// <summary>
/// Guías de remisión en memoria (HU-10): numera la T001 desde 1 y, como el
/// real, un estado SUNAT definitivo ya no cambia y el ticket se conserva.
/// </summary>
public sealed class GuiaRemisionRepositorioEnMemoria : IGuiaRemisionRepositorio
{
    private readonly List<GuiaRemision> _guias = new();

    /// <summary>Si se asigna, las operaciones lanzan esta excepción (para simular la red caída).</summary>
    public Exception? Error { get; set; }

    public IReadOnlyList<GuiaRemision> Guias => _guias;

    public Dictionary<long, int> Intentos { get; } = new();

    public Task<GuiaRemision> EmitirAsync(GuiaRemision guia, CancellationToken ct = default)
    {
        Fallar();
        guia.Id = _guias.Count + 1;
        guia.Numero = _guias.Count + 1;
        guia.Fecha = DateTimeOffset.Now;
        guia.EstadoSunat = EstadoSunat.Pendiente;
        _guias.Add(guia);
        return Task.FromResult(guia);
    }

    public Task<GuiaRemision?> ObtenerAsync(long id, CancellationToken ct = default)
    {
        Fallar();
        return Task.FromResult(_guias.FirstOrDefault(g => g.Id == id));
    }

    public Task<IReadOnlyList<GuiaRemision>> BuscarAsync(FiltroGuias filtro, CancellationToken ct = default)
    {
        Fallar();
        var texto = filtro.Texto?.Trim() ?? string.Empty;
        var esNumero = Serie.IntentarLeerNumero(texto, out var serie, out var numero);
        IReadOnlyList<GuiaRemision> encontradas = _guias
            .Where(g => filtro.Desde is null || EmisionComprobante.FechaPeru(g.Fecha) >= filtro.Desde)
            .Where(g => filtro.Hasta is null || EmisionComprobante.FechaPeru(g.Fecha) <= filtro.Hasta)
            .Where(g => texto.Length == 0
                        || (esNumero && g.Numero == numero && (serie is null || g.SerieCodigo == serie))
                        || g.DestinatarioDocumento.StartsWith(texto, StringComparison.Ordinal)
                        || g.DestinatarioNombre.Contains(texto, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(g => g.Id)
            .Take(filtro.Limite)
            .ToList();
        return Task.FromResult(encontradas);
    }

    public Task RegistrarRespuestaAsync(long id, RespuestaSunat respuesta, CancellationToken ct = default)
    {
        Fallar();
        var guia = _guias.Single(g => g.Id == id);
        Intentos[id] = Intentos.GetValueOrDefault(id) + 1;
        guia.Ticket = respuesta.Ticket ?? guia.Ticket;
        if (!EstadoSunat.EsDefinitivo(guia.EstadoSunat))
        {
            guia.EstadoSunat = respuesta.Estado;
            guia.MensajeSunat = respuesta.Descripcion;
        }
        return Task.CompletedTask;
    }

    public Task RegistrarFallaAsync(long id, string motivo, CancellationToken ct = default)
    {
        Fallar();
        var guia = _guias.Single(g => g.Id == id);
        Intentos[id] = Intentos.GetValueOrDefault(id) + 1;
        if (!EstadoSunat.EsDefinitivo(guia.EstadoSunat))
            guia.MensajeSunat = motivo;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GuiaRemision>> ListarPendientesAsync(int limite = 50, CancellationToken ct = default)
    {
        Fallar();
        return Task.FromResult<IReadOnlyList<GuiaRemision>>(_guias.Where(g => g.EstadoSunat == EstadoSunat.Pendiente).Take(limite).ToList());
    }

    /// <summary>Impresiones anotadas por guía. Como la base real, no toca el objeto en memoria.</summary>
    public Dictionary<long, int> ImpresionesRegistradas { get; } = new();

    public Task RegistrarImpresionAsync(long id, CancellationToken ct = default)
    {
        ImpresionesRegistradas[id] = ImpresionesRegistradas.GetValueOrDefault(id) + 1;
        return Task.CompletedTask;
    }

    private void Fallar()
    {
        if (Error is not null)
            throw Error;
    }
}
