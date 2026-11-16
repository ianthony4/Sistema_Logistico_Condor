using Condor.Datos.Respaldos;

namespace Condor.Pruebas.Datos;

public class ConfiguracionRespaldoTests : IDisposable
{
    private readonly string _carpeta = Directory.CreateTempSubdirectory("condor_respaldo_").FullName;

    public void Dispose() => Directory.Delete(_carpeta, recursive: true);

    private void Escribir(string json) =>
        File.WriteAllText(Path.Combine(_carpeta, ConfiguracionRespaldo.NombreArchivo), json);

    [Fact]
    public void Lee_la_seccion_Respaldo_de_condor_json_con_comentarios()
    {
        Escribir("""
            {
              "Servidor": "localhost",
              // Disco externo de la PC principal
              "Respaldo": {
                "Carpeta": "E:\\Respaldos Condor",
                "AvisoHoras": 12,
              }
            }
            """);

        var config = ConfiguracionRespaldo.Cargar(_carpeta);

        Assert.True(config.Configurada);
        Assert.Equal(@"E:\Respaldos Condor", config.Carpeta);
        Assert.Equal(12, config.AvisoHoras);
        Assert.Equal(string.Empty, config.PgDump);
    }

    [Fact]
    public void Sin_archivo_o_sin_seccion_esta_PC_no_genera_respaldos()
    {
        Assert.False(ConfiguracionRespaldo.Cargar(_carpeta).Configurada);

        Escribir("""{ "Servidor": "192.168.1.10" }""");
        var config = ConfiguracionRespaldo.Cargar(_carpeta);
        Assert.False(config.Configurada);
        Assert.Equal(24, config.AvisoHoras);
        Assert.Equal("sin disco de respaldo", config.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    public void Horas_de_aviso_fuera_de_rango_se_rechazan(int horas)
    {
        Escribir($$"""{ "Respaldo": { "Carpeta": "E:\\R", "AvisoHoras": {{horas}} } }""");

        var ex = Assert.Throws<InvalidDataException>(() => ConfiguracionRespaldo.Cargar(_carpeta));
        Assert.Contains("AvisoHoras", ex.Message);
    }

    [Fact]
    public void La_plantilla_condor_ejemplo_json_es_valida()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "CondorVentas.sln")))
            raiz = raiz.Parent;
        File.Copy(Path.Combine(raiz!.FullName, "src", "Condor.Escritorio", "condor.ejemplo.json"),
            Path.Combine(_carpeta, ConfiguracionRespaldo.NombreArchivo));

        var config = ConfiguracionRespaldo.Cargar(_carpeta);

        Assert.Equal(@"E:\Respaldos Condor", config.Carpeta);
        Assert.Equal(24, config.AvisoHoras);
    }
}
