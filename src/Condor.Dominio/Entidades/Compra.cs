namespace Condor.Dominio.Entidades;

/// <summary>
/// Comprobante que entrega el proveedor (catálogo 01 de la SUNAT): la
/// ferretería registra sus compras con la factura o la boleta que recibe.
/// </summary>
public static class TipoDocumentoCompra
{
    public const string Factura = "01";
    public const string Boleta = "03";

    public static string Nombre(string tipo) => tipo switch
    {
        Factura => "Factura",
        Boleta => "Boleta",
        _ => "Comprobante",
    };
}

/// <summary>
/// Compra a un proveedor (HU-11, T-80): el comprobante que entregó, su fecha
/// y el detalle de productos con lo que costaron. Sirve para controlar los
/// egresos del negocio; no mueve stock (el sistema no lleva inventario).
/// </summary>
public sealed class Compra
{
    public long Id { get; set; }

    public long ProveedorId { get; set; }

    /// <summary>Datos del proveedor para la lista; null si solo se conoce su Id.</summary>
    public Proveedor? Proveedor { get; set; }

    /// <summary>01 factura o 03 boleta del proveedor (<see cref="TipoDocumentoCompra"/>).</summary>
    public string TipoDocumento { get; set; } = TipoDocumentoCompra.Factura;

    /// <summary>Serie impresa en el comprobante del proveedor: F001, E001, FA01...</summary>
    public string Serie { get; set; } = string.Empty;

    public long Numero { get; set; }

    /// <summary>Fecha que figura en el comprobante del proveedor.</summary>
    public DateOnly FechaEmision { get; set; }

    /// <summary>Cuándo se registró en el sistema.</summary>
    public DateTimeOffset RegistradaEn { get; set; }

    public int TerminalId { get; set; }

    /// <summary>PC donde se registró, para la lista.</summary>
    public string? TerminalNombre { get; set; }

    /// <summary>Nota libre: "pedido de la obra Pedregal", "pagado con cheque"...</summary>
    public string? Observacion { get; set; }

    public decimal OpGravada { get; set; }

    public decimal Igv { get; set; }

    public decimal Total { get; set; }

    public List<DetalleCompra> Detalle { get; } = new();

    /// <summary>Número del comprobante del proveedor: F001-00012345.</summary>
    public string Documento => Entidades.Serie.Formatear(Serie, Numero);

    /// <summary>"Factura F001-00012345", como se muestra en la lista.</summary>
    public string DocumentoCompleto => $"{TipoDocumentoCompra.Nombre(TipoDocumento)} {Documento}";
}

/// <summary>Línea de la compra. El costo incluye IGV, como en el comprobante del proveedor.</summary>
public sealed class DetalleCompra
{
    public int Item { get; set; }

    public long ProductoId { get; set; }

    public string Descripcion { get; set; } = string.Empty;

    public string Unidad { get; set; } = "UND";

    public decimal Cantidad { get; set; }

    /// <summary>Costo por unidad con IGV.</summary>
    public decimal CostoUnitario { get; set; }

    public decimal Importe { get; set; }
}
