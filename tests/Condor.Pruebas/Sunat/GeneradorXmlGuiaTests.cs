using System.Xml.Linq;
using Condor.Dominio.Entidades;
using Condor.Sunat;

namespace Condor.Pruebas.Sunat;

/// <summary>XML UBL 2.1 de la guía de remisión remitente (HU-10, T-92).</summary>
public class GeneradorXmlGuiaTests
{
    private static readonly XNamespace Cac = GeneradorXmlUbl.Cac;
    private static readonly XNamespace Cbc = GeneradorXmlUbl.Cbc;

    private static Empresa Empresa() => new()
    {
        Ruc = "20600000005",
        RazonSocial = "FERRETERÍA CÓNDOR MAJES S.A.C.",
        Direccion = "Majes, Caylloma - Arequipa",
    };

    private static GuiaRemision Guia(bool publico = false)
    {
        var guia = new GuiaRemision
        {
            Id = 5, SerieCodigo = "T001", Numero = 5,
            Fecha = new DateTimeOffset(2026, 11, 26, 15, 30, 0, TimeSpan.Zero), // 10:30 en Majes
            FechaTraslado = new DateOnly(2026, 11, 27),
            Motivo = MotivoTraslado.Venta,
            Modalidad = publico ? ModalidadTraslado.Publico : ModalidadTraslado.Privado,
            DestinatarioTipo = TipoDocumento.Ruc, DestinatarioDocumento = "20131312955",
            DestinatarioNombre = "Constructora Valle de Majes S.A.C.",
            PartidaUbigeo = "040520", PartidaDireccion = "Majes, Caylloma - Arequipa",
            LlegadaUbigeo = "040129", LlegadaDireccion = "Obra Av. Ejército 101, Yanahuara",
            PesoBrutoKg = 1235.5m,
            ComprobanteId = 31, ComprobanteTipo = TipoComprobante.Factura, ComprobanteNumero = "F001-00000031",
            Detalle =
            {
                new() { Item = 1, ProductoId = 1, Descripcion = "Cemento Portland tipo I x 42.5 kg", Unidad = "BLS", Cantidad = 20 },
                new() { Item = 2, ProductoId = 2, Descripcion = "Fierro corrugado 1/2 in x 9 m", Unidad = "VAR", Cantidad = 10 },
            },
        };
        if (publico)
        {
            guia.TransportistaRuc = "20100137390";
            guia.TransportistaRazonSocial = "Transportes Arequipa S.A.";
        }
        else
        {
            guia.VehiculoPlaca = "V1A952";
            guia.ConductorDni = "45879632";
            guia.ConductorNombres = "Juan";
            guia.ConductorApellidos = "Pérez Quispe";
            guia.ConductorLicencia = "Q45879632";
        }
        return guia;
    }

    private static string Valor(XDocument xml, params string[] ruta)
    {
        IEnumerable<XElement> actual = new[] { xml.Root! };
        foreach (var nombre in ruta)
            actual = actual.Elements().Where(e => e.Name.LocalName == nombre);
        return actual.Single().Value;
    }

    [Fact]
    public void La_cabecera_lleva_tipo_09_serie_numero_y_fecha_de_Peru()
    {
        var xml = GeneradorXmlGuia.Generar(Empresa(), Guia());

        Assert.Equal("DespatchAdvice", xml.Root!.Name.LocalName);
        Assert.Equal("T001-5", Valor(xml, "ID"));
        Assert.Equal("2026-11-26", Valor(xml, "IssueDate"));
        Assert.Equal("10:30:00", Valor(xml, "IssueTime"));
        Assert.Equal("09", Valor(xml, "DespatchAdviceTypeCode"));
        Assert.Equal("20600000005-09-T001-5", GeneradorXmlGuia.NombreArchivo(Empresa(), Guia()));
    }

