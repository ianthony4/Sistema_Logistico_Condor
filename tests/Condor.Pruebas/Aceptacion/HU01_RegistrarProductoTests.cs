using Condor.Datos;
using Condor.Dominio.Validacion;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;
using Npgsql;

namespace Condor.Pruebas.Aceptacion;

/// <summary>
/// HU-01: registrar producto en el catálogo.
/// Criterio 1: con un nombre no vacío y un precio mayor que cero, al presionar
/// Guardar el producto queda disponible de inmediato en las tres PC.
/// Criterio 2: si ya existe un producto con el mismo nombre, el sistema lo
/// advierte y no crea el duplicado.
/// </summary>
[Collection(BaseDatos.Nombre)]
public class HU01_RegistrarProductoTests
{
    private readonly BaseDatosFixture _bd;

    public HU01_RegistrarProductoTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task Producto_guardado_en_la_PC1_aparece_de_inmediato_en_la_PC2_y_la_PC3()
    {
        await using var local = new TresPC();
        var nombre = DatosPrueba.NombreUnico("Esmalte sintético rojo 1 gal");
        var dialogo = new NuevoProductoViewModel(new ProductoRepositorio(local.Pc[0]))
        {
            Nombre = nombre,
            PrecioTexto = "58.00",
            Unidad = "GAL",
        };
        var cerrado = false;
        dialogo.Cerrar += g => cerrado = g;

        await dialogo.GuardarCommand.EjecutarAsync();

        Assert.True(cerrado, dialogo.Error);
        foreach (var pc in local.Pc.Skip(1))
        {
            var encontrados = await new ProductoRepositorio(pc).BuscarPorNombreAsync(nombre);
            var producto = Assert.Single(encontrados);
            Assert.Equal(58.00m, producto.Precio);
            Assert.Equal("GAL", producto.Unidad);
        }
    }

    [FactConBD]
    public async Task Nombre_repetido_se_advierte_y_no_se_crea_el_duplicado()
    {
        var repo = new ProductoRepositorio(_bd.Conexiones);
        var nombre = DatosPrueba.NombreUnico("Cemento Portland");
        await repo.RegistrarAsync(DatosPrueba.Nuevo(nombre, 32.50m));

        // Mismo nombre con otras mayúsculas y espacios de más.
        var dialogo = new NuevoProductoViewModel(repo)
        {
            Nombre = "  " + nombre.ToUpperInvariant().Replace(" ", "  "),
            PrecioTexto = "33",
        };
        var cerrado = false;
        dialogo.Cerrar += _ => cerrado = true;

        await dialogo.GuardarCommand.EjecutarAsync();

        Assert.False(cerrado);
        Assert.Contains("Ya existe", dialogo.Error);
        Assert.Equal(1L, await ContarAsync(nombre));
    }

    [FactConBD]
    public async Task Dos_PC_que_guardan_el_mismo_producto_a_la_vez_crean_uno_solo()
    {
        await using var local = new TresPC();
        var nombre = DatosPrueba.NombreUnico("Clavo 2 in");

        var intentos = local.Pc.Take(2)
            .Select(pc => Capturar(() => new ProductoRepositorio(pc).RegistrarAsync(DatosPrueba.Nuevo(nombre, 7.5m))))
            .ToArray();
        var errores = await Task.WhenAll(intentos);

        Assert.Single(errores, e => e is null);
        Assert.Single(errores, e => e is ProductoDuplicadoException);
        Assert.Equal(1L, await ContarAsync(nombre));
    }

    [Theory]
    [InlineData("   ", "10", "nombre")]
    [InlineData("Martillo", "0", "mayor que cero")]
    [InlineData("Martillo", "-3", "mayor que cero")]
    public async Task Sin_nombre_o_con_precio_no_positivo_no_se_guarda(string nombre, string precio, string mensaje)
    {
        var repo = new ProductoRepositorioEnMemoria();
        var dialogo = new NuevoProductoViewModel(repo) { Nombre = nombre, PrecioTexto = precio };
        var cerrado = false;
        dialogo.Cerrar += _ => cerrado = true;

        await dialogo.GuardarCommand.EjecutarAsync();

        Assert.False(cerrado);
        Assert.Empty(repo.Productos);
        if (dialogo.GuardarCommand.CanExecute(null))
            Assert.Contains(mensaje, dialogo.Error);
    }

    [FactConBD]
    public async Task El_validador_frena_el_producto_antes_de_llegar_a_la_base_de_datos()
    {
        var repo = new ProductoRepositorio(_bd.Conexiones);
        var nombre = DatosPrueba.NombreUnico("Gratis");

        await Assert.ThrowsAsync<ProductoInvalidoException>(() => repo.RegistrarAsync(DatosPrueba.Nuevo(nombre, 0m)));
        Assert.Equal(0L, await ContarAsync(nombre));
    }

    private static async Task<Exception?> Capturar(Func<Task> accion)
    {
        try
        {
            await accion();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private async Task<long> ContarAsync(string nombre)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM producto WHERE lower(nombre) = lower(@n)", conexion);
        cmd.Parameters.AddWithValue("n", nombre);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
