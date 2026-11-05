using System.Globalization;
using System.Xml.Linq;
using Condor.Dominio.Entidades;
using Condor.Dominio.Impresion;
using Condor.Dominio.Ventas;

namespace Condor.Sunat;

/// <summary>
/// Arma el XML UBL 2.1 de una boleta o factura según la guía de la SUNAT
/// (HU-07, T-41). Sigue la plantilla invoice2.1 de Greenter: el orden de los
/// elementos importa, porque la SUNAT valida el XML contra el esquema UBL.
/// </summary>
/// <remarks>
/// Los precios del catálogo incluyen IGV (venta interna gravada, código 10
/// del catálogo 07). Cada línea se separa en valor de venta e IGV; los
/// totales de la cabecera son los del comprobante guardado, que siempre
/// cumplen gravada + IGV = total.
/// </remarks>
public static class GeneradorXmlUbl
{
    public static readonly XNamespace Inv = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    public static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    public static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    public static readonly XNamespace Ds = "http://www.w3.org/2000/09/xmldsig#";
    public static readonly XNamespace Ext = "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";

    /// <summary>Id de la firma: lo usan cac:Signature y ds:Signature.</summary>
    public const string IdFirma = "SignCondor";

    /// <summary>Catálogo 51: venta interna.</summary>
    public const string TipoOperacionVentaInterna = "0101";

    /// <summary>Catálogo 07: gravado - operación onerosa.</summary>
    public const string AfectacionGravada = "10";

    public const string Moneda = "PEN";

    private static readonly CultureInfo Numeros = CultureInfo.InvariantCulture;

    /// <summary>Perú no cambia de hora: la fecha de emisión es la de UTC-5.</summary>
    private static readonly TimeSpan HoraPeru = TimeSpan.FromHours(-5);

    /// <summary>
    /// Nombre del archivo que pide la SUNAT, sin extensión: RUC-TIPO-SERIE-NÚMERO
    /// (20600000005-03-B001-12).
    /// </summary>
    public static string NombreArchivo(Empresa empresa, Comprobante comprobante) =>
        $"{empresa.Ruc}-{comprobante.TipoComprobante}-{comprobante.SerieCodigo}-{comprobante.Numero}";

    /// <summary>Genera el XML sin firmar (con el ext:ExtensionContent vacío donde irá la firma).</summary>
    public static XDocument Generar(Empresa empresa, Comprobante comprobante)
    {
        if (comprobante.TipoComprobante is not (TipoComprobante.Boleta or TipoComprobante.Factura))
            throw new ArgumentException($"{comprobante.NombreTipo} no se envía a la SUNAT.", nameof(comprobante));
        if (comprobante.Numero <= 0)
            throw new ArgumentException("El comprobante aún no tiene número: emítalo antes de enviarlo.", nameof(comprobante));
        if (comprobante.Detalle.Count == 0)
            throw new ArgumentException("El comprobante no tiene detalle.", nameof(comprobante));

        var fecha = comprobante.Fecha.ToOffset(HoraPeru);
        var raiz = new XElement(Inv + "Invoice",
            new XAttribute(XNamespace.Xmlns + "cac", Cac),
            new XAttribute(XNamespace.Xmlns + "cbc", Cbc),
            new XAttribute(XNamespace.Xmlns + "ds", Ds),
            new XAttribute(XNamespace.Xmlns + "ext", Ext),
            new XElement(Ext + "UBLExtensions",
                new XElement(Ext + "UBLExtension",
                    new XElement(Ext + "ExtensionContent"))),
            new XElement(Cbc + "UBLVersionID", "2.1"),
            new XElement(Cbc + "CustomizationID", "2.0"),
            new XElement(Cbc + "ID", $"{comprobante.SerieCodigo}-{comprobante.Numero}"),
            new XElement(Cbc + "IssueDate", fecha.ToString("yyyy-MM-dd", Numeros)),
            new XElement(Cbc + "IssueTime", fecha.ToString("HH:mm:ss", Numeros)),
            new XElement(Cbc + "InvoiceTypeCode", new XAttribute("listID", TipoOperacionVentaInterna), comprobante.TipoComprobante),
            // Leyenda 1000: importe en letras.
            new XElement(Cbc + "Note", new XAttribute("languageLocaleID", "1000"),
                new XCData(MontoEnLetras.Soles(comprobante.Total))),
            new XElement(Cbc + "DocumentCurrencyCode", Moneda),
            Firma(empresa),
            Emisor(empresa),
            Receptor(comprobante),
            comprobante.TipoComprobante == TipoComprobante.Factura ? FormaPagoContado() : null,
            Impuestos(comprobante.OpGravada, comprobante.Igv),
            new XElement(Cac + "LegalMonetaryTotal",
                Monto("LineExtensionAmount", comprobante.OpGravada),
                Monto("TaxInclusiveAmount", comprobante.Total),
                Monto("PayableAmount", comprobante.Total)),
            comprobante.Detalle.Select(Linea));

        return new XDocument(new XDeclaration("1.0", "utf-8", null), raiz);
    }

