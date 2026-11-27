using System.Globalization;
using System.Xml.Linq;
using Condor.Dominio.Entidades;

namespace Condor.Sunat;

/// <summary>
/// Arma el XML UBL 2.1 (DespatchAdvice) de la guía de remisión remitente
/// según la guía de la SUNAT para la GRE de 2022 (HU-10, T-92). Sigue la
/// plantilla despatch2022 de Greenter; como en la factura, el orden de los
/// elementos importa.
/// </summary>
public static class GeneradorXmlGuia
{
    public static readonly XNamespace Despatch = "urn:oasis:names:specification:ubl:schema:xsd:DespatchAdvice-2";

    private static readonly XNamespace Cac = GeneradorXmlUbl.Cac;
    private static readonly XNamespace Cbc = GeneradorXmlUbl.Cbc;
    private static readonly CultureInfo Numeros = CultureInfo.InvariantCulture;
    private static readonly TimeSpan HoraPeru = TimeSpan.FromHours(-5);

    /// <summary>Nombre del archivo, sin extensión: 20600000005-09-T001-5.</summary>
    public static string NombreArchivo(Empresa empresa, GuiaRemision guia) =>
        $"{empresa.Ruc}-{GuiaRemision.TipoSunat}-{guia.SerieCodigo}-{guia.Numero}";

    /// <summary>Genera el XML sin firmar (con el ext:ExtensionContent vacío donde irá la firma).</summary>
    public static XDocument Generar(Empresa empresa, GuiaRemision guia)
    {
        if (guia.Numero <= 0)
            throw new ArgumentException("La guía aún no tiene número: emítala antes de enviarla.", nameof(guia));
        if (guia.Detalle.Count == 0)
            throw new ArgumentException("La guía no tiene detalle.", nameof(guia));

        var fecha = guia.Fecha.ToOffset(HoraPeru);
        var raiz = new XElement(Despatch + "DespatchAdvice",
            new XAttribute(XNamespace.Xmlns + "cac", Cac),
            new XAttribute(XNamespace.Xmlns + "cbc", Cbc),
            new XAttribute(XNamespace.Xmlns + "ds", GeneradorXmlUbl.Ds),
            new XAttribute(XNamespace.Xmlns + "ext", GeneradorXmlUbl.Ext),
            new XElement(GeneradorXmlUbl.Ext + "UBLExtensions",
                new XElement(GeneradorXmlUbl.Ext + "UBLExtension",
                    new XElement(GeneradorXmlUbl.Ext + "ExtensionContent"))),
            new XElement(Cbc + "UBLVersionID", "2.1"),
            new XElement(Cbc + "CustomizationID", "2.0"),
            new XElement(Cbc + "ID", $"{guia.SerieCodigo}-{guia.Numero}"),
            new XElement(Cbc + "IssueDate", fecha.ToString("yyyy-MM-dd", Numeros)),
            new XElement(Cbc + "IssueTime", fecha.ToString("HH:mm:ss", Numeros)),
            new XElement(Cbc + "DespatchAdviceTypeCode",
                Catalogo("Tipo de Documento", "01"), GuiaRemision.TipoSunat),
            string.IsNullOrWhiteSpace(guia.Observacion) ? null : new XElement(Cbc + "Note", new XCData(guia.Observacion)),
            DocumentoRelacionado(empresa, guia),
            Firma(empresa),
            new XElement(Cac + "DespatchSupplierParty",
                Parte("6", empresa.Ruc, empresa.RazonSocial)),
            new XElement(Cac + "DeliveryCustomerParty",
                Parte(((int)guia.DestinatarioTipo).ToString(Numeros), guia.DestinatarioDocumento, guia.DestinatarioNombre)),
            Envio(guia),
            guia.Detalle.Select(Linea));

        return new XDocument(new XDeclaration("1.0", "utf-8", null), raiz);
    }

    private static IEnumerable<XAttribute> Catalogo(string nombre, string numero) => new[]
    {
        new XAttribute("listAgencyName", "PE:SUNAT"),
        new XAttribute("listName", nombre),
        new XAttribute("listURI", $"urn:pe:gob:sunat:cpe:see:gem:catalogos:catalogo{numero}"),
    };

    /// <summary>La factura o boleta que sustenta el traslado (catálogo 61).</summary>
    private static XElement? DocumentoRelacionado(Empresa empresa, GuiaRemision guia)
    {
        if (guia.ComprobanteNumero is not { } numero || guia.ComprobanteTipo is not { } tipo)
            return null;
        return new XElement(Cac + "AdditionalDocumentReference",
            new XElement(Cbc + "ID", numero),
            new XElement(Cbc + "DocumentTypeCode", Catalogo("Documento relacionado al transporte", "61"), tipo),
            new XElement(Cbc + "DocumentType", tipo == TipoComprobante.Factura ? "Factura" : "Boleta de venta"),
            new XElement(Cac + "IssuerParty",
                new XElement(Cac + "PartyIdentification",
                    new XElement(Cbc + "ID", Esquema("6"), empresa.Ruc))));
    }

