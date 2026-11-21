using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Ventas;

namespace Condor.Pruebas.Infraestructura;

/// <summary>
/// Repositorio de comprobantes falso: numera cada serie desde 1, como la base
/// de datos central, y guarda los comprobantes en memoria. Como V011, da la
/// vigencia a la proforma y no deja convertirla dos veces (HU-09).
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

        var proforma = _comprobantes.FirstOrDefault(c => comprobante.ProformaId == c.Id);
        if (proforma is not null && _comprobantes.Any(c => c.ProformaId == proforma.Id))
            throw new ProformaYaConvertidaException(proforma.NumeroCompleto);

        _correlativos.TryGetValue(comprobante.SerieCodigo, out var ultimo);
        _correlativos[comprobante.SerieCodigo] = ++ultimo;

        comprobante.Id = _comprobantes.Count + 1;
        comprobante.Numero = ultimo;
        comprobante.Fecha = DateTimeOffset.Now;
        if (comprobante.TipoComprobante == TipoComprobante.Proforma)
            comprobante.ValidaHasta = EmisionComprobante.VigenciaProforma(EmisionComprobante.FechaPeru(comprobante.Fecha));
        _comprobantes.Add(comprobante);
        if (proforma is not null)
            proforma.ConvertidaEn = comprobante.NumeroCompleto;
        return Task.FromResult(comprobante);
    }

    public Task<Comprobante?> ObtenerAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(_comprobantes.FirstOrDefault(c => c.Id == id));

    public Task<IReadOnlyList<Comprobante>> ListarRecientesAsync(int limite = 30, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Comprobante>>(_comprobantes.OrderByDescending(c => c.Id).Take(limite).ToList());

    public Task<IReadOnlyList<Comprobante>> ListarProformasAsync(string? texto, int limite = 100, CancellationToken ct = default)
    {
        texto = texto?.Trim() ?? string.Empty;
        var esNumero = Serie.IntentarLeerNumero(texto, out var serie, out var numero);
        IReadOnlyList<Comprobante> proformas = _comprobantes
            .Where(c => c.TipoComprobante == TipoComprobante.Proforma)
            .Where(c => texto.Length == 0
                        || (esNumero && c.Numero == numero && (serie is null || c.SerieCodigo == serie))
                        || (c.Cliente?.NumeroDocumento.StartsWith(texto, StringComparison.Ordinal) ?? false)
                        || (c.Cliente?.Nombre.Contains(texto, StringComparison.CurrentCultureIgnoreCase) ?? false))
            .OrderByDescending(c => c.Id)
            .Take(limite)
            .ToList();
        return Task.FromResult(proformas);
    }

    /// <summary>Impresiones anotadas por comprobante. Como la base real, no toca el objeto en memoria.</summary>
    public Dictionary<long, int> ImpresionesRegistradas { get; } = new();

    public Task RegistrarImpresionAsync(long id, CancellationToken ct = default)
    {
        ImpresionesRegistradas[id] = ImpresionesRegistradas.GetValueOrDefault(id) + 1;
        return Task.CompletedTask;
    }
}
