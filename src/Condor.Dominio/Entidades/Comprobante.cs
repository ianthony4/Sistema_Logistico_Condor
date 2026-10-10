namespace Condor.Dominio.Entidades;

/// <summary>
/// Comprobante emitido (boleta, factura, nota de venta o proforma).
/// La emisión se implementa a partir del Sprint 2 (HU-05).
/// </summary>
public sealed class Comprobante
{
    public long Id { get; set; }

    public int SerieId { get; set; }

    public string SerieCodigo { get; set; } = string.Empty;

    public long Numero { get; set; }

    public DateTimeOffset Fecha { get; set; }

    public long? ClienteId { get; set; }

    public int TerminalId { get; set; }

    /// <summary>Perfil que emitió el documento; siempre MAESTRO (HU-14).</summary>
    public string Usuario { get; set; } = "MAESTRO";

    public decimal OpGravada { get; set; }

    public decimal Igv { get; set; }

    public decimal Total { get; set; }

    public string EstadoSunat { get; set; } = "PENDIENTE";

    public List<DetalleComprobante> Detalle { get; } = new();

    public string NumeroCompleto => Serie.Formatear(SerieCodigo, Numero);
}

/// <summary>
/// Línea del comprobante. Descripción y precio se copian del producto al vender.
/// </summary>
public sealed class DetalleComprobante
{
    public int Item { get; set; }

    public long ProductoId { get; set; }

    public string Descripcion { get; set; } = string.Empty;

    public decimal Cantidad { get; set; }

    public decimal PrecioUnitario { get; set; }

    public decimal Importe { get; set; }
}
