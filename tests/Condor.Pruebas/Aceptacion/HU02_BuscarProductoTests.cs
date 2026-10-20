using System.Diagnostics;
using Condor.Datos;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;
using Npgsql;
using Xunit.Abstractions;

namespace Condor.Pruebas.Aceptacion;

/// <summary>
/// HU-02: buscar producto por nombre al vender.
/// Criterio 1: con tres o más letras escritas en el campo Producto, el sistema
/// muestra en menos de un segundo los productos que coinciden, con su precio.
/// Criterio 2: al elegir un producto y una cantidad y presionar Agregar o
/// Enter, la línea se agrega al detalle y se recalculan la operación gravada,
/// el IGV y el total.
/// </summary>
[Collection(BaseDatos.Nombre)]
public class HU02_BuscarProductoTests
{
    /// <summary>Más productos de los que tendrá el local, para probar con margen.</summary>
    private const int ProductosDeCarga = 3000;

    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(1);

    private readonly BaseDatosFixture _bd;
    private readonly ITestOutputHelper _salida;

    public HU02_BuscarProductoTests(BaseDatosFixture bd, ITestOutputHelper salida)
    {
        _bd = bd;
        _salida = salida;
    }

    [FactConBD]
    public async Task Con_tres_letras_muestra_las_coincidencias_con_precio_en_menos_de_un_segundo()
    {
        var marca = DatosPrueba.NombreUnico("Hu02").Replace(" ", "");
        await CargarProductosAsync(marca);
        try
        {
            var vm = new MainViewModel(new ProductoRepositorio(_bd.Conexiones));
            var buscador = vm.Buscador!;   // con la espera real de 250 ms entre teclas
            var tiempos = new List<TimeSpan>();

            foreach (var texto in new[] { "cem", "fie", "esm", "tub", "cab", "pro", "sintetico", marca })
            {
                var reloj = Stopwatch.StartNew();
                buscador.Texto = texto;
                await buscador.UltimaBusqueda;
                reloj.Stop();
                tiempos.Add(reloj.Elapsed);

                Assert.All(buscador.Sugerencias, p => Assert.True(p.Precio > 0));
                buscador.Limpiar();
            }

            _salida.WriteLine($"Tiempos (incluye la espera de 250 ms): {string.Join(", ", tiempos.Select(t => $"{t.TotalMilliseconds:0} ms"))}");
            Assert.All(tiempos, t => Assert.True(t < Limite, $"La búsqueda tardó {t.TotalMilliseconds:0} ms."));
        }
        finally
        {
            await BorrarProductosAsync(marca);
        }
    }

    [FactConBD]
    public async Task La_consulta_responde_rapido_aun_con_miles_de_productos()
    {
        var marca = DatosPrueba.NombreUnico("Carga").Replace(" ", "");
        await CargarProductosAsync(marca);
        try
        {
            var repo = new ProductoRepositorio(_bd.Conexiones);
            await repo.BuscarPorNombreAsync("calentamiento");   // primera conexión del pool

            var reloj = Stopwatch.StartNew();
            var resultado = await repo.BuscarPorNombreAsync($"{marca} producto 12", BuscadorProductoViewModel.MaximoSugerencias);
            reloj.Stop();

            _salida.WriteLine($"Consulta sobre {ProductosDeCarga} productos: {reloj.Elapsed.TotalMilliseconds:0} ms");
            Assert.Equal(BuscadorProductoViewModel.MaximoSugerencias, resultado.Count);
            Assert.True(reloj.Elapsed < TimeSpan.FromMilliseconds(300), $"La consulta tardó {reloj.Elapsed.TotalMilliseconds:0} ms.");
        }
        finally
        {
            await BorrarProductosAsync(marca);
        }
    }

    [FactConBD]
    public async Task Elegir_producto_y_cantidad_y_Enter_agrega_la_linea_y_recalcula()
    {
        var repo = new ProductoRepositorio(_bd.Conexiones);
        var nombre = DatosPrueba.NombreUnico("Fierro corrugado 1/2 in x 9 m");
        await repo.RegistrarAsync(DatosPrueba.Nuevo(nombre, 38.00m, "VAR"));
        var vm = new MainViewModel(repo);
        vm.Buscador!.Espera = TimeSpan.Zero;

        vm.Buscador.Texto = nombre;
        await vm.Buscador.UltimaBusqueda;
        vm.Buscador.Elegir(vm.Buscador.Seleccionado);   // Enter en la lista
        vm.Venta.CantidadTexto = "8";
        Assert.True(vm.Venta.Agregar());                 // Enter en Cant. o botón Agregar

        var linea = Assert.Single(vm.Venta.Detalle);
        Assert.Equal(304.00m, linea.Importe);
        Assert.Equal(304.00m, vm.Venta.Total);
        Assert.Equal(257.63m, vm.Venta.OpGravada);
        Assert.Equal(46.37m, vm.Venta.Igv);
    }

    [Fact]
    public async Task Con_menos_de_tres_letras_no_se_consulta_la_base_de_datos()
    {
        var repo = new ProductoRepositorioEnMemoria(DatosPrueba.Catalogo());
        var buscador = new BuscadorProductoViewModel(repo) { Espera = TimeSpan.Zero };

        buscador.Texto = "ce";
        await buscador.UltimaBusqueda;

        Assert.Equal(0, repo.Busquedas);
    }

    private async Task CargarProductosAsync(string marca)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO producto (nombre, precio, unidad)
            SELECT @marca || ' producto ' || g || ' ' || left(md5(g::text), 8), (g % 500) + 0.50, 'UND'
            FROM generate_series(1, @n) AS g
            """, conexion);
        cmd.Parameters.AddWithValue("marca", marca);
        cmd.Parameters.AddWithValue("n", ProductosDeCarga);
        await cmd.ExecuteNonQueryAsync();
        await using var analizar = new NpgsqlCommand("ANALYZE producto", conexion);
        await analizar.ExecuteNonQueryAsync();
    }

    private async Task BorrarProductosAsync(string marca)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM producto WHERE nombre LIKE @m || ' producto %'", conexion);
        cmd.Parameters.AddWithValue("m", marca);
        await cmd.ExecuteNonQueryAsync();
    }
}
