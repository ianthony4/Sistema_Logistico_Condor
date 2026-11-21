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

    /// <summary>True para la boleta y la factura, las únicas que van a la SUNAT.</summary>
    public static bool EsElectronico(string tipo) => tipo is Factura or Boleta;
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

    /// <summary>
    /// Lee un número buscado como "PR01-00000004", "pr01-4" o solo "4". Devuelve
    /// false si el texto no es un número de comprobante; la serie es null si
    /// solo se digitó el correlativo.
    /// </summary>
    public static bool IntentarLeerNumero(string? texto, out string? codigo, out long numero)
    {
        codigo = null;
        numero = 0;
        var partes = (texto ?? string.Empty).Trim().ToUpperInvariant().Split('-');
        var correlativo = partes[^1];
        if (partes.Length > 2 || correlativo.Length is 0 or > 8 || !correlativo.All(char.IsAsciiDigit))
            return false;
        if (partes.Length == 2)
        {
            if (partes[0].Length != 4 || !partes[0].All(char.IsAsciiLetterOrDigit))
                return false;
            codigo = partes[0];
        }
        numero = long.Parse(correlativo);
        return numero > 0;
    }
}
