namespace Condor.Dominio.Entidades;

/// <summary>
/// Producto del catálogo. No controla stock (HU-01).
/// </summary>
public sealed class Producto
{
    public long Id { get; set; }

    public string Nombre { get; set; } = string.Empty;

    /// <summary>Precio de venta con IGV incluido, en soles.</summary>
    public decimal Precio { get; set; }

    /// <summary>Unidad de medida: UND, KG, M, GAL, etc.</summary>
    public string Unidad { get; set; } = "UND";

    /// <summary>Reservado para el lector de código de barras (HU-13, futuro).</summary>
    public string? CodigoBarras { get; set; }

    public bool Activo { get; set; } = true;

    public DateTimeOffset CreadoEn { get; set; }

    public override string ToString() => $"{Nombre} - S/ {Precio:0.00}";
}
