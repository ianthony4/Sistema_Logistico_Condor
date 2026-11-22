using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;

namespace Condor.Pruebas.Infraestructura;

/// <summary>
/// Compras en memoria para probar los ViewModel sin base de datos (HU-11).
/// Como el real, no admite dos veces el mismo comprobante de un proveedor y
/// el total del filtro es de todas las compras, aunque la lista se corte.
/// </summary>
public sealed class CompraRepositorioEnMemoria : ICompraRepositorio
{
    private readonly List<Compra> _compras = new();

    public CompraRepositorioEnMemoria(IEnumerable<Compra>? iniciales = null)
    {
        foreach (var c in iniciales ?? Enumerable.Empty<Compra>())
            RegistrarAsync(c).GetAwaiter().GetResult();
    }

    /// <summary>Si se asigna, todas las operaciones lanzan esta excepción (para simular la red caída).</summary>
    public Exception? Error { get; set; }

    public IReadOnlyList<Compra> Compras => _compras;

    /// <summary>Último filtro con que se buscó, para comprobar lo que pidió la ventana.</summary>
    public FiltroCompras? UltimoFiltro { get; private set; }

    public Task<Compra> RegistrarAsync(Compra compra, CancellationToken ct = default)
    {
        if (Error is not null)
            throw Error;
        if (_compras.Any(c => c.ProveedorId == compra.ProveedorId && c.TipoDocumento == compra.TipoDocumento
                              && c.Serie == compra.Serie && c.Numero == compra.Numero))
            throw new CompraDuplicadaException(compra.DocumentoCompleto, compra.Proveedor?.RazonSocial);

        compra.Id = _compras.Count + 1;
        compra.RegistradaEn = DateTimeOffset.Now;
        _compras.Add(compra);
        return Task.FromResult(compra);
    }

    public Task<Compra?> ObtenerAsync(long id, CancellationToken ct = default)
    {
        if (Error is not null)
            throw Error;
        return Task.FromResult(_compras.FirstOrDefault(c => c.Id == id));
    }

    public Task<ResultadoCompras> BuscarAsync(FiltroCompras filtro, CancellationToken ct = default)
    {
        if (Error is not null)
            throw Error;

        UltimoFiltro = filtro;
        var texto = filtro.Proveedor?.Trim() ?? string.Empty;
        var encontradas = _compras
            .Where(c => filtro.Desde is null || c.FechaEmision >= filtro.Desde)
            .Where(c => filtro.Hasta is null || c.FechaEmision <= filtro.Hasta)
            .Where(c => texto.Length == 0
                        || (c.Proveedor?.Ruc.StartsWith(texto, StringComparison.Ordinal) ?? false)
                        || ($"{c.Proveedor?.RazonSocial} {c.Proveedor?.NombreComercial}").Contains(texto, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(c => c.FechaEmision)
            .ThenByDescending(c => c.Id)
            .ToList();
        return Task.FromResult(encontradas.Count == 0
            ? ResultadoCompras.Ninguna
            : new ResultadoCompras(encontradas.Take(filtro.Limite).ToList(), encontradas.Count, encontradas.Sum(c => c.Total)));
    }
}
