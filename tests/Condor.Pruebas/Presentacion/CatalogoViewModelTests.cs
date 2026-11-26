using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

/// <summary>Ventana Catálogo (F2) con el código de barras de cada producto (HU-13, T-88).</summary>
public class CatalogoViewModelTests
{
    private static (CatalogoViewModel Vm, ProductoRepositorioEnMemoria Repo) Ventana()
    {
        var catalogo = DatosPrueba.Catalogo().ToList();
        catalogo[0].CodigoBarras = "7751234500017";
        var repo = new ProductoRepositorioEnMemoria(catalogo);
        return (new CatalogoViewModel(repo, () => new NuevoProductoViewModel(repo)), repo);
    }

    [Fact]
    public async Task Lista_todo_el_catalogo_y_cuenta_los_que_no_tienen_codigo()
    {
        var (vm, _) = Ventana();

        await vm.BuscarAsync();

        Assert.Equal(5, vm.Productos.Count);
        Assert.Equal("5 productos, 4 sin código de barras.", vm.Mensaje);
    }

    [Fact]
    public async Task Busca_por_nombre_o_leyendo_el_codigo()
    {
        var (vm, _) = Ventana();

        vm.Texto = "esmalte";
        await vm.BuscarAsync();
        var porNombre = vm.Productos.Count;
        vm.Texto = "7751234500017";
        await vm.BuscarAsync();

        Assert.Equal(2, porNombre);
        Assert.Equal("Cemento Portland tipo I x 42.5 kg", Assert.Single(vm.Productos).Nombre);
    }

    [Fact]
    public async Task Asigna_al_producto_elegido_el_codigo_leido()
    {
        var (vm, repo) = Ventana();
        vm.Texto = "fierro";
        await vm.BuscarAsync();

        vm.Codigo = "7750000222333";
        await vm.AsignarCodigoCommand.EjecutarAsync();

        Assert.Null(vm.Error);
        Assert.Equal("7750000222333", vm.Seleccionado!.CodigoBarras);
        Assert.Equal("Fierro corrugado 1/2 in x 9 m", (await repo.BuscarPorCodigoBarrasAsync("7750000222333"))!.Nombre);
        Assert.Contains("ya se vende con el lector", vm.Mensaje);
    }

    [Fact]
    public async Task Un_codigo_de_otro_producto_no_se_asigna_y_dice_de_cual_es()
    {
        var (vm, _) = Ventana();
        vm.Texto = "clavo";
        await vm.BuscarAsync();

        vm.Codigo = "7751234500017";
        await vm.AsignarCodigoCommand.EjecutarAsync();

        Assert.Contains("Cemento Portland", vm.Error);
        Assert.Null(vm.Seleccionado!.CodigoBarras);
    }

    [Fact]
    public async Task Un_codigo_mal_leido_se_avisa_sin_guardar()
    {
        var (vm, _) = Ventana();
        vm.Texto = "clavo";
        await vm.BuscarAsync();

        vm.Codigo = "12";
        await vm.AsignarCodigoCommand.EjecutarAsync();

        Assert.Contains("entre 4 y 30", vm.Error);
    }

    [Fact]
    public async Task Quitar_el_codigo_lo_deja_sin_codigo()
    {
        var (vm, _) = Ventana();
        vm.Texto = "7751234500017";
        await vm.BuscarAsync();
        Assert.True(vm.QuitarCodigoCommand.CanExecute(null));

        await vm.QuitarCodigoCommand.EjecutarAsync();

        Assert.Null(vm.Seleccionado!.CodigoBarras);
        Assert.Equal(string.Empty, vm.Codigo);
        Assert.Contains("Se quitó", vm.Mensaje);
    }

    [Fact]
    public async Task Un_codigo_que_nadie_tiene_lo_propone_en_Nuevo_producto()
    {
        var (vm, _) = Ventana();
        vm.Texto = "7759999000013";
        await vm.BuscarAsync();

        Assert.Contains("Ningún producto tiene el código 7759999000013", vm.Mensaje);
        Assert.Equal("7759999000013", vm.CrearNuevoProducto().CodigoBarras);
    }

    [Fact]
    public async Task El_producto_registrado_queda_elegido()
    {
        var (vm, _) = Ventana();
        await vm.BuscarAsync();
        var nuevo = vm.CrearNuevoProducto();
        nuevo.Nombre = "Silicona transparente 280 ml";
        nuevo.PrecioTexto = "18.90";
        await nuevo.GuardarCommand.EjecutarAsync();

        await vm.ProductoRegistradoAsync(nuevo.Guardado!);

        Assert.Equal("Silicona transparente 280 ml", vm.Seleccionado!.Nombre);
        Assert.Contains("Producto registrado", vm.Mensaje);
    }

    [Fact]
    public void La_pantalla_principal_abre_el_catalogo_si_hay_productos()
    {
        Assert.Null(new MainViewModel().CrearCatalogo());
        Assert.NotNull(new MainViewModel(new ProductoRepositorioEnMemoria()).CrearCatalogo());
    }
}