    [Fact]
    public void Remitente_destinatario_y_factura_relacionada()
    {
        var xml = GeneradorXmlGuia.Generar(Empresa(), Guia());

        Assert.Equal("20600000005", Valor(xml, "DespatchSupplierParty", "Party", "PartyIdentification", "ID"));
        Assert.Equal("20131312955", Valor(xml, "DeliveryCustomerParty", "Party", "PartyIdentification", "ID"));
        Assert.Equal("F001-00000031", Valor(xml, "AdditionalDocumentReference", "ID"));
        Assert.Equal("01", Valor(xml, "AdditionalDocumentReference", "DocumentTypeCode"));
    }

    [Fact]
    public void El_envio_lleva_motivo_peso_fecha_partida_y_llegada()
    {
        var xml = GeneradorXmlGuia.Generar(Empresa(), Guia());

        Assert.Equal("01", Valor(xml, "Shipment", "HandlingCode"));
        Assert.Equal("1235.500", Valor(xml, "Shipment", "GrossWeightMeasure"));
        Assert.Equal("2026-11-27", Valor(xml, "Shipment", "ShipmentStage", "TransitPeriod", "StartDate"));
        Assert.Equal("040129", Valor(xml, "Shipment", "Delivery", "DeliveryAddress", "ID"));
        Assert.Equal("040520", Valor(xml, "Shipment", "Delivery", "Despatch", "DespatchAddress", "ID"));
    }

    [Fact]
    public void En_transporte_privado_van_el_conductor_su_licencia_y_la_placa()
    {
        var xml = GeneradorXmlGuia.Generar(Empresa(), Guia());

        Assert.Equal("02", Valor(xml, "Shipment", "ShipmentStage", "TransportModeCode"));
        Assert.Equal("45879632", Valor(xml, "Shipment", "ShipmentStage", "DriverPerson", "ID"));
        Assert.Equal("Q45879632", Valor(xml, "Shipment", "ShipmentStage", "DriverPerson", "IdentityDocumentReference", "ID"));
        Assert.Equal("V1A952", Valor(xml, "Shipment", "TransportHandlingUnit", "TransportEquipment", "ID"));
        Assert.Empty(xml.Descendants(Cac + "CarrierParty"));
    }

    [Fact]
    public void En_transporte_publico_va_el_RUC_del_transportista_y_no_el_vehiculo()
    {
        var xml = GeneradorXmlGuia.Generar(Empresa(), Guia(publico: true));

        Assert.Equal("01", Valor(xml, "Shipment", "ShipmentStage", "TransportModeCode"));
        Assert.Equal("20100137390", Valor(xml, "Shipment", "ShipmentStage", "CarrierParty", "PartyIdentification", "ID"));
        Assert.Empty(xml.Descendants(Cac + "DriverPerson"));
        Assert.Empty(xml.Descendants(Cac + "TransportHandlingUnit"));
    }

    [Fact]
    public void Las_lineas_llevan_cantidad_y_unidad_del_catalogo_03()
    {
        var xml = GeneradorXmlGuia.Generar(Empresa(), Guia());

        var lineas = xml.Root!.Elements(Cac + "DespatchLine").ToList();
        Assert.Equal(2, lineas.Count);
        var cantidad = lineas[0].Element(Cbc + "DeliveredQuantity")!;
        Assert.Equal("BG", cantidad.Attribute("unitCode")!.Value);
        Assert.Equal("20", cantidad.Value);
        Assert.Equal("Cemento Portland tipo I x 42.5 kg", lineas[0].Element(Cac + "Item")!.Element(Cbc + "Description")!.Value);
    }

    [Fact]
    public void El_XML_de_la_guia_se_firma_y_la_firma_se_verifica()
    {
        var firmado = FirmaXml.Firmar(GeneradorXmlGuia.Generar(Empresa(), Guia()), CertificadoDigital.Autofirmado("20600000005"));

        Assert.True(FirmaXml.Verificar(firmado.Xml));
        Assert.Contains("<DespatchAdvice", firmado.Xml);
    }

    [Fact]
    public void Una_guia_sin_numero_no_se_genera()
    {
        var guia = Guia();
        guia.Numero = 0;

        Assert.Throws<ArgumentException>(() => GeneradorXmlGuia.Generar(Empresa(), guia));
    }
}
