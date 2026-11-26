using System.Text.RegularExpressions;
using Condor.Dominio.Entidades;

namespace Condor.Dominio.Validacion;

/// <summary>
/// Reglas de un producto del catálogo (HU-01). Se aplican antes de guardar,
/// tanto desde el diálogo Nuevo producto como desde la carga inicial (T-19).
/// Desde el Sprint 5 valida también el código de barras (HU-13, T-86).
/// </summary>
public static partial class ValidadorProducto
{
    public const int LargoMaximoNombre = 150;
    public const decimal PrecioMaximo = 99_999.99m;

    /// <summary>Largo del código de barras: EAN-8 tiene 8, EAN-13 tiene 13; los códigos propios, de 4 a 30.</summary>
    public const int LargoMinimoCodigo = 4;
    public const int LargoMaximoCodigo = 30;

    /// <summary>Quita espacios sobrantes y pone la unidad en mayúsculas.</summary>
    public static Producto Normalizar(Producto producto)
    {
        producto.Nombre = EspaciosRepetidos().Replace(producto.Nombre ?? string.Empty, " ").Trim();
        producto.Unidad = (producto.Unidad ?? string.Empty).Trim().ToUpperInvariant();
        if (producto.Unidad.Length == 0)
            producto.Unidad = "UND";
        producto.CodigoBarras = NormalizarCodigo(producto.CodigoBarras);
        return producto;
    }

    /// <summary>Código de barras sin espacios y en mayúsculas; null si está vacío.</summary>
    public static string? NormalizarCodigo(string? codigo) =>
        string.IsNullOrWhiteSpace(codigo) ? null : codigo.Trim().ToUpperInvariant();

    /// <summary>
    /// Error del código de barras (HU-13); null si es válido o si no tiene.
    /// Solo letras, números y guiones: así lo envía el lector.
    /// </summary>
    public static string? ErrorCodigo(string? codigo)
    {
        codigo = NormalizarCodigo(codigo);
        if (codigo is null)
            return null;
        if (codigo.Length is < LargoMinimoCodigo or > LargoMaximoCodigo)
            return $"El código de barras debe tener entre {LargoMinimoCodigo} y {LargoMaximoCodigo} caracteres.";
        if (!CodigoValido().IsMatch(codigo))
            return "El código de barras solo puede tener letras, números y guiones.";
        return null;
    }

    /// <summary>
    /// True si el texto parece leído por el lector (sin espacios, con al menos
    /// un número y del largo de un código) y no un nombre escrito a mano.
    /// </summary>
    public static bool PareceCodigo(string? texto)
    {
        var codigo = NormalizarCodigo(texto);
        return codigo is not null && ErrorCodigo(codigo) is null && codigo.Any(char.IsAsciiDigit);
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

        if (ErrorCodigo(producto.CodigoBarras) is { } errorCodigo)
            errores.Add(errorCodigo);

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

    [GeneratedRegex("^[A-Z0-9-]+$")]
    private static partial Regex CodigoValido();
}

/// <summary>El producto no cumple las reglas del catálogo.</summary>
public sealed class ProductoInvalidoException : Exception
{
    public ProductoInvalidoException(IReadOnlyList<string> errores)
        : base(string.Join(" ", errores)) => Errores = errores;

    public IReadOnlyList<string> Errores { get; }
}

/// <summary>El código de barras ya es de otro producto (HU-13): el lector no sabría cuál vender.</summary>
public sealed class CodigoBarrasDuplicadoException : Exception
{
    public CodigoBarrasDuplicadoException(string codigo, string? producto)
        : base($"El código {codigo} ya es de {(producto is null ? "otro producto" : $"\"{producto}\"")}.") => Codigo = codigo;

    public string Codigo { get; }
}

/// <summary>Ya existe un producto con el mismo nombre (sin distinguir mayúsculas).</summary>
public sealed class ProductoDuplicadoException : Exception
{
    public ProductoDuplicadoException(string nombre)
        : base($"Ya existe un producto llamado \"{nombre}\". No se creó el duplicado.") => Nombre = nombre;

    public string Nombre { get; }
}
