using System.Text.RegularExpressions;
using Condor.Dominio.Entidades;

namespace Condor.Dominio.Validacion;

/// <summary>
/// Reglas de un proveedor (HU-04, T-36). Se aplican en el diálogo Nuevo
/// proveedor y otra vez en el repositorio, antes de guardarlo.
/// </summary>
public static partial class ValidadorProveedor
{
    public const int LargoMaximoRazonSocial = 200;
    public const int LargoMaximoNombreComercial = 150;
    public const int LargoMaximoDireccion = 250;
    public const int LargoMaximoTelefono = 30;
    public const int LargoMaximoCorreo = 120;
    public const int LargoMaximoContacto = 120;

    /// <summary>Limpia el RUC y quita espacios sobrantes; los campos opcionales vacíos quedan en null.</summary>
    public static Proveedor Normalizar(Proveedor proveedor)
    {
        proveedor.Ruc = ValidadorDocumento.Limpiar(proveedor.Ruc);
        proveedor.RazonSocial = Compactar(proveedor.RazonSocial) ?? string.Empty;
        proveedor.NombreComercial = Compactar(proveedor.NombreComercial);
        proveedor.Direccion = Compactar(proveedor.Direccion);
        proveedor.Telefono = Compactar(proveedor.Telefono);
        proveedor.Correo = Compactar(proveedor.Correo)?.ToLowerInvariant();
        proveedor.Contacto = Compactar(proveedor.Contacto);
        return proveedor;
    }

    /// <summary>Lista de errores en español; vacía si el proveedor es válido.</summary>
    public static IReadOnlyList<string> Errores(Proveedor proveedor)
    {
        var errores = new List<string>();

        // El proveedor emite facturas: siempre tiene RUC, y debe ser válido.
        if (ValidadorDocumento.Error(TipoDocumento.Ruc, proveedor.Ruc) is { } error)
            errores.Add(error);

        if (string.IsNullOrWhiteSpace(proveedor.RazonSocial))
            errores.Add("La razón social es obligatoria.");
        else if (proveedor.RazonSocial.Trim().Length > LargoMaximoRazonSocial)
            errores.Add($"La razón social no puede pasar de {LargoMaximoRazonSocial} caracteres.");

        Largo(errores, proveedor.NombreComercial, LargoMaximoNombreComercial, "El nombre comercial");
        Largo(errores, proveedor.Direccion, LargoMaximoDireccion, "La dirección");
        Largo(errores, proveedor.Contacto, LargoMaximoContacto, "El contacto");

        if (!string.IsNullOrWhiteSpace(proveedor.Telefono))
        {
            if (proveedor.Telefono.Trim().Length > LargoMaximoTelefono)
                errores.Add($"El teléfono no puede pasar de {LargoMaximoTelefono} caracteres.");
            else if (!Telefono().IsMatch(proveedor.Telefono.Trim()))
                errores.Add("El teléfono solo admite números, espacios, guiones, paréntesis y +.");
        }

        if (!string.IsNullOrWhiteSpace(proveedor.Correo))
        {
            if (proveedor.Correo.Trim().Length > LargoMaximoCorreo)
                errores.Add($"El correo no puede pasar de {LargoMaximoCorreo} caracteres.");
            else if (!Correo().IsMatch(proveedor.Correo.Trim()))
                errores.Add("El correo no es válido (ejemplo: ventas@proveedor.com).");
        }

        return errores;
    }

    /// <summary>Normaliza el proveedor y lanza ProveedorInvalidoException si no es válido.</summary>
    public static Proveedor Validar(Proveedor proveedor)
    {
        Normalizar(proveedor);
        var errores = Errores(proveedor);
        if (errores.Count > 0)
            throw new ProveedorInvalidoException(errores);
        return proveedor;
    }

    private static void Largo(List<string> errores, string? valor, int maximo, string etiqueta)
    {
        if (valor?.Trim().Length > maximo)
            errores.Add($"{etiqueta} no puede pasar de {maximo} caracteres.");
    }

    private static string? Compactar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : EspaciosRepetidos().Replace(texto, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex EspaciosRepetidos();

    // 054 586214, 959-123-456, (054) 586214, +51 959 123 456
    [GeneratedRegex(@"^\+?[0-9 ()\-]{6,}$")]
    private static partial Regex Telefono();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$")]
    private static partial Regex Correo();
}

/// <summary>El proveedor no cumple las reglas (RUC, razón social, teléfono o correo).</summary>
public sealed class ProveedorInvalidoException : Exception
{
    public ProveedorInvalidoException(IReadOnlyList<string> errores)
        : base(string.Join(" ", errores)) => Errores = errores;

    public IReadOnlyList<string> Errores { get; }
}

/// <summary>Ya hay un proveedor registrado con el mismo RUC (HU-04).</summary>
public sealed class ProveedorDuplicadoException : Exception
{
    public ProveedorDuplicadoException(string ruc)
        : base($"Ya existe un proveedor con RUC {ruc}. Búsquelo en la lista de proveedores.") => Ruc = ruc;

    public string Ruc { get; }
}
