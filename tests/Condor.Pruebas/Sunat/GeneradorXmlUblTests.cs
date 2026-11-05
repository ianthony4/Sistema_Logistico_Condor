using System.Xml.Linq;
using Condor.Dominio.Entidades;
using Condor.Sunat;
using static Condor.Sunat.GeneradorXmlUbl;

namespace Condor.Pruebas.Sunat;

public class GeneradorXmlUblTests
{
    [Fact]
    public void La_boleta_lleva_version_UBL_2_1_tipo_03_y_su_serie_y_numero()
    {
        var xml = Generar(Empresa(), Boleta());
        var raiz = xml.Root!;

        Assert.Equal(Inv + "Invoice", raiz.Name);
        Assert.Equal("2.1", Valor(raiz, Cbc + "UBLVersionID"));
        Assert.Equal("2.0", Valor(raiz, Cbc + "CustomizationID"));
        Assert.Equal("B001-12", Valor(raiz, Cbc + "ID"));
        var tipo = raiz.Element(Cbc + "InvoiceTypeCode")!;
        Assert.Equal("03", tipo.Value);
        Assert.Equal("0101", tipo.Attribute("listID")!.Value);
        Assert.Equal("PEN", Valor(raiz, Cbc + "DocumentCurrencyCode"));
    }

    [Fact]
    public void La_fecha_y_hora_de_emision_son_las_de_Peru()
    {
        var boleta = Boleta();
        boleta.Fecha = new DateTimeOffset(2026, 11, 5, 2, 30, 15, TimeSpan.Zero); // 21:30 del día 4 en Perú

        var raiz = Generar(Empresa(), boleta).Root!;

        Assert.Equal("2026-11-04", Valor(raiz, Cbc + "IssueDate"));
        Assert.Equal("21:30:15", Valor(raiz, Cbc + "IssueTime"));
    }

    [Fact]
    public void Deja_el_lugar_de_la_firma_vacio_en_el_ExtensionContent()
    {
        var raiz = Generar(Empresa(), Boleta()).Root!;

        var contenido = raiz.Element(Ext + "UBLExtensions")!.Element(Ext + "UBLExtension")!.Element(Ext + "ExtensionContent")!;
        Assert.False(contenido.HasElements);
        Assert.Equal($"#{IdFirma}", raiz.Descendants(Cbc + "URI").Single().Value);
    }

    [Fact]
    public void El_emisor_lleva_RUC_razon_social_y_domicilio_fiscal_con_ubigeo()
    {
        var emisor = Generar(Empresa(), Boleta()).Root!.Element(Cac + "AccountingSupplierParty")!;

        var id = emisor.Descendants(Cac + "PartyIdentification").Single().Element(Cbc + "ID")!;
        Assert.Equal("20600000005", id.Value);
        Assert.Equal("6", id.Attribute("schemeID")!.Value);
        Assert.Equal("FERRETERÍA CÓNDOR MAJES S.A.C.", emisor.Descendants(Cbc + "RegistrationName").Single().Value);
        var domicilio = emisor.Descendants(Cac + "RegistrationAddress").Single();
        Assert.Equal("040520", Valor(domicilio, Cbc + "ID"));
        Assert.Equal("0000", Valor(domicilio, Cbc + "AddressTypeCode"));
        Assert.Equal("MAJES", Valor(domicilio, Cbc + "District"));
    }

    [Fact]
    public void La_boleta_sin_DNI_va_a_clientes_varios_con_tipo_0()
    {
        var receptor = Generar(Empresa(), Boleta()).Root!.Element(Cac + "AccountingCustomerParty")!;

        var id = receptor.Descendants(Cac + "PartyIdentification").Single().Element(Cbc + "ID")!;
        Assert.Equal("0", id.Attribute("schemeID")!.Value);
        Assert.Equal("-", id.Value);
        Assert.Equal("CLIENTES VARIOS", receptor.Descendants(Cbc + "RegistrationName").Single().Value);
    }

