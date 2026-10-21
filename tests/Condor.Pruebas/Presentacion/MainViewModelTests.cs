using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

public class MainViewModelTests
{
    private static MainViewModel Crear(ProductoRepositorioEnMemoria? repo = null)
    {
        var vm = new MainViewModel(repo ?? new ProductoRepositorioEnMemoria(DatosPrueba.Catalogo()));
        vm.Buscador!.Espera = TimeSpan.Zero;
        return vm;
    }

    [Fact]
    public async Task Nuevo_producto_propone_lo_escrito_si_no_hubo_coincidencias()
    {
        var vm = Crear();
        vm.Buscador!.Texto = "Brocha de 4 pulgadas";
        await vm.Buscador.UltimaBusqueda;

        var dialogo = vm.CrearNuevoProducto();

        Assert.Equal("Brocha de 4 pulgadas", dialogo!.Nombre);
    }

    [Fact]
    public void Nuevo_producto_empieza_vacio_si_no_se_escribio_nada()
    {
        Assert.Equal(string.Empty, Crear().CrearNuevoProducto()!.Nombre);
    }

    [Fact]
    public void Sin_catalogo_no_hay_dialogo_ni_buscador()
    {
        var vm = new MainViewModel();

        Assert.Null(vm.Buscador);
        Assert.Null(vm.CrearNuevoProducto());
    }

    [Fact]
    public async Task Al_registrar_el_producto_queda_elegido_para_vender()
    {
        var repo = new ProductoRepositorioEnMemoria();
        var vm = Crear(repo);
        var dialogo = vm.CrearNuevoProducto()!;
        dialogo.Nombre = "Brocha 4 in";
        dialogo.PrecioTexto = "12.00";
        await dialogo.GuardarCommand.EjecutarAsync();

        vm.ProductoRegistrado(dialogo.Guardado!);

        Assert.Equal("Brocha 4 in", vm.Buscador!.Texto);
        Assert.Contains("tres PC", vm.Mensaje);
    }

    [Fact]
    public async Task El_aviso_de_otra_PC_actualiza_la_lista_abierta()
    {
        var repo = new ProductoRepositorioEnMemoria(DatosPrueba.Catalogo());
        var vm = Crear(repo);
        vm.Buscador!.Texto = "esmalte";
        await vm.Buscador.UltimaBusqueda;
        Assert.Equal(2, vm.Buscador.Sugerencias.Count);

        // Otra PC registra un producto que coincide.
        await repo.RegistrarAsync(DatosPrueba.Nuevo("Esmalte sintético azul 1 gal", 58m, "GAL"));
        vm.AlCambiarCatalogo();
        await vm.Buscador.UltimaBusqueda;

        Assert.Equal(3, vm.Buscador.Sugerencias.Count);
    }
}