    private static XElement Firma(Empresa empresa) =>
        new(Cac + "Signature",
            new XElement(Cbc + "ID", $"SIGN{empresa.Ruc}"),
            new XElement(Cac + "SignatoryParty",
                new XElement(Cac + "PartyIdentification", new XElement(Cbc + "ID", empresa.Ruc)),
                new XElement(Cac + "PartyName", new XElement(Cbc + "Name", new XCData(empresa.RazonSocial)))),
            new XElement(Cac + "DigitalSignatureAttachment",
                new XElement(Cac + "ExternalReference", new XElement(Cbc + "URI", $"#{GeneradorXmlUbl.IdFirma}"))));

    private static XElement Parte(string tipoDocumento, string numero, string nombre) =>
        new(Cac + "Party",
            new XElement(Cac + "PartyIdentification",
                new XElement(Cbc + "ID", Esquema(tipoDocumento), numero)),
            new XElement(Cac + "PartyLegalEntity",
                new XElement(Cbc + "RegistrationName", new XCData(nombre))));

    private static IEnumerable<XAttribute> Esquema(string tipo) => new[]
    {
        new XAttribute("schemeID", tipo),
        new XAttribute("schemeName", "Documento de Identidad"),
        new XAttribute("schemeAgencyName", "PE:SUNAT"),
        new XAttribute("schemeURI", "urn:pe:gob:sunat:cpe:see:gem:catalogos:catalogo06"),
    };

    private static XElement Envio(GuiaRemision guia) =>
        new(Cac + "Shipment",
            new XElement(Cbc + "ID", "SUNAT_Envio"),
            new XElement(Cbc + "HandlingCode", Catalogo("Motivo de traslado", "20"), guia.Motivo),
            new XElement(Cbc + "HandlingInstructions", guia.DescripcionMotivo ?? MotivoTraslado.Nombre(guia.Motivo)),
            new XElement(Cbc + "GrossWeightMeasure", new XAttribute("unitCode", "KGM"),
                guia.PesoBrutoKg.ToString("0.000", Numeros)),
            new XElement(Cac + "ShipmentStage",
                new XElement(Cbc + "TransportModeCode", Catalogo("Modalidad de traslado", "18"), guia.Modalidad),
                new XElement(Cac + "TransitPeriod",
                    new XElement(Cbc + "StartDate", guia.FechaTraslado.ToString("yyyy-MM-dd", Numeros))),
                guia.EsPublico ? Transportista(guia) : Conductor(guia)),
            new XElement(Cac + "Delivery",
                Direccion("DeliveryAddress", guia.LlegadaUbigeo, guia.LlegadaDireccion),
                new XElement(Cac + "Despatch",
                    Direccion("DespatchAddress", guia.PartidaUbigeo, guia.PartidaDireccion))),
            guia.EsPublico
                ? null
                : new XElement(Cac + "TransportHandlingUnit",
                    new XElement(Cac + "TransportEquipment", new XElement(Cbc + "ID", guia.VehiculoPlaca))));

    private static XElement Transportista(GuiaRemision guia) =>
        new(Cac + "CarrierParty",
            new XElement(Cac + "PartyIdentification", new XElement(Cbc + "ID", Esquema("6"), guia.TransportistaRuc)),
            new XElement(Cac + "PartyLegalEntity",
                new XElement(Cbc + "RegistrationName", new XCData(guia.TransportistaRazonSocial ?? string.Empty))));

    private static XElement Conductor(GuiaRemision guia) =>
        new(Cac + "DriverPerson",
            new XElement(Cbc + "ID", Esquema("1"), guia.ConductorDni),
            new XElement(Cbc + "FirstName", guia.ConductorNombres),
            new XElement(Cbc + "FamilyName", guia.ConductorApellidos),
            new XElement(Cbc + "JobTitle", "Principal"),
            new XElement(Cac + "IdentityDocumentReference", new XElement(Cbc + "ID", guia.ConductorLicencia)));

    private static XElement Direccion(string elemento, string ubigeo, string linea) =>
        new(Cac + elemento,
            new XElement(Cbc + "ID",
                new XAttribute("schemeAgencyName", "PE:INEI"), new XAttribute("schemeName", "Ubigeos"), ubigeo),
            new XElement(Cac + "AddressLine", new XElement(Cbc + "Line", new XCData(linea))));

    private static XElement Linea(DetalleGuia linea) =>
        new(Cac + "DespatchLine",
            new XElement(Cbc + "ID", linea.Item),
            new XElement(Cbc + "DeliveredQuantity", new XAttribute("unitCode", GeneradorXmlUbl.UnidadSunat(linea.Unidad)),
                linea.Cantidad.ToString("0.###", Numeros)),
            new XElement(Cac + "OrderLineReference", new XElement(Cbc + "LineID", linea.Item)),
            new XElement(Cac + "Item",
                new XElement(Cbc + "Description", new XCData(linea.Descripcion)),
                new XElement(Cac + "SellersItemIdentification", new XElement(Cbc + "ID", linea.ProductoId))));
}
