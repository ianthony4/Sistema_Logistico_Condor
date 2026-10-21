using Condor.Datos;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Datos;

[Collection(BaseDatos.Nombre)]
public class AvisoCatalogoTests
{
    private readonly BaseDatosFixture _bd;

    public AvisoCatalogoTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task Otra_PC_recibe_el_aviso_cuando_se_registra_un_producto()
    {
        // "PC-2" escucha; "PC-1" registra el producto.
        await using var aviso = new AvisoCatalogo(_bd.Conexiones.CadenaConexion);
        var recibido = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        aviso.CatalogoCambiado += () => recibido.TrySetResult();
        aviso.Iniciar();
        await EsperarAsync(() => aviso.Conectado);

        await new ProductoRepositorio(_bd.Conexiones)
            .RegistrarAsync(DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Aviso"), 9.90m));

        var termino = await Task.WhenAny(recibido.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(recibido.Task, termino);
    }

    [Fact]
    public async Task Sin_servidor_no_lanza_y_sigue_reintentando()
    {
        await using var aviso = new AvisoCatalogo("Host=127.0.0.1;Port=1;Timeout=1")
        {
            EsperaReintento = TimeSpan.FromMilliseconds(100),
        };

        aviso.Iniciar();
        await Task.Delay(300);

        Assert.False(aviso.Conectado);
    }

    private static async Task EsperarAsync(Func<bool> condicion)
    {
        for (var i = 0; i < 50 && !condicion(); i++)
            await Task.Delay(100);
        Assert.True(condicion(), "La escucha no se conectó a tiempo.");
    }
}
