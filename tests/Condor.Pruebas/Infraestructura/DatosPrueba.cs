using Condor.Dominio.Entidades;

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

    /// <summary>
    /// Nombre único por ejecución, para que las pruebas de integración no
    /// choquen con productos que dejó una ejecución anterior.
    /// </summary>
    public static string NombreUnico(string prefijo) => $"{prefijo} {Guid.NewGuid():N}"[..Math.Min(prefijo.Length + 13, 150)];
}
