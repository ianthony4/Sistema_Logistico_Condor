using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

public class NuevoProductoViewModelTests
{
    [Theory]
    [InlineData("58.00", 58.00)]
    [InlineData("58,50", 58.50)]
    [InlineData(" 7.5 ", 7.5)]
    public void Acepta_el_precio_con_punto_o_coma(string texto, decimal esperado)
    {
        Assert.True(NuevoProductoViewModel.IntentarLeerPrecio(texto, out var precio));
        Assert.Equal(esperado, precio);
    }

    [Fact]
    public void Guardar_esta_deshabilitado_sin_nombre_o_sin_precio()
    {
        var vm = new NuevoProductoViewModel(new ProductoRepositorioEnMemoria());
        Assert.False(vm.GuardarCommand.CanExecute(null));

        vm.Nombre = "Esmalte sintético rojo 1 gal";
        Assert.False(vm.GuardarCommand.CanExecute(null));

        vm.PrecioTexto = "58.00";
        Assert.True(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Guardar_registra_el_producto_y_cierra_el_dialogo()
    {
        var repo = new ProductoRepositorioEnMemoria();
        var vm = new NuevoProductoViewModel(repo)
        {
            Nombre = "  Esmalte sintético rojo 1 gal ",
            PrecioTexto = "58.00",
            Unidad = "GAL",
        };
        bool? cerrado = null;
        vm.Cerrar += g => cerrado = g;

        await vm.GuardarCommand.EjecutarAsync();

        Assert.True(cerrado);
        var guardado = Assert.Single(repo.Productos);
        Assert.Equal("Esmalte sintético rojo 1 gal", guardado.Nombre);
        Assert.Equal("GAL", guardado.Unidad);
        Assert.Same(guardado, vm.Guardado);
    }

    [Fact]
    public async Task Precio_no_numerico_muestra_error_y_no_guarda()
    {
        var repo = new ProductoRepositorioEnMemoria();
        var vm = new NuevoProductoViewModel(repo) { Nombre = "Martillo", PrecioTexto = "veinte" };

        await vm.GuardarCommand.EjecutarAsync();

        Assert.NotNull(vm.Error);
        Assert.Empty(repo.Productos);
    }

    [Fact]
    public async Task Error_del_repositorio_se_muestra_y_el_dialogo_sigue_abierto()
    {
        var repo = new ProductoRepositorioEnMemoria { ErrorAlRegistrar = new InvalidOperationException("Ya existe") };
        var vm = new NuevoProductoViewModel(repo) { Nombre = "Martillo", PrecioTexto = "25" };
        var cerrado = false;
        vm.Cerrar += _ => cerrado = true;

        await vm.GuardarCommand.EjecutarAsync();

        Assert.Equal("Ya existe", vm.Error);
        Assert.False(cerrado);
    }
}
