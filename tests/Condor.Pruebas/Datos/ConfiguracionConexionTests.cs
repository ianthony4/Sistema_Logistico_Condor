using Condor.Datos;
using Npgsql;

namespace Condor.Pruebas.Datos;

public class ConfiguracionConexionTests : IDisposable
{
    private readonly string _carpeta = Directory.CreateTempSubdirectory("condor_cfg_").FullName;

    public void Dispose() => Directory.Delete(_carpeta, recursive: true);

    private void Escribir(string json) =>
        File.WriteAllText(Path.Combine(_carpeta, ConfiguracionConexion.NombreArchivo), json);

    [Fact]
    public void Lee_condor_json_con_comentarios()
    {
        Escribir("""
            {
              // PC principal
              "Servidor": "192.168.1.10",
              "Puerto": 5432,
              "BaseDatos": "condor",
              "Usuario": "condor_app",
              "Clave": "secreta",
            }
            """);

        var config = ConfiguracionConexion.Cargar(_carpeta);

        Assert.Equal("192.168.1.10", config.Servidor);
        Assert.Equal("192.168.1.10:5432/condor", config.ToString());
    }

    [Fact]
    public void Sin_archivo_avisa_que_hay_que_copiar_la_plantilla()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => ConfiguracionConexion.Cargar(_carpeta));
        Assert.Contains("condor.ejemplo.json", ex.Message);
    }

    [Fact]
    public void Json_invalido_da_un_mensaje_claro()
    {
        Escribir("{ \"Servidor\": ");
        Assert.Throws<InvalidDataException>(() => ConfiguracionConexion.Cargar(_carpeta));
    }

    [Fact]
    public void Puerto_fuera_de_rango_no_es_valido()
    {
        Escribir("""{ "Servidor": "localhost", "Puerto": 70000 }""");
        Assert.Throws<InvalidDataException>(() => ConfiguracionConexion.Cargar(_carpeta));
    }

    [Fact]
    public void La_cadena_identifica_a_la_terminal_y_usa_tiempo_de_espera_corto()
    {
        var config = new ConfiguracionConexion { Servidor = "192.168.1.10", Clave = "x" };

        var cadena = new NpgsqlConnectionStringBuilder(config.CadenaConexion("PC-2"));

        Assert.Equal("CondorVentas@PC-2", cadena.ApplicationName);
        Assert.Equal(5, cadena.Timeout);
        Assert.Equal("condor_app", cadena.Username);
    }

    [Fact]
    public async Task Sin_servidor_ProbarAsync_devuelve_false_sin_lanzar()
    {
        // Puerto cerrado en la propia PC: la conexión falla de inmediato.
        var config = new ConfiguracionConexion { Servidor = "127.0.0.1", Puerto = 1, TiempoEsperaSegundos = 2 };
        await using var fabrica = new FabricaConexiones(config);

        Assert.False(await fabrica.ProbarAsync());
    }
}
