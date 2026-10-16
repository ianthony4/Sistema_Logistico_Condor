using Condor.Dominio.Contratos;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

public class EstadoConexionViewModelTests
{
    private static Exception Caida() => new SinConexionException(new TimeoutException());

    [Fact]
    public async Task Si_la_PC_principal_no_responde_muestra_el_aviso()
    {
        var estado = new EstadoConexionViewModel(_ => Task.FromResult(false));

        await estado.ActualizarAsync();

        Assert.False(estado.Conectado);
        Assert.Contains("Reintentar", estado.Aviso);
        Assert.Contains("se conserva", estado.Aviso);
    }

    [Fact]
    public async Task Reintentar_quita_el_aviso_cuando_vuelve_la_red()
    {
        var hayRed = false;
        var estado = new EstadoConexionViewModel(_ => Task.FromResult(hayRed));
        var recuperada = 0;
        estado.ConexionRecuperada += () => recuperada++;
        await estado.ActualizarAsync();

        hayRed = true;
        await estado.ReintentarCommand.EjecutarAsync();

        Assert.True(estado.Conectado);
        Assert.Equal(string.Empty, estado.Aviso);
        Assert.Equal(1, recuperada);
    }

    [Fact]
    public async Task Una_excepcion_al_probar_cuenta_como_sin_conexion()
    {
        var estado = new EstadoConexionViewModel(_ => throw new InvalidOperationException());

        Assert.False(await estado.ActualizarAsync());
        Assert.False(estado.Conectado);
    }

    [Fact]
    public async Task Al_guardar_un_producto_sin_red_el_dialogo_conserva_los_datos()
    {
        var repo = new ProductoRepositorioEnMemoria { ErrorAlRegistrar = Caida() };
        var vm = new MainViewModel(repo) { Conexion = new EstadoConexionViewModel(_ => Task.FromResult(true)) };
        var dialogo = vm.CrearNuevoProducto()!;
        dialogo.Nombre = "Brocha 4 in";
        dialogo.PrecioTexto = "12.00";
        var cerrado = false;
        dialogo.Cerrar += _ => cerrado = true;

        await dialogo.GuardarCommand.EjecutarAsync();

        Assert.False(cerrado);
        Assert.Equal("Brocha 4 in", dialogo.Nombre);
        Assert.Equal("12.00", dialogo.PrecioTexto);
        Assert.Contains("reintentar", dialogo.Error);
        Assert.False(vm.Conexion!.Conectado);

        // Vuelve la red: el mismo diálogo guarda sin volver a escribir nada.
        repo.ErrorAlRegistrar = null;
        await dialogo.GuardarCommand.EjecutarAsync();
        Assert.True(cerrado);
    }

    [Fact]
    public async Task Buscar_sin_red_marca_la_conexion_como_perdida()
    {
        var vm = new MainViewModel(new BuscadorQueSeCae()) { Conexion = new EstadoConexionViewModel(_ => Task.FromResult(true)) };
        vm.Buscador!.Espera = TimeSpan.Zero;

        vm.Buscador.Texto = "cem";
        await vm.Buscador.UltimaBusqueda;

        Assert.False(vm.Conexion!.Conectado);
        Assert.NotNull(vm.Buscador.Error);
    }

    private sealed class BuscadorQueSeCae : IProductoRepositorio
    {
        public Task<Condor.Dominio.Entidades.Producto> RegistrarAsync(Condor.Dominio.Entidades.Producto producto, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Condor.Dominio.Entidades.Producto>> BuscarPorNombreAsync(string texto, int limite = 20, CancellationToken ct = default) =>
            throw Caida();

        public Task<IReadOnlyList<Condor.Dominio.Entidades.Producto>> ListarAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
