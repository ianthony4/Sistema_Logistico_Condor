using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;

namespace Condor.Pruebas.Infraestructura;

/// <summary>
/// Datos de prueba comunes. Se toman de la lista de precios del local para que
/// los casos se parezcan a las ventas reales del mostrador.
/// </summary>
public static class DatosPrueba
{
    public static Producto Cemento() => Nuevo("Cemento Portland tipo I x 42.5 kg", 32.50m, "BLS");

    public static Producto Fierro() => Nuevo("Fierro corrugado 1/2 in x 9 m", 38.00m, "VAR");

    public static Producto EsmalteBlanco() => Nuevo("Esmalte sintético blanco 1 gal", 58.00m, "GAL");

    public static Producto EsmalteRojo() => Nuevo("Esmalte sintético rojo 1 gal", 58.00m, "GAL");

    public static Producto Clavos() => Nuevo("Clavo para madera 3 in x kg", 7.50m, "KG");

    public static IReadOnlyList<Producto> Catalogo() =>
        new[] { Cemento(), Fierro(), EsmalteBlanco(), EsmalteRojo(), Clavos() };

    public static Producto Nuevo(string nombre, decimal precio, string unidad = "UND") =>
        new() { Nombre = nombre, Precio = precio, Unidad = unidad };

    // Clientes (HU-03): un vecino con DNI y una constructora con RUC.
    public static Cliente ClienteDni(string? dni = null) => new()
    {
        TipoDocumento = TipoDocumento.Dni,
        NumeroDocumento = dni ?? "45879632",
        Nombre = "Juan Pérez Quispe",
        Direccion = "Calle Los Pinos 214, El Pedregal",
    };

    public static Cliente ClienteRuc(string? ruc = null) => new()
    {
        TipoDocumento = TipoDocumento.Ruc,
        NumeroDocumento = ruc ?? "20131312955",
        Nombre = "Constructora Valle de Majes S.A.C.",
        Direccion = "Av. Principal 450, Majes",
    };

    public static IReadOnlyList<Cliente> Clientes() => new[] { ClienteDni(), ClienteRuc() };

    /// <summary>Datos de la ferretería como los deja la migración V005.</summary>
    public static Empresa Empresa() => new()
    {
        Ruc = "20600000005",
        RazonSocial = "FERRETERÍA CÓNDOR MAJES S.A.C.",
        NombreComercial = "Ferretería Cóndor",
        Direccion = "Majes, Caylloma - Arequipa",
    };

    /// <summary>DNI al azar, para que las pruebas con base de datos no choquen entre ejecuciones.</summary>
    public static string DniNuevo() => Random.Shared.Next(10_000_000, 99_999_999).ToString();

    /// <summary>RUC de persona jurídica al azar, con su dígito verificador correcto.</summary>
    public static string RucNuevo()
    {
        var primerosDiez = "20" + Random.Shared.Next(10_000_000, 99_999_999);
        return primerosDiez + ValidadorDocumento.DigitoVerificadorRuc(primerosDiez);
    }

    /// <summary>
    /// Nombre único por ejecución, para que las pruebas de integración no
    /// choquen con productos que dejó una ejecución anterior.
    /// </summary>
    public static string NombreUnico(string prefijo) => $"{prefijo} {Guid.NewGuid():N}"[..Math.Min(prefijo.Length + 13, 150)];
}
