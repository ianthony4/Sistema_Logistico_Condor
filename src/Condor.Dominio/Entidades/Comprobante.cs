namespace Condor.Dominio.Entidades;

/// <summary>
/// Comprobante emitido (boleta, factura, nota de venta o proforma).
/// Las boletas se emiten desde el Sprint 2 (HU-05).
/// </summary>
public sealed class Comprobante
{
    public long Id { get; set; }

    /// <summary>Código del catálogo 01 de la SUNAT (03 boleta, 01 factura) o NV / PR.</summary>
    public string TipoComprobante { get; set; } = Entidades.TipoComprobante.Boleta;

    public int SerieId { get; set; }

    public string SerieCodigo { get; set; } = string.Empty;

    public long Numero { get; set; }

    public DateTimeOffset Fecha { get; set; }

    public long? ClienteId { get; set; }

    /// <summary>Datos del cliente para el ticket; null en la boleta sin DNI.</summary>
    public Cliente? Cliente { get; set; }

    public int TerminalId { get; set; }

    /// <summary>Nombre de la PC que lo emitió, para el ticket.</summary>
    public string? TerminalNombre { get; set; }

    /// <summary>Perfil que emitió el documento; siempre MAESTRO (HU-14).</summary>
    public string Usuario { get; set; } = "MAESTRO";

    public decimal OpGravada { get; set; }

    public decimal Igv { get; set; }

    public decimal Total { get; set; }

    public string EstadoSunat { get; set; } = "PENDIENTE";

    /// <summary>Veces que se imprimió en la ticketera; 0 si la impresión falló (HU-12).</summary>
    public int Impresiones { get; set; }

    public List<DetalleComprobante> Detalle { get; } = new();

    public string NumeroCompleto => Serie.Formatear(SerieCodigo, Numero);

    /// <summary>Nombre del documento tal como se imprime.</summary>
    public string NombreTipo => TipoComprobante switch
    {
        Entidades.TipoComprobante.Boleta => "Boleta de venta electrónica",
        Entidades.TipoComprobante.Factura => "Factura electrónica",
        Entidades.TipoComprobante.NotaVenta => "Nota de venta",
        Entidades.TipoComprobante.Proforma => "Proforma",
        _ => "Comprobante",
    };
}

/// <summary>
/// Línea del comprobante. Descripción y precio se copian del producto al vender.
/// </summary>
public sealed class DetalleComprobante
{
    public int Item { get; set; }

    public long ProductoId { get; set; }

    public string Descripcion { get; set; } = string.Empty;

    /// <summary>Unidad de medida para el ticket; al reimprimir se lee del producto.</summary>
    public string Unidad { get; set; } = "UND";

    public decimal Cantidad { get; set; }

    public decimal PrecioUnitario { get; set; }

    public decimal Importe { get; set; }
}
