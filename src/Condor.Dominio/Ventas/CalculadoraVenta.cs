namespace Condor.Dominio.Ventas;

/// <summary>Totales de un comprobante.</summary>
public readonly record struct TotalesVenta(decimal OpGravada, decimal Igv, decimal Total)
{
    public static TotalesVenta Cero => new(0m, 0m, 0m);
}

/// <summary>
/// Cálculo de importes, operación gravada, IGV y total (HU-02, T-17).
/// </summary>
/// <remarks>
/// Los precios del catálogo incluyen IGV. Por eso el total es la suma de los
/// importes y la operación gravada se obtiene dividiendo entre 1.18; el IGV es
/// la diferencia, de modo que gravada + IGV siempre da exactamente el total.
/// Ejemplo del wireframe: total 745.00 = op. gravada 631.36 + IGV 113.64.
/// </remarks>
public static class CalculadoraVenta
{
    public const decimal TasaIgv = 0.18m;

    /// <summary>Importe de una línea: cantidad por precio unitario, a dos decimales.</summary>
    public static decimal Importe(decimal cantidad, decimal precioUnitario)
    {
        if (cantidad <= 0)
            throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad debe ser mayor que cero.");
        if (precioUnitario <= 0)
            throw new ArgumentOutOfRangeException(nameof(precioUnitario), "El precio debe ser mayor que cero.");

        return Redondear(cantidad * precioUnitario);
    }

    /// <summary>Totales a partir de los importes (con IGV) de las líneas.</summary>
    public static TotalesVenta Totales(IEnumerable<decimal> importes)
    {
        var total = Redondear(importes.Sum());
        if (total == 0m)
            return TotalesVenta.Cero;

        var opGravada = Redondear(total / (1m + TasaIgv));
        return new TotalesVenta(opGravada, total - opGravada, total);
    }

    /// <summary>Redondeo comercial a céntimos (0.005 sube).</summary>
    public static decimal Redondear(decimal monto) => Math.Round(monto, 2, MidpointRounding.AwayFromZero);
}
