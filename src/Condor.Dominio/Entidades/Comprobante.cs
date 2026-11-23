namespace Condor.Dominio.Entidades;

/// <summary>
/// Comprobante emitido (boleta, factura, nota de venta o proforma).
/// Las boletas se emiten desde el Sprint 2 (HU-05); las notas de venta y las
/// proformas, desde el Sprint 5 (HU-08, HU-09).
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

    /// <summary>PENDIENTE, ACEPTADO, OBSERVADO o RECHAZADO (HU-07; ver Sunat.EstadoSunat).</summary>
    public string EstadoSunat { get; set; } = "PENDIENTE";

    /// <summary>Descripción del CDR o motivo del último intento fallido; null si nunca se envió.</summary>
    public string? MensajeSunat { get; set; }

    /// <summary>Veces que se imprimió en la ticketera; 0 si la impresión falló (HU-12).</summary>
    public int Impresiones { get; set; }

    /// <summary>Último día en que valen los precios de la proforma (HU-09); null en los demás tipos.</summary>
    public DateOnly? ValidaHasta { get; set; }

    /// <summary>Proforma de la que salió esta venta (HU-09); null si se armó en la pantalla.</summary>
    public long? ProformaId { get; set; }

    /// <summary>Número del comprobante en que se convirtió la proforma (B001-00000031); null si aún no.</summary>
    public string? ConvertidaEn { get; set; }

    /// <summary>La proforma ya se convirtió en venta: no se puede convertir otra vez.</summary>
    public bool Convertida => ConvertidaEn is not null;

    /// <summary>True si es una proforma cuya vigencia ya pasó (<paramref name="hoy"/> en hora de Perú).</summary>
    public bool Vencida(DateOnly hoy) => TipoComprobante == Entidades.TipoComprobante.Proforma && ValidaHasta < hoy;

    public List<DetalleComprobante> Detalle { get; } = new();

    public string NumeroCompleto => Serie.Formatear(SerieCodigo, Numero);

    /// <summary>
    /// Boleta o factura: comprobante electrónico que se envía a la SUNAT. La
    /// nota de venta y la proforma son documentos internos (HU-08, HU-09).
    /// </summary>
    public bool EsElectronico => Entidades.TipoComprobante.EsElectronico(TipoComprobante);

    /// <summary>Nombre corto para las listas: Boleta, Factura, Nota de venta o Proforma.</summary>
    public string TipoCorto => TipoComprobante switch
    {
        Entidades.TipoComprobante.Boleta => "Boleta",
        Entidades.TipoComprobante.Factura => "Factura",
        Entidades.TipoComprobante.NotaVenta => "Nota de venta",
        Entidades.TipoComprobante.Proforma => "Proforma",
        _ => "Comprobante",
    };

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