    [Fact]
    public void La_boleta_con_DNI_lleva_tipo_1_y_el_nombre_del_cliente()
    {
        var boleta = Boleta();
        boleta.Cliente = new Cliente { Id = 3, TipoDocumento = TipoDocumento.Dni, NumeroDocumento = "45879632", Nombre = "Juan Pérez Quispe" };

        var id = Generar(Empresa(), boleta).Root!.Element(Cac + "AccountingCustomerParty")!
            .Descendants(Cac + "PartyIdentification").Single().Element(Cbc + "ID")!;

        Assert.Equal("1", id.Attribute("schemeID")!.Value);
        Assert.Equal("45879632", id.Value);
    }

    [Fact]
    public void La_factura_lleva_tipo_01_RUC_del_cliente_y_forma_de_pago_al_contado()
    {
        var factura = Boleta();
        factura.TipoComprobante = TipoComprobante.Factura;
        factura.SerieCodigo = "F001";
        factura.Cliente = new Cliente
        {
            Id = 4, TipoDocumento = TipoDocumento.Ruc, NumeroDocumento = "20131312955",
            Nombre = "Constructora Valle de Majes S.A.C.", Direccion = "Av. Principal 450, Majes",
        };

        var raiz = Generar(Empresa(), factura).Root!;

        Assert.Equal("01", Valor(raiz, Cbc + "InvoiceTypeCode"));
        Assert.Equal("F001-12", Valor(raiz, Cbc + "ID"));
        Assert.Equal("6", raiz.Element(Cac + "AccountingCustomerParty")!.Descendants(Cbc + "ID").First().Attribute("schemeID")!.Value);
        Assert.Equal("Contado", raiz.Element(Cac + "PaymentTerms")!.Element(Cbc + "PaymentMeansID")!.Value);
        Assert.Contains(raiz.Element(Cac + "AccountingCustomerParty")!.Descendants(Cbc + "Line"), l => l.Value.Contains("Majes"));
    }

    [Fact]
    public void La_boleta_no_lleva_forma_de_pago()
    {
        Assert.Null(Generar(Empresa(), Boleta()).Root!.Element(Cac + "PaymentTerms"));
    }

    [Fact]
    public void Los_totales_son_los_del_comprobante_y_cuadran()
    {
        var raiz = Generar(Empresa(), Boleta()).Root!;

        var impuestos = raiz.Element(Cac + "TaxTotal")!;
        Assert.Equal("17.62", Valor(impuestos, Cbc + "TaxAmount"));
        Assert.Equal("97.88", impuestos.Element(Cac + "TaxSubtotal")!.Element(Cbc + "TaxableAmount")!.Value);
        var totales = raiz.Element(Cac + "LegalMonetaryTotal")!;
        Assert.Equal("97.88", Valor(totales, Cbc + "LineExtensionAmount"));
        Assert.Equal("115.50", Valor(totales, Cbc + "TaxInclusiveAmount"));
        Assert.Equal("115.50", Valor(totales, Cbc + "PayableAmount"));
        Assert.All(raiz.Descendants().Where(e => e.Attribute("currencyID") is not null),
            e => Assert.Equal("PEN", e.Attribute("currencyID")!.Value));
    }

    [Fact]
    public void Cada_linea_separa_valor_de_venta_e_IGV_y_lleva_el_precio_con_IGV()
    {
        var linea = Generar(Empresa(), Boleta()).Root!.Elements(Cac + "InvoiceLine").First();

        Assert.Equal("1", Valor(linea, Cbc + "ID"));
        var cantidad = linea.Element(Cbc + "InvoicedQuantity")!;
        Assert.Equal("2", cantidad.Value);
        Assert.Equal("BG", cantidad.Attribute("unitCode")!.Value);
        Assert.Equal("55.08", Valor(linea, Cbc + "LineExtensionAmount"));   // 65.00 / 1.18
        Assert.Equal("9.92", linea.Element(Cac + "TaxTotal")!.Element(Cbc + "TaxAmount")!.Value);
        Assert.Equal("32.50", linea.Descendants(Cac + "AlternativeConditionPrice").Single().Element(Cbc + "PriceAmount")!.Value);
        Assert.Equal("27.542373", linea.Element(Cac + "Price")!.Element(Cbc + "PriceAmount")!.Value);
        Assert.Equal("10", linea.Descendants(Cbc + "TaxExemptionReasonCode").Single().Value);
        Assert.Equal("Cemento Portland tipo I x 42.5 kg", linea.Descendants(Cbc + "Description").Single().Value);
    }

