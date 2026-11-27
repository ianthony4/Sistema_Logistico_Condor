using System.Security.Cryptography.X509Certificates;
using Condor.Dominio.Entidades;
using Condor.Sunat;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Sunat;

/// <summary>Revisión de lo que falta para pasar a producción (T-99).</summary>
public sealed class ChequeoProduccionTests : IDisposable
{
    private readonly string _carpeta = Directory.CreateTempSubdirectory("condor-produccion").FullName;

    public void Dispose() => Directory.Delete(_carpeta, recursive: true);

    private static Empresa EmpresaReal() => new()
    {
        Ruc = "20131312955", RazonSocial = "FERRETERÍA CÓNDOR MAJES S.A.C.", Direccion = "Av. Principal 120, Majes",
    };

    /// <summary>Exporta un certificado de prueba que vence en <paramref name="dias"/> días como .pfx.</summary>
    private string Certificado(int dias)
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var pedido = new CertificateRequest("CN=FERRETERIA CONDOR, SERIALNUMBER=20131312955", rsa,
            System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var cert = pedido.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(dias));
        var ruta = Path.Combine(_carpeta, "condor.pfx");
        File.WriteAllBytes(ruta, cert.Export(X509ContentType.Pfx, "clave"));
        return ruta;
    }

    private ConfiguracionSunat Produccion(int diasCertificado = 365) => new()
    {
        Ambiente = "Produccion", UsuarioSol = "CONDOR01", ClaveSol = "x", Certificado = Certificado(diasCertificado),
        ClaveCertificado = "clave", ClientId = "id", ClientSecret = "secreto",
    };

    [Fact]
    public void En_beta_no_se_revisa_nada()
    {
        Assert.Empty(ChequeoProduccion.Pendientes(new ConfiguracionSunat(), DatosPrueba.Empresa()));
    }

    [Fact]
    public void Con_todo_configurado_no_hay_pendientes()
    {
        Assert.Empty(ChequeoProduccion.Pendientes(Produccion(), EmpresaReal()));
    }

    [Fact]
    public void Avisa_si_la_empresa_sigue_con_el_RUC_provisional()
    {
        var pendientes = ChequeoProduccion.Pendientes(Produccion(), DatosPrueba.Empresa());

        Assert.Contains("provisionales", Assert.Single(pendientes));
    }

    [Fact]
    public void Avisa_lo_que_falta_en_condor_json()
    {
        var config = new ConfiguracionSunat { Ambiente = "Produccion" };

        var pendientes = ChequeoProduccion.Pendientes(config, EmpresaReal());

        Assert.Equal(3, pendientes.Count);
        Assert.Contains(pendientes, p => p.Contains("usuario secundario SOL"));
        Assert.Contains(pendientes, p => p.Contains("certificado digital (.pfx)"));
        Assert.Contains(pendientes, p => p.Contains("API de guías"));
    }

    [Fact]
    public void Avisa_un_mes_antes_de_que_venza_el_certificado()
    {
        var pendientes = ChequeoProduccion.Pendientes(Produccion(diasCertificado: 10), EmpresaReal());

        Assert.Contains("vence el", Assert.Single(pendientes));
    }

    [Fact]
    public void Avisa_si_el_certificado_no_se_puede_abrir()
    {
        var config = Produccion();
        config.ClaveCertificado = "equivocada";

        Assert.Contains("no se pudo abrir", Assert.Single(ChequeoProduccion.Pendientes(config, EmpresaReal())));
    }
}
