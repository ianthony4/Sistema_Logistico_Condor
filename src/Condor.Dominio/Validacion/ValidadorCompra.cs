using System.Text.RegularExpressions;
using Condor.Dominio.Entidades;
using Condor.Dominio.Ventas;

namespace Condor.Dominio.Validacion;

/// <summary>Campo del diálogo Nueva compra al que apunta un error, para señalarlo.</summary>
public enum CampoCompra
{
    Proveedor,
    Documento,
    Fecha,
    Detalle,
}

public sealed record ErrorCompra(CampoCompra Campo, string Mensaje);

/// <summary>
/// Reglas para registrar una compra (HU-11, T-80): proveedor registrado,
/// serie y número del comprobante del proveedor, fecha que no sea futura y al
/// menos un producto con cantidad y costo. Los importes, la operación gravada
/// y el IGV se calculan igual que en la venta (los costos incluyen IGV).
/// </summary>
public static partial class ValidadorCompra
{
    /// <summary>Una compra de hace más de un año casi siempre es un error de digitación.</summary>
    public const int DiasAntiguedadMaxima = 366;

    public static IReadOnlyList<ErrorCompra> Errores(Compra compra, DateOnly hoy)
    {
        var errores = new List<ErrorCompra>();

        if (compra.ProveedorId <= 0)
            errores.Add(new(CampoCompra.Proveedor, "Elija al proveedor: búsquelo por su RUC o regístrelo en Proveedores (F6)."));

        if (compra.TipoDocumento is not (TipoDocumentoCompra.Factura or TipoDocumentoCompra.Boleta))
            errores.Add(new(CampoCompra.Documento, "Indique si el proveedor entregó factura o boleta."));
        if (!PatronSerie().IsMatch((compra.Serie ?? string.Empty).Trim().ToUpperInvariant()))
            errores.Add(new(CampoCompra.Documento, "La serie del comprobante del proveedor tiene 4 letras o números (F001, E001...)."));
        if (compra.Numero is < 1 or > 99999999)
            errores.Add(new(CampoCompra.Documento, "El número del comprobante del proveedor va de 1 a 99999999."));

        if (compra.FechaEmision > hoy)
            errores.Add(new(CampoCompra.Fecha, "La fecha del comprobante no puede ser posterior a hoy."));
        else if (compra.FechaEmision < hoy.AddDays(-DiasAntiguedadMaxima))
            errores.Add(new(CampoCompra.Fecha, "La fecha del comprobante tiene más de un año: revísela."));

        if (compra.Detalle.Count == 0)
            errores.Add(new(CampoCompra.Detalle, "Agregue al menos un producto a la compra."));
        else if (compra.Detalle.Any(l => l.Cantidad <= 0 || l.CostoUnitario <= 0))
            errores.Add(new(CampoCompra.Detalle, "Cada producto debe tener cantidad y costo mayores que cero."));

        return errores;
    }

    /// <summary>
    /// Deja la compra lista para guardar: serie en mayúsculas, líneas
    /// numeradas e importes y totales recalculados. Lanza
    /// CompraInvalidaException si no cumple las reglas.
    /// </summary>
    public static Compra Preparar(Compra compra, DateOnly hoy)
    {
        compra.Serie = (compra.Serie ?? string.Empty).Trim().ToUpperInvariant();
        compra.Observacion = string.IsNullOrWhiteSpace(compra.Observacion) ? null : compra.Observacion.Trim();

        var errores = Errores(compra, hoy);
        if (errores.Count > 0)
            throw new CompraInvalidaException(errores);

        var item = 0;
        foreach (var linea in compra.Detalle)
        {
            linea.Item = ++item;
            linea.Importe = CalculadoraVenta.Importe(linea.Cantidad, linea.CostoUnitario);
        }

        var totales = CalculadoraVenta.Totales(compra.Detalle.Select(l => l.Importe));
        compra.OpGravada = totales.OpGravada;
        compra.Igv = totales.Igv;
        compra.Total = totales.Total;
        return compra;
    }

    [GeneratedRegex("^[A-Z0-9]{4}$")]
    private static partial Regex PatronSerie();
}

/// <summary>La compra no cumple las reglas para registrarse.</summary>
public sealed class CompraInvalidaException : Exception
{
    public CompraInvalidaException(IReadOnlyList<ErrorCompra> errores)
        : base(string.Join(" ", errores.Select(e => e.Mensaje))) => Errores = errores;

    public IReadOnlyList<ErrorCompra> Errores { get; }
}

/// <summary>
/// El comprobante del proveedor ya se registró (HU-11, criterio 2): quizá lo
/// registró otra PC en el mismo momento.
/// </summary>
public sealed class CompraDuplicadaException : Exception
{
    public CompraDuplicadaException(string documento, string? proveedor)
        : base($"La {documento} de {proveedor ?? "este proveedor"} ya está registrada (quizá desde otra PC). Búsquela en Compras (F4).")
    {
    }
}
