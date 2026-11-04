using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;

namespace Condor.Pruebas.Infraestructura;

/// <summary>
/// Repositorio de comprobantes falso: numera cada serie desde 1, como la base
/// de datos central, y guarda los comprobantes en memoria.
/// </summary>
public sealed class ComprobanteRepositorioEnMemoria : IComprobanteRepositorio
{
    private readonly List<Comprobante> _comprobantes = new();
    private readonly Dictionary<string, long> _correlativos = new();

    /// <summary>Si se asigna, EmitirAsync lanza esta excepción (para simular errores o la red caída).</summary>
    public Exception? ErrorAlEmitir { get; set; }

    public IReadOnlyList<Comprobante> Comprobantes => _comprobantes;

    public Task<Comprobante> EmitirAsync(Comprobante comprobante, CancellationToken ct = default)
    {
        if (ErrorAlEmitir is not null)
            throw ErrorAlEmitir;

        _correlativos.TryGetValue(comprobante.SerieCodigo, out var ultimo);
        _correlativos[comprobante.SerieCodigo] = ++ultimo;

        comprobante.Id = _comprobantes.Count + 1;
        comprobante.Numero = ultimo;
        comprobante.Fecha = DateTimeOffset.Now;
        _comprobantes.Add(comprobante);
        return Task.FromResult(comprobante);
    }

    public Task<Comprobante?> ObtenerAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(_comprobantes.FirstOrDefault(c => c.Id == id));

    public Task<IReadOnlyList<Comprobante>> ListarRecientesAsync(int limite = 30, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Comprobante>>(_comprobantes.OrderByDescending(c => c.Id).Take(limite).ToList());

    /// <summary>Impresiones anotadas por comprobante. Como la base real, no toca el objeto en memoria.</summary>
    public Dictionary<long, int> ImpresionesRegistradas { get; } = new();

    public Task RegistrarImpresionAsync(long id, CancellationToken ct = default)
    {
        ImpresionesRegistradas[id] = ImpresionesRegistradas.GetValueOrDefault(id) + 1;
        return Task.CompletedTask;
    }
}
