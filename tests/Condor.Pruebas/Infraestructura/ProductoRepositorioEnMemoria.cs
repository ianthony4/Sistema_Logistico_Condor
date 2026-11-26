using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;

namespace Condor.Pruebas.Infraestructura;

/// <summary>
/// Repositorio falso para probar los ViewModel sin base de datos.
/// </summary>
public sealed class ProductoRepositorioEnMemoria : IProductoRepositorio
{
    private readonly List<Producto> _productos = new();
    private long _siguienteId = 1;

    public ProductoRepositorioEnMemoria(IEnumerable<Producto>? iniciales = null)
    {
        foreach (var p in iniciales ?? Enumerable.Empty<Producto>())
            RegistrarAsync(p).GetAwaiter().GetResult();
    }

    /// <summary>Si se asigna, RegistrarAsync lanza esta excepción (para simular errores).</summary>
    public Exception? ErrorAlRegistrar { get; set; }

    public int Busquedas { get; private set; }

    public IReadOnlyList<Producto> Productos => _productos;

    public Task<Producto> RegistrarAsync(Producto producto, CancellationToken ct = default)
    {
        if (ErrorAlRegistrar is not null)
            throw ErrorAlRegistrar;

        ValidadorProducto.Normalizar(producto);
        if (producto.CodigoBarras is { } codigo && _productos.FirstOrDefault(p => p.CodigoBarras == codigo) is { } otro)
            throw new CodigoBarrasDuplicadoException(codigo, otro.Nombre);

        producto.Id = _siguienteId++;
        _productos.Add(producto);
        return Task.FromResult(producto);
    }

    public Task<IReadOnlyList<Producto>> BuscarPorNombreAsync(string texto, int limite = 20, CancellationToken ct = default)
    {
        Busquedas++;
        IReadOnlyList<Producto> encontrados = _productos
            .Where(p => p.Nombre.Contains(texto, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => !p.Nombre.StartsWith(texto, StringComparison.OrdinalIgnoreCase))
            .ThenBy(p => p.Nombre)
            .Take(limite)
            .ToList();
        return Task.FromResult(encontrados);
    }

    public Task<IReadOnlyList<Producto>> ListarAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Producto>>(_productos.OrderBy(p => p.Nombre).ToList());

    public Task<Producto?> BuscarPorCodigoBarrasAsync(string codigo, CancellationToken ct = default)
    {
        var normalizado = ValidadorProducto.NormalizarCodigo(codigo);
        return Task.FromResult(_productos.FirstOrDefault(p => normalizado is not null && p.CodigoBarras == normalizado));
    }

    public Task<Producto> AsignarCodigoBarrasAsync(long productoId, string? codigo, CancellationToken ct = default)
    {
        var normalizado = ValidadorProducto.NormalizarCodigo(codigo);
        if (ValidadorProducto.ErrorCodigo(normalizado) is { } error)
            throw new ProductoInvalidoException(new[] { error });
        if (_productos.FirstOrDefault(p => normalizado is not null && p.CodigoBarras == normalizado && p.Id != productoId) is { } otro)
            throw new CodigoBarrasDuplicadoException(normalizado!, otro.Nombre);

        var producto = _productos.Single(p => p.Id == productoId);
        producto.CodigoBarras = normalizado;
        return Task.FromResult(producto);
    }
}