    /// <summary>Importes de una línea con precio que incluye IGV.</summary>
    public readonly record struct ImportesLinea(decimal ValorVenta, decimal Igv, decimal ValorUnitario);

    /// <summary>
    /// Separa el importe (con IGV) en valor de venta e IGV: el valor se
    /// redondea a céntimos y el IGV es la diferencia, como en el comprobante.
    /// </summary>
    public static ImportesLinea Importes(DetalleComprobante linea)
    {
        var valor = CalculadoraVenta.Redondear(linea.Importe / (1m + CalculadoraVenta.TasaIgv));
        var valorUnitario = Math.Round(linea.PrecioUnitario / (1m + CalculadoraVenta.TasaIgv), 6, MidpointRounding.AwayFromZero);
        return new ImportesLinea(valor, linea.Importe - valor, valorUnitario);
    }

    /// <summary>Unidad de medida del catálogo 03 de la SUNAT (UN/ECE rec. 20).</summary>
    public static string UnidadSunat(string? unidad) => (unidad ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "KG" or "KGM" => "KGM",
        "G" or "GR" or "GRM" => "GRM",
        "M" or "MT" or "MTR" => "MTR",
        "M2" or "MTK" => "MTK",
        "GAL" or "GLL" => "GLL",
        "L" or "LT" or "LTR" => "LTR",
        "BLS" or "BOL" or "BG" => "BG",
        "CJA" or "CAJA" or "BX" => "BX",
        "PAR" or "PR" => "PR",
        "ROL" or "ROLLO" => "RO",
        "PQT" or "PK" => "PK",
        "SRV" or "ZZ" => "ZZ",
        _ => "NIU", // UND, VAR (varilla), PZA...: unidad (bienes)
    };

    private static XElement Firma(Empresa empresa) =>
        new(Cac + "Signature",
            new XElement(Cbc + "ID", $"SIGN{empresa.Ruc}"),
            new XElement(Cac + "SignatoryParty",
                new XElement(Cac + "PartyIdentification", new XElement(Cbc + "ID", empresa.Ruc)),
                new XElement(Cac + "PartyName", new XElement(Cbc + "Name", new XCData(empresa.RazonSocial)))),
            new XElement(Cac + "DigitalSignatureAttachment",
                new XElement(Cac + "ExternalReference", new XElement(Cbc + "URI", $"#{IdFirma}"))));

    private static XElement Emisor(Empresa empresa) =>
        new(Cac + "AccountingSupplierParty",
            new XElement(Cac + "Party",
                new XElement(Cac + "PartyIdentification",
                    new XElement(Cbc + "ID", new XAttribute("schemeID", "6"), empresa.Ruc)),
                string.IsNullOrWhiteSpace(empresa.NombreComercial)
                    ? null
                    : new XElement(Cac + "PartyName", new XElement(Cbc + "Name", new XCData(empresa.NombreComercial))),
                new XElement(Cac + "PartyLegalEntity",
                    new XElement(Cbc + "RegistrationName", new XCData(empresa.RazonSocial)),
                    new XElement(Cac + "RegistrationAddress",
                        new XElement(Cbc + "ID", empresa.Ubigeo),
                        new XElement(Cbc + "AddressTypeCode", empresa.CodigoEstablecimiento),
                        new XElement(Cbc + "CityName", empresa.Provincia),
                        new XElement(Cbc + "CountrySubentity", empresa.Departamento),
                        new XElement(Cbc + "District", empresa.Distrito),
                        new XElement(Cac + "AddressLine", new XElement(Cbc + "Line", new XCData(empresa.Direccion))),
                        new XElement(Cac + "Country", new XElement(Cbc + "IdentificationCode", "PE"))))));