    [Fact]
    public void Las_lineas_suman_lo_mismo_que_el_total_con_tolerancia_de_centimos()
    {
        var boleta = Boleta();
        var lineas = boleta.Detalle.Select(Importes).ToList();

        Assert.Equal(boleta.Total, lineas.Sum(l => l.ValorVenta + l.Igv));
        Assert.True(Math.Abs(boleta.OpGravada - lineas.Sum(l => l.ValorVenta)) <= 0.05m);
    }

    [Fact]
    public void La_leyenda_1000_lleva_el_importe_en_letras()
    {
        var nota = Generar(Empresa(), Boleta()).Root!.Element(Cbc + "Note")!;

        Assert.Equal("1000", nota.Attribute("languageLocaleID")!.Value);
        Assert.Equal("CIENTO QUINCE CON 50/100 SOLES", nota.Value);
    }

    [Theory]
    [InlineData("UND", "NIU")]
    [InlineData("VAR", "NIU")]
    [InlineData("KG", "KGM")]
    [InlineData("GAL", "GLL")]
    [InlineData("BLS", "BG")]
    [InlineData("M", "MTR")]
    [InlineData("caja", "BX")]
    [InlineData(null, "NIU")]
    public void Las_unidades_del_catalogo_se_traducen_al_catalogo_03(string? unidad, string esperado)
    {
        Assert.Equal(esperado, UnidadSunat(unidad));
    }

    [Fact]
    public void El_nombre_del_archivo_es_RUC_tipo_serie_y_numero()
    {
        Assert.Equal("20600000005-03-B001-12", NombreArchivo(Empresa(), Boleta()));
    }

    [Fact]
    public void Una_nota_de_venta_o_un_comprobante_sin_numero_no_se_generan()
    {
        var nota = Boleta();
        nota.TipoComprobante = TipoComprobante.NotaVenta;
        var sinNumero = Boleta();
        sinNumero.Numero = 0;

        Assert.Throws<ArgumentException>(() => Generar(Empresa(), nota));
        Assert.Throws<ArgumentException>(() => Generar(Empresa(), sinNumero));
    }

    [Fact]
    public void El_XML_se_puede_volver_a_leer_y_conserva_los_caracteres_especiales()
    {
        var boleta = Boleta();
        boleta.Detalle[1].Descripcion = "Tubo PVC 1/2\" & codo <90°> Ñ";

        var texto = Generar(Empresa(), boleta).ToString();
        var leido = XDocument.Parse(texto);

        Assert.Contains(leido.Descendants(Cbc + "Description"), d => d.Value == "Tubo PVC 1/2\" & codo <90°> Ñ");
    }

    private static string Valor(XElement padre, XName nombre) => padre.Element(nombre)!.Value;

    private static Empresa Empresa() => new()
    {
        Ruc = "20600000005",
        RazonSocial = "FERRETERÍA CÓNDOR MAJES S.A.C.",
        NombreComercial = "Ferretería Cóndor",
        Direccion = "Majes, Caylloma - Arequipa",
    };

    /// <summary>Boleta B001-12 sin cliente: 2 bolsas de cemento y 1 galón de esmalte, total 115.50.</summary>
    private static Comprobante Boleta()
    {
        var boleta = new Comprobante
        {
            Id = 12,
            TipoComprobante = TipoComprobante.Boleta,
            SerieCodigo = "B001",
            Numero = 12,
            Fecha = new DateTimeOffset(2026, 11, 4, 10, 15, 0, TimeSpan.FromHours(-5)),
            OpGravada = 97.88m,
            Igv = 17.62m,
            Total = 115.50m,
        };
        boleta.Detalle.Add(new DetalleComprobante
        {
            Item = 1, ProductoId = 7, Descripcion = "Cemento Portland tipo I x 42.5 kg", Unidad = "BLS",
            Cantidad = 2, PrecioUnitario = 32.50m, Importe = 65.00m,
        });
        boleta.Detalle.Add(new DetalleComprobante
        {
            Item = 2, ProductoId = 9, Descripcion = "Esmalte sintético blanco 1 gal", Unidad = "GAL",
            Cantidad = 1, PrecioUnitario = 50.50m, Importe = 50.50m,
        });
        return boleta;
    }
}
