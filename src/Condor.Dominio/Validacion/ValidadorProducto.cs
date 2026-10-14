using System.Text.RegularExpressions;
using Condor.Dominio.Entidades;

namespace Condor.Dominio.Validacion;

/// <summary>
/// Reglas de un producto del catálogo (HU-01). Se aplican antes de guardar,
/// tanto desde el diálogo Nuevo producto como desde la carga inicial (T-19).
/// </summary>
public static partial class ValidadorProducto
{
    public const int LargoMaximoNombre = 150;
    public const decimal PrecioMaximo = 99_999.99m;

    /// <summary>Quita espacios sobrantes y pone la unidad en mayúsculas.</summary>
    public static Producto Normalizar(Producto producto)
    {
        producto.Nombre = EspaciosRepetidos().Replace(producto.Nombre ?? string.Empty, " ").Trim();
        producto.Unidad = (producto.Unidad ?? string.Empty).Trim().ToUpperInvariant();
        if (producto.Unidad.Length == 0)
            producto.Unidad = "UND";
        producto.CodigoBarras = string.IsNullOrWhiteSpace(producto.CodigoBarras) ? null : producto.CodigoBarras.Trim();
        return producto;
    }

    /// <summary>Lista de errores en español; vacía si el producto es válido.</summary>
    public static IReadOnlyList<string> Errores(Producto producto)
    {
        var errores = new List<string>();

        if (string.IsNullOrWhiteSpace(producto.Nombre))
            errores.Add("El nombre es obligatorio.");
        else if (producto.Nombre.Trim().Length > LargoMaximoNombre)
            errores.Add($"El nombre no puede pasar de {LargoMaximoNombre} caracteres.");

        if (producto.Precio <= 0)
            errores.Add("El precio debe ser mayor que cero.");
        else if (producto.Precio > PrecioMaximo)
            errores.Add($"El precio no puede pasar de S/ {PrecioMaximo:N2}.");
        else if (decimal.Round(producto.Precio, 2) != producto.Precio)
            errores.Add("El precio solo puede tener dos decimales.");

        if (producto.Unidad?.Trim().Length > 10)
            errores.Add("La unidad no puede pasar de 10 caracteres.");

        return errores;
    }

    /// <summary>Normaliza el producto y lanza ProductoInvalidoException si no es válido.</summary>
    public static Producto Validar(Producto producto)
    {
        Normalizar(producto);
        var errores = Errores(producto);
        if (errores.Count > 0)
            throw new ProductoInvalidoException(errores);
        return producto;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex EspaciosRepetidos();
}

/// <summary>El producto no cumple las reglas del catálogo.</summary>
public sealed class ProductoInvalidoException : Exception
{
    public ProductoInvalidoException(IReadOnlyList<string> errores)
        : base(string.Join(" ", errores)) => Errores = errores;

    public IReadOnlyList<string> Errores { get; }
}

/// <summary>Ya existe un producto con el mismo nombre (sin distinguir mayúsculas).</summary>
public sealed class ProductoDuplicadoException : Exception
{
    public ProductoDuplicadoException(string nombre)
        : base($"Ya existe un producto llamado \"{nombre}\". No se creó el duplicado.") => Nombre = nombre;

    public string Nombre { get; }
}
