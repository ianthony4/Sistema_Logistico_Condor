using System.Security.Cryptography.X509Certificates;
using System.Xml;
using Condor.Dominio.Entidades;
using Condor.Sunat;

namespace Condor.Pruebas.Sunat;

public class FirmaXmlTests
{
    private static readonly X509Certificate2 Certificado = CertificadoDigital.Autofirmado("20600000005");

    [Fact]
    public void La_firma_es_valida_y_va_dentro_del_ExtensionContent()
    {
        var firmado = FirmaXml.Firmar(GeneradorXmlUbl.Generar(Empresa(), Boleta()), Certificado);

        Assert.True(FirmaXml.Verificar(firmado.Xml));
        var xml = Cargar(firmado.Xml);
        var firma = (XmlElement)xml.GetElementsByTagName("Signature", "http://www.w3.org/2000/09/xmldsig#")[0]!;
        Assert.Equal("ExtensionContent", firma.ParentNode!.LocalName);
        Assert.Equal(GeneradorXmlUbl.IdFirma, firma.GetAttribute("Id"));
    }

    [Fact]
    public void Usa_C14N_RSA_SHA1_y_la_transformacion_enveloped_como_greenter()
    {
        var xml = Cargar(FirmaXml.Firmar(GeneradorXmlUbl.Generar(Empresa(), Boleta()), Certificado).Xml);

        string Algoritmo(string elemento) =>
            ((XmlElement)xml.GetElementsByTagName(elemento, "http://www.w3.org/2000/09/xmldsig#")[0]!).GetAttribute("Algorithm");

        Assert.Equal("http://www.w3.org/TR/2001/REC-xml-c14n-20010315", Algoritmo("CanonicalizationMethod"));
        Assert.Equal("http://www.w3.org/2000/09/xmldsig#rsa-sha1", Algoritmo("SignatureMethod"));
        Assert.Equal("http://www.w3.org/2000/09/xmldsig#enveloped-signature", Algoritmo("Transform"));
        Assert.Equal("http://www.w3.org/2000/09/xmldsig#sha1", Algoritmo("DigestMethod"));
        var referencia = (XmlElement)xml.GetElementsByTagName("Reference", "http://www.w3.org/2000/09/xmldsig#")[0]!;
        Assert.Equal("", referencia.GetAttribute("URI"));
        Assert.Single(xml.GetElementsByTagName("X509Certificate", "http://www.w3.org/2000/09/xmldsig#").Cast<XmlNode>());
    }

    [Fact]
    public void El_hash_es_el_DigestValue_de_la_firma()
    {
        var firmado = FirmaXml.Firmar(GeneradorXmlUbl.Generar(Empresa(), Boleta()), Certificado);

        var digest = Cargar(firmado.Xml).GetElementsByTagName("DigestValue", "http://www.w3.org/2000/09/xmldsig#")[0]!.InnerText;
        Assert.Equal(digest, firmado.Hash);
        Assert.Equal(28, firmado.Hash.Length); // SHA-1 en base64
    }

    [Fact]
    public void Si_se_cambia_el_total_despues_de_firmar_la_firma_ya_no_vale()
    {
        var firmado = FirmaXml.Firmar(GeneradorXmlUbl.Generar(Empresa(), Boleta()), Certificado);

        var adulterado = firmado.Xml.Replace(">65.00<", ">6.50<");

        Assert.NotEqual(firmado.Xml, adulterado);
        Assert.False(FirmaXml.Verificar(adulterado));
    }

    [Fact]
    public void El_XML_firmado_empieza_con_la_declaracion_UTF_8()
    {
        var firmado = FirmaXml.Firmar(GeneradorXmlUbl.Generar(Empresa(), Boleta()), Certificado);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?><Invoice", firmado.Xml);
    }

    [Fact]
    public void Carga_el_certificado_desde_un_pfx_con_clave_y_desde_un_pem()
    {
        var carpeta = Directory.CreateTempSubdirectory("condor-cert-").FullName;
        try
        {
            var pfx = Path.Combine(carpeta, "certificado.pfx");
            File.WriteAllBytes(pfx, Certificado.Export(X509ContentType.Pfx, "clave123"));
            var pem = Path.Combine(carpeta, "certificado.pem");
            File.WriteAllText(pem, Certificado.GetRSAPrivateKey()!.ExportPkcs8PrivateKeyPem() + "\n" + Certificado.ExportCertificatePem());

            Assert.True(CertificadoDigital.Cargar(pfx, "clave123").HasPrivateKey);
            Assert.True(CertificadoDigital.Cargar(pem).HasPrivateKey);
            var firmado = FirmaXml.Firmar(GeneradorXmlUbl.Generar(Empresa(), Boleta()), CertificadoDigital.Cargar(pem));
            Assert.True(FirmaXml.Verificar(firmado.Xml));
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    [Fact]
    public void Una_clave_equivocada_o_un_archivo_que_no_existe_dan_un_mensaje_claro()
    {
        var carpeta = Directory.CreateTempSubdirectory("condor-cert-").FullName;
        try
        {
            var pfx = Path.Combine(carpeta, "certificado.pfx");
            File.WriteAllBytes(pfx, Certificado.Export(X509ContentType.Pfx, "clave123"));

            Assert.Contains("clave", Assert.Throws<CertificadoInvalidoException>(() => CertificadoDigital.Cargar(pfx, "otra")).Message);
            Assert.Contains("No se encontró", Assert.Throws<CertificadoInvalidoException>(
                () => CertificadoDigital.Cargar(Path.Combine(carpeta, "no-existe.pfx"))).Message);
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    [Fact]
    public void Un_certificado_vencido_se_detecta()
    {
        Assert.False(CertificadoDigital.Vencido(Certificado));
        Assert.True(CertificadoDigital.Vencido(Certificado, DateTime.Now.AddYears(5)));
    }

    private static XmlDocument Cargar(string texto)
    {
        var xml = new XmlDocument { PreserveWhitespace = true };
        xml.LoadXml(texto);
        return xml;
    }

    private static Empresa Empresa() => new()
    {
        Ruc = "20600000005",
        RazonSocial = "FERRETERÍA CÓNDOR MAJES S.A.C.",
        Direccion = "Majes, Caylloma - Arequipa",
    };

    private static Comprobante Boleta()
    {
        var boleta = new Comprobante
        {
            Id = 1, TipoComprobante = TipoComprobante.Boleta, SerieCodigo = "B001", Numero = 1,
            Fecha = DateTimeOffset.Now, OpGravada = 55.08m, Igv = 9.92m, Total = 65.00m,
        };
        boleta.Detalle.Add(new DetalleComprobante
        {
            Item = 1, ProductoId = 1, Descripcion = "Cemento Portland tipo I x 42.5 kg", Unidad = "BLS",
            Cantidad = 2, PrecioUnitario = 32.50m, Importe = 65.00m,
        });
        return boleta;
    }
}
