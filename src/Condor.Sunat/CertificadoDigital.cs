using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Condor.Sunat;

/// <summary>
/// Certificado digital con el que la ferretería firma sus comprobantes
/// (HU-07, T-42). Se lee del archivo que entrega la entidad certificadora:
/// .pfx / .p12 con su clave, o un .pem con la clave privada y el certificado.
/// </summary>
public static class CertificadoDigital
{
    public static X509Certificate2 Cargar(string ruta, string? clave = null)
    {
        if (string.IsNullOrWhiteSpace(ruta))
            throw new CertificadoInvalidoException("Falta la ruta del certificado digital.");
        if (!File.Exists(ruta))
            throw new CertificadoInvalidoException($"No se encontró el certificado digital en {ruta}.");

        X509Certificate2 certificado;
        try
        {
            certificado = Path.GetExtension(ruta).ToLowerInvariant() switch
            {
                ".pem" or ".crt" => CargarPem(ruta, clave),
                _ => new X509Certificate2(ruta, clave, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet),
            };
        }
        catch (CryptographicException ex)
        {
            throw new CertificadoInvalidoException(
                $"No se pudo abrir el certificado {Path.GetFileName(ruta)}: revise la clave del certificado. ({ex.Message})", ex);
        }

        if (!certificado.HasPrivateKey || certificado.GetRSAPrivateKey() is null)
            throw new CertificadoInvalidoException(
                $"El certificado {Path.GetFileName(ruta)} no trae la clave privada RSA: pida a la entidad certificadora el .pfx completo.");
        return certificado;
    }

    /// <summary>True si el certificado ya venció o aún no entra en vigencia.</summary>
    public static bool Vencido(X509Certificate2 certificado, DateTime? ahora = null)
    {
        var fecha = ahora ?? DateTime.Now;
        return fecha < certificado.NotBefore || fecha > certificado.NotAfter;
    }

    /// <summary>
    /// Certificado autofirmado para el ambiente beta de la SUNAT, que no valida
    /// la entidad certificadora. Nunca se usa en producción.
    /// </summary>
    public static X509Certificate2 Autofirmado(string ruc)
    {
        using var rsa = RSA.Create(2048);
        var solicitud = new CertificateRequest(
            $"CN=CONDOR PRUEBAS BETA, OU=RUC {ruc}, O=Ferreteria Condor, C=PE", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generado = solicitud.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(2));
        // Se reexporta para que la clave privada quede utilizable en Windows.
        return new X509Certificate2(generado.Export(X509ContentType.Pfx), (string?)null,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
    }

    private static X509Certificate2 CargarPem(string ruta, string? clave) =>
        string.IsNullOrEmpty(clave)
            ? X509Certificate2.CreateFromPemFile(ruta)
            : X509Certificate2.CreateFromEncryptedPemFile(ruta, clave);
}

/// <summary>El certificado digital no existe, la clave es incorrecta o no sirve para firmar.</summary>
public sealed class CertificadoInvalidoException : Exception
{
    public CertificadoInvalidoException(string mensaje, Exception? causa = null) : base(mensaje, causa)
    {
    }
}
