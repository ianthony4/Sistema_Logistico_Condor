using Condor.Dominio.Entidades;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

/// <summary>Lector de código de barras en la pantalla Nueva venta (HU-13, T-87).</summary>
public class EscaneoViewModelTests
{
    private const string CodigoCemento = "7751234500017";

    private static (MainViewModel Vm, ProductoRepositorioEnMemoria Productos) Crear()
    {
        var catalogo = DatosPrueba.Catalogo().ToList();
        catalogo[0].CodigoBarras = CodigoCemento;
        var productos = new ProductoRepositorioEnMemoria(catalogo);
        var vm = new MainViewModel(productos, new ClienteRepositorioEnMemoria(), new ComprobanteRepositorioEnMemoria());
        vm.Buscador!.Espera = TimeSpan.Zero;
        return (vm, productos);
    }

    [Fact]
    public async Task Un_codigo_del_catalogo_agrega_una_unidad_al_detalle()
    {
        var (vm, _) = Crear();
        vm.Buscador!.Texto = CodigoCemento;

        Assert.True(await vm.EscanearAsync(vm.Buscador.Texto));

        var linea = Assert.Single(vm.Venta.Detalle);
        Assert.Equal("Cemento Portland tipo I x 42.5 kg", linea.Descripcion);
        Assert.Equal(1, linea.Cantidad);
        Assert.Equal(32.50m, vm.Venta.Total);
        Assert.Equal(string.Empty, vm.Buscador.Texto); // listo para el siguiente
    }

    [Fact]
    public async Task Pasar_dos_veces_el_mismo_producto_suma_en_la_misma_linea()
    {
        var (vm, _) = Crear();

        await vm.EscanearAsync(CodigoCemento);
        await vm.EscanearAsync(CodigoCemento);

        Assert.Equal(2, Assert.Single(vm.Venta.Detalle).Cantidad);
    }

    [Fact]
    public async Task Un_codigo_que_no_esta_en_el_catalogo_avisa_y_Nuevo_producto_lo_propone()
    {
        var (vm, _) = Crear();
        vm.Buscador!.Texto = "7759999000013";

        Assert.True(await vm.EscanearAsync(vm.Buscador.Texto));
        await vm.Buscador.UltimaBusqueda;

        Assert.Empty(vm.Venta.Detalle);
        Assert.Contains("no está en el catálogo", vm.Mensaje);
        var nuevo = vm.CrearNuevoProducto()!;
        Assert.Equal("7759999000013", nuevo.CodigoBarras);
        Assert.Equal(string.Empty, nuevo.Nombre);
    }

    [Theory]
    [InlineData("cemento")]
    [InlineData("tubo 1/2")]
    public async Task Un_nombre_escrito_no_se_toma_como_codigo(string texto)
    {
        var (vm, _) = Crear();

        Assert.False(await vm.EscanearAsync(texto));
        Assert.Empty(vm.Venta.Detalle);
    }

    [Fact]
    public async Task Sin_conexion_avisa_y_no_agrega_nada()
    {
        var (vm, _) = Crear();
        var caido = new MainViewModel(new ProductoQueSeCae()) { Conexion = new EstadoConexionViewModel(_ => Task.FromResult(true)) };

        Assert.True(await caido.EscanearAsync(CodigoCemento));

        Assert.Empty(caido.Venta.Detalle);
        Assert.Contains("Sin conexión", caido.Mensaje);
        Assert.False(caido.Conexion.Conectado);
        Assert.Empty(vm.Venta.Detalle);
    }

    [Fact]
    public async Task Nuevo_producto_guarda_el_codigo_de_barras()
    {
        var (_, productos) = Crear();
        var vm = new NuevoProductoViewModel(productos)
        {
            Nombre = "Silicona transparente 280 ml",
            PrecioTexto = "18.90",
            CodigoBarras = "7750000111222",
        };

        await vm.GuardarCommand.EjecutarAsync();

        Assert.Null(vm.Error);
        Assert.Equal("7750000111222", vm.Guardado!.CodigoBarras);
    }

    [Fact]
    public async Task Nuevo_producto_con_un_codigo_ajeno_avisa_de_quien_es()
    {
        var (_, productos) = Crear();
        var vm = new NuevoProductoViewModel(productos) { Nombre = "Otro cemento", PrecioTexto = "30", CodigoBarras = CodigoCemento };

        await vm.GuardarCommand.EjecutarAsync();

        Assert.Null(vm.Guardado);
        Assert.Contains("Cemento Portland", vm.Error);
    }

    private sealed class ProductoQueSeCae : Condor.Dominio.Contratos.IProductoRepositorio
    {
        private static Exception Caida() => new Condor.Dominio.Contratos.SinConexionException(new TimeoutException());

        public Task<Producto> RegistrarAsync(Producto producto, CancellationToken ct = default) => throw Caida();

        public Task<IReadOnlyList<Producto>> BuscarPorNombreAsync(string texto, int limite = 20, CancellationToken ct = default) => throw Caida();

        public Task<IReadOnlyList<Producto>> ListarAsync(CancellationToken ct = default) => throw Caida();

        public Task<Producto?> BuscarPorCodigoBarrasAsync(string codigo, CancellationToken ct = default) => throw Caida();

        public Task<Producto> AsignarCodigoBarrasAsync(long productoId, string? codigo, CancellationToken ct = default) => throw Caida();
    }
}