    /// <summary>
    /// Cliente del comprobante. La boleta sin DNI va a "CLIENTES VARIOS" con el
    /// tipo de documento 0 y el número "-" (ventas menores a S/ 700).
    /// </summary>
    private static XElement Receptor(Comprobante comprobante)
    {
        var cliente = comprobante.Cliente;
        var tipo = cliente is null ? "0" : ((int)cliente.TipoDocumento).ToString(Numeros);
        var numero = cliente?.NumeroDocumento is { Length: > 0 } n ? n : "-";
        var nombre = cliente?.Nombre is { Length: > 0 } nom ? nom : "CLIENTES VARIOS";

        return new XElement(Cac + "AccountingCustomerParty",
            new XElement(Cac + "Party",
                new XElement(Cac + "PartyIdentification",
                    new XElement(Cbc + "ID", new XAttribute("schemeID", tipo), numero)),
                new XElement(Cac + "PartyLegalEntity",
                    new XElement(Cbc + "RegistrationName", new XCData(nombre)),
                    string.IsNullOrWhiteSpace(cliente?.Direccion)
                        ? null
                        : new XElement(Cac + "RegistrationAddress",
                            new XElement(Cac + "AddressLine", new XElement(Cbc + "Line", new XCData(cliente.Direccion))),
                            new XElement(Cac + "Country", new XElement(Cbc + "IdentificationCode", "PE"))))));
    }

    /// <summary>La factura debe declarar la forma de pago; el mostrador vende al contado.</summary>
    private static XElement FormaPagoContado() =>
        new(Cac + "PaymentTerms",
            new XElement(Cbc + "ID", "FormaPago"),
            new XElement(Cbc + "PaymentMeansID", "Contado"));

    private static XElement Impuestos(decimal baseImponible, decimal igv) =>
        new(Cac + "TaxTotal",
            Monto("TaxAmount", igv),
            new XElement(Cac + "TaxSubtotal",
                Monto("TaxableAmount", baseImponible),
                Monto("TaxAmount", igv),
                new XElement(Cac + "TaxCategory", EsquemaIgv())));

    private static XElement Linea(DetalleComprobante linea)
    {
        var importes = Importes(linea);
        return new XElement(Cac + "InvoiceLine",
            new XElement(Cbc + "ID", linea.Item),
            new XElement(Cbc + "InvoicedQuantity", new XAttribute("unitCode", UnidadSunat(linea.Unidad)),
                linea.Cantidad.ToString("0.###", Numeros)),
            Monto("LineExtensionAmount", importes.ValorVenta),
            // Precio unitario con IGV (tipo 01 del catálogo 16).
            new XElement(Cac + "PricingReference",
                new XElement(Cac + "AlternativeConditionPrice",
                    Monto("PriceAmount", linea.PrecioUnitario),
                    new XElement(Cbc + "PriceTypeCode", "01"))),
            new XElement(Cac + "TaxTotal",
                Monto("TaxAmount", importes.Igv),
                new XElement(Cac + "TaxSubtotal",
                    Monto("TaxableAmount", importes.ValorVenta),
                    Monto("TaxAmount", importes.Igv),
                    new XElement(Cac + "TaxCategory",
                        new XElement(Cbc + "Percent", (CalculadoraVenta.TasaIgv * 100).ToString("0", Numeros)),
                        new XElement(Cbc + "TaxExemptionReasonCode", AfectacionGravada),
                        EsquemaIgv()))),
            new XElement(Cac + "Item",
                new XElement(Cbc + "Description", new XCData(linea.Descripcion)),
                new XElement(Cac + "SellersItemIdentification", new XElement(Cbc + "ID", linea.ProductoId))),
            // Valor unitario sin IGV.
            new XElement(Cac + "Price",
                new XElement(Cbc + "PriceAmount", new XAttribute("currencyID", Moneda),
                    importes.ValorUnitario.ToString("0.######", Numeros))));
    }

    private static XElement EsquemaIgv() =>
        new(Cac + "TaxScheme",
            new XElement(Cbc + "ID", "1000"),
            new XElement(Cbc + "Name", "IGV"),
            new XElement(Cbc + "TaxTypeCode", "VAT"));

    private static XElement Monto(string nombre, decimal monto) =>
        new(Cbc + nombre, new XAttribute("currencyID", Moneda), monto.ToString("0.00", Numeros));
}
