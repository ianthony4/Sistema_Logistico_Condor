using System.Security.Cryptography.X509Certificates;
using Condor.Sunat;

namespace Condor.Pruebas.Sunat;

public class ConfiguracionSunatTests : IDisposable
{
    private readonly string _carpeta = Directory.CreateTempSubdirectory("condor_sunat_").FullName;

    public void Dispose() => Directory.Delete(_carpeta, recursive: true);

    private void Escribir(string json) =>
        File.WriteAllText(Path.Combine(_carpeta, ConfiguracionSunat.NombreArchivo), json);

    [Fact]
    public void Lee_la_seccion_Sunat_de_condor_json_con_comentarios()
    {
        Escribir("""
            {
              "Servidor": "192.168.1.10",
              // Envío a la SUNAT
              "Sunat": {
                "Ambiente": "Beta",
                "UsuarioSol": "moddatos",
                "ClaveSol": "moddatos",
                "EsperaSegundos": 20,
              }
            }
            """);

        var config = ConfiguracionSunat.Cargar(_carpeta);

        Assert.True(config.Configurada);
        Assert.False(config.EsProduccion);
        Assert.Equal(ConfiguracionSunat.UrlBeta, config.UrlServicio);
        Assert.Equal(20, config.EsperaSegundos);
        Assert.Equal("SUNAT beta", config.ToString());
    }

    [Fact]
    public void Sin_archivo_o_sin_seccion_queda_sin_configurar()
    {
        Assert.False(ConfiguracionSunat.Cargar(_carpeta).Configurada);

        Escribir("""{ "Servidor": "localhost" }""");
        Assert.False(ConfiguracionSunat.Cargar(_carpeta).Configurada);
        Assert.Equal("SUNAT sin configurar", ConfiguracionSunat.Cargar(_carpeta).ToString());
    }

    [Theory]
    [InlineData("MODDATOS", "20600000005MODDATOS")]
    [InlineData("moddatos", "20600000005MODDATOS")]
    [InlineData("20600000005CONDOR01", "20600000005CONDOR01")]
    public void El_usuario_del_servicio_es_el_RUC_mas_el_usuario_SOL(string usuario, string esperado)
    {
        var config = new ConfiguracionSunat { UsuarioSol = usuario, ClaveSol = "x" };

        Assert.Equal(esperado, config.UsuarioServicio("20600000005"));
    }

    [Fact]
    public void En_produccion_usa_la_URL_de_produccion_y_exige_el_certificado()
    {
        var config = new ConfiguracionSunat { Ambiente = "Produccion", UsuarioSol = "CONDOR01", ClaveSol = "x" };

        Assert.Equal(ConfiguracionSunat.UrlProduccion, config.UrlServicio);
        Assert.Contains("Certificado es obligatorio", Assert.Throws<InvalidDataException>(config.Validar).Message);
        Assert.Throws<CertificadoInvalidoException>(() => config.CargarCertificado("20600000005"));
    }

    [Theory]
    [InlineData("""{ "Sunat": { "Ambiente": "Pruebas" } }""", "Ambiente")]
    [InlineData("""{ "Sunat": { "EsperaSegundos": 1 } }""", "EsperaSegundos")]
    [InlineData("""{ "Sunat": { "Url": "no es url" } }""", "Url")]
    [InlineData("""{ "Sunat": { "Ambiente": 5 } }""", "no es válida")]
    public void Una_seccion_mal_escrita_se_avisa(string json, string mensaje)
    {
        Escribir(json);

        Assert.Contains(mensaje, Assert.Throws<InvalidDataException>(() => ConfiguracionSunat.Cargar(_carpeta)).Message);
    }

    [Fact]
    public void En_beta_sin_certificado_firma_con_uno_de_prueba()
    {
        var config = new ConfiguracionSunat { UsuarioSol = "MODDATOS", ClaveSol = "moddatos" };

        var certificado = config.CargarCertificado("20600000005");

        Assert.True(certificado.HasPrivateKey);
        Assert.Contains("20600000005", certificado.Subject);
    }

    [Fact]
    public void La_ruta_relativa_del_certificado_se_busca_en_la_carpeta_del_programa()
    {
        var prueba = CertificadoDigital.Autofirmado("20600000005");
        File.WriteAllBytes(Path.Combine(_carpeta, "condor.pfx"), prueba.Export(X509ContentType.Pfx, "clave"));
        var config = new ConfiguracionSunat { Certificado = "condor.pfx", ClaveCertificado = "clave" };

        Assert.Equal(prueba.Thumbprint, config.CargarCertificado("20600000005", _carpeta).Thumbprint);
    }

    [Fact]
    public void La_plantilla_condor_ejemplo_json_trae_la_seccion_para_beta()
    {
        var plantilla = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Condor.Escritorio", "condor.ejemplo.json");
        File.Copy(plantilla, Path.Combine(_carpeta, ConfiguracionSunat.NombreArchivo));

        var config = ConfiguracionSunat.Cargar(_carpeta);

        Assert.True(config.Configurada);
        Assert.Equal("MODDATOS", config.UsuarioSol);
        Assert.False(config.EsProduccion);
    }
}
