namespace Condor.Dominio.Entidades;

/// <summary>
/// Tipo de comprobante. Factura y boleta usan el código del catálogo 01 de la SUNAT.
/// </summary>
public static class TipoComprobante
{
    public const string Factura = "01";
    public const string Boleta = "03";
    public const string NotaVenta = "NV";
    public const string Proforma = "PR";
}

/// <summary>
/// Serie de numeración (B001, F001...). El último correlativo vive en la
/// base de datos central para que las tres PC no repitan números (HU-16).
/// </summary>
public sealed class Serie
{
    public int Id { get; set; }

    public string TipoComprobante { get; set; } = Entidades.TipoComprobante.Boleta;

    public string Codigo { get; set; } = string.Empty;

    public long UltimoCorrelativo { get; set; }

    /// <summary>Formato impreso: B001-00000123 (hasta 8 dígitos según la SUNAT).</summary>
    public static string Formatear(string codigo, long numero) => $"{codigo}-{numero:D8}";
}
