namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Módulos del menú lateral (T-73). Nueva venta es la pantalla de siempre;
/// los demás se muestran en el panel derecho de la misma ventana, sin abrir
/// ventanas aparte.
/// </summary>
public enum Modulo
{
    Venta,
    Catalogo,
    Clientes,
    Compras,
    Historial,
    Proveedores,
    Proformas,
    Guias,
    Respaldo,
}

public static class ModuloMenu
{
    /// <summary>Nombre del módulo tal como sale en el menú.</summary>
    public static string Nombre(this Modulo modulo) => modulo switch
    {
        Modulo.Venta => "Nueva venta",
        Modulo.Catalogo => "Catálogo",
        Modulo.Clientes => "Clientes",
        Modulo.Compras => "Compras",
        Modulo.Historial => "Historial",
        Modulo.Proveedores => "Proveedores",
        Modulo.Proformas => "Proformas",
        Modulo.Guias => "Guías",
        Modulo.Respaldo => "Respaldo",
        _ => modulo.ToString(),
    };

    /// <summary>Módulo de cada tecla del menú: F1 Nueva venta ... F9 Respaldo; null si la tecla no es de un módulo.</summary>
    public static Modulo? DeTecla(int numeroF) => numeroF switch
    {
        1 => Modulo.Venta,
        2 => Modulo.Catalogo,
        3 => Modulo.Clientes,
        4 => Modulo.Compras,
        5 => Modulo.Historial,
        6 => Modulo.Proveedores,
        7 => Modulo.Proformas,
        8 => Modulo.Guias,
        9 => Modulo.Respaldo,
        _ => null,
    };
}
