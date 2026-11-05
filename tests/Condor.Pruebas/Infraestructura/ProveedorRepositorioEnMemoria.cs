using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;

namespace Condor.Pruebas.Infraestructura;

/// <summary>
/// Repositorio de proveedores falso para probar los ViewModel sin base de
/// datos. Aplica las mismas reglas que el real: valida y no admite RUC repetidos.
/// </summary>
public sealed class ProveedorRepositorioEnMemoria : IProveedorRepositorio
{
    private readonly List<Proveedor> _proveedores = new();
    private long _siguienteId = 1;

    public ProveedorRepositorioEnMemoria(IEnumerable<Proveedor>? iniciales = null)
    {
        foreach (var p in iniciales ?? Enumerable.Empty<Proveedor>())
            RegistrarAsync(p).GetAwaiter().GetResult();
    }

    /// <summary>Si se asigna, todas las operaciones lanzan esta excepción (para simular la red caída).</summary>
    public Exception? Error { get; set; }

    public int Busquedas { get; private set; }

    public IReadOnlyList<Proveedor> Proveedores => _proveedores;

    public Task<Proveedor> RegistrarAsync(Proveedor proveedor, CancellationToken ct = default)
    {
        if (Error is not null)
            throw Error;

        ValidadorProveedor.Validar(proveedor);
        if (_proveedores.Any(p => p.Ruc == proveedor.Ruc))
            throw new ProveedorDuplicadoException(proveedor.Ruc);

        proveedor.Id = _siguienteId++;
        proveedor.Activo = true;
        proveedor.CreadoEn = DateTimeOffset.Now;
        _proveedores.Add(proveedor);
        return Task.FromResult(proveedor);
    }

    public Task<Proveedor?> BuscarPorRucAsync(string ruc, CancellationToken ct = default)
    {
        if (Error is not null)
            throw Error;

        ruc = ValidadorDocumento.Limpiar(ruc);
        return Task.FromResult(_proveedores.FirstOrDefault(p => p.Ruc == ruc));
    }

    public Task<IReadOnlyList<Proveedor>> BuscarAsync(string? texto, int limite = 200, CancellationToken ct = default)
    {
        if (Error is not null)
            throw Error;

        Busquedas++;
        texto = texto?.Trim() ?? string.Empty;
        IReadOnlyList<Proveedor> encontrados = _proveedores
            .Where(p => p.Activo)
            .Where(p => texto.Length == 0
                        || p.Ruc.StartsWith(texto, StringComparison.Ordinal)
                        || SinTildes($"{p.RazonSocial} {p.NombreComercial}").Contains(SinTildes(texto), StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.RazonSocial, StringComparer.CurrentCultureIgnoreCase)
            .Take(limite)
            .ToList();
        return Task.FromResult(encontrados);
    }

    private static string SinTildes(string texto) => new(texto
        .Normalize(System.Text.NormalizationForm.FormD)
        .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
        .ToArray());
}
