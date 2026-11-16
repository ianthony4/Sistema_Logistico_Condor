using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using System.Xml.Linq;

namespace Condor.Sunat;

/// <summary>XML firmado listo para comprimir y enviar, con su valor resumen.</summary>
public sealed record XmlFirmado(string Xml, string Hash);

/// <summary>
/// Firma digital del XML UBL como la pide la SUNAT (HU-07, T-42), igual que
/// greenter/xmldsig: firma XMLDSig "enveloped" sobre todo el documento
/// (Reference URI=""), canonicalización C14N, RSA-SHA1 y el certificado en
/// KeyInfo. La firma va dentro del último ext:ExtensionContent.
/// </summary>
public static class FirmaXml
{
    public static XmlFirmado Firmar(XDocument documento, X509Certificate2 certificado)
    {
        var rsa = certificado.GetRSAPrivateKey()
                  ?? throw new CertificadoInvalidoException("El certificado digital no tiene la clave privada RSA para firmar.");

        // PreserveWhitespace: lo que se firma tiene que ser exactamente lo que se envía.
        var xml = new XmlDocument { PreserveWhitespace = true };
        xml.LoadXml(documento.Declaration + documento.ToString(SaveOptions.DisableFormatting));

        var lugar = xml.GetElementsByTagName("ExtensionContent", GeneradorXmlUbl.Ext.NamespaceName);
        var contenedor = lugar.Count > 0 ? (XmlElement)lugar[lugar.Count - 1]! : xml.DocumentElement!;

        var firma = new SignedXml(xml) { SigningKey = rsa };
        firma.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;
        firma.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA1Url;
        firma.Signature.Id = GeneradorXmlUbl.IdFirma;

        var referencia = new Reference(string.Empty) { DigestMethod = SignedXml.XmlDsigSHA1Url };
        referencia.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        firma.AddReference(referencia);

        var datosCertificado = new KeyInfo();
        datosCertificado.AddClause(new KeyInfoX509Data(certificado));
        firma.KeyInfo = datosCertificado;

        firma.ComputeSignature();
        // Sin prefijo ds: cambiarlo después de firmar alteraría el SignedInfo
        // canonicalizado y la firma dejaría de ser válida. La SUNAT acepta ambas formas.
        contenedor.AppendChild(xml.ImportNode(firma.GetXml(), deep: true));

        var hash = Convert.ToBase64String(referencia.DigestValue!);
        return new XmlFirmado(xml.OuterXml, hash);
    }

    /// <summary>True si la firma del XML es válida con el certificado que trae dentro.</summary>
    public static bool Verificar(string xmlFirmado)
    {
        var xml = new XmlDocument { PreserveWhitespace = true };
        xml.LoadXml(xmlFirmado);
        var firmas = xml.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl);
        if (firmas.Count != 1)
            return false;

        var firma = new SignedXml(xml);
        firma.LoadXml((XmlElement)firmas[0]!);
        return firma.CheckSignature();
    }
}
