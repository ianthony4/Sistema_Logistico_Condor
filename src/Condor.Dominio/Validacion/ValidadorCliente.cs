using System.Text.RegularExpressions;
using Condor.Dominio.Entidades;

namespace Condor.Dominio.Validacion;

/// <summary>
/// Reglas de un cliente (HU-03, T-21). Se aplican antes de guardarlo, tanto
/// desde el diálogo Nuevo cliente como desde el repositorio.
/// </summary>
public static partial class ValidadorCliente
{
    public const int LargoMaximoNombre = 200;
    public const int LargoMaximoDireccion = 250;

    /// <summary>Limpia el número y quita espacios sobrantes del nombre y la dirección.</summary>
    public static Cliente Normalizar(Cliente cliente)
    {
        cliente.NumeroDocumento = ValidadorDocumento.Limpiar(cliente.NumeroDocumento);
        cliente.Nombre = EspaciosRepetidos().Replace(cliente.Nombre ?? string.Empty, " ").Trim();
        cliente.Direccion = string.IsNullOrWhiteSpace(cliente.Direccion)
            ? null
            : EspaciosRepetidos().Replace(cliente.Direccion, " ").Trim();
        return cliente;
    }

    /// <summary>Lista de errores en español; vacía si el cliente es válido.</summary>
    public static IReadOnlyList<string> Errores(Cliente cliente)
    {
        var errores = new List<string>();

        if (cliente.TipoDocumento == TipoDocumento.SinDocumento)
            errores.Add("Elija DNI o RUC: el cliente se registra con su documento.");
        else if (ValidadorDocumento.Error(cliente.TipoDocumento, cliente.NumeroDocumento) is { } error)
            errores.Add(error);

        var etiqueta = cliente.TipoDocumento == TipoDocumento.Ruc ? "La razón social" : "El nombre";
        if (string.IsNullOrWhiteSpace(cliente.Nombre))
            errores.Add($"{etiqueta} es obligatorio.");
        else if (cliente.Nombre.Trim().Length > LargoMaximoNombre)
            errores.Add($"{etiqueta} no puede pasar de {LargoMaximoNombre} caracteres.");

        if (cliente.Direccion?.Trim().Length > LargoMaximoDireccion)
            errores.Add($"La dirección no puede pasar de {LargoMaximoDireccion} caracteres.");

        return errores;
    }

    /// <summary>Normaliza el cliente y lanza ClienteInvalidoException si no es válido.</summary>
    public static Cliente Validar(Cliente cliente)
    {
        Normalizar(cliente);
        var errores = Errores(cliente);
        if (errores.Count > 0)
            throw new ClienteInvalidoException(errores);
        return cliente;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex EspaciosRepetidos();
}

/// <summary>El cliente no cumple las reglas (documento, nombre o dirección).</summary>
public sealed class ClienteInvalidoException : Exception
{
    public ClienteInvalidoException(IReadOnlyList<string> errores)
        : base(string.Join(" ", errores)) => Errores = errores;

    public IReadOnlyList<string> Errores { get; }
}

/// <summary>Ya hay un cliente registrado con el mismo DNI o RUC.</summary>
public sealed class ClienteDuplicadoException : Exception
{
    public ClienteDuplicadoException(TipoDocumento tipo, string numero)
        : base($"Ya existe un cliente con {Cliente.Etiqueta(tipo)} {numero}. Búsquelo con Buscar.")
    {
        TipoDocumento = tipo;
        NumeroDocumento = numero;
    }

    public TipoDocumento TipoDocumento { get; }

    public string NumeroDocumento { get; }
}
