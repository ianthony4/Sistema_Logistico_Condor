using Condor.Dominio.Entidades;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

public class BuscadorProductoViewModelTests
{
    private static (BuscadorProductoViewModel vm, ProductoRepositorioEnMemoria repo) Crear()
    {
        var repo = new ProductoRepositorioEnMemoria(DatosPrueba.Catalogo());
        var vm = new BuscadorProductoViewModel(repo) { Espera = TimeSpan.Zero };
        return (vm, repo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("e")]
    [InlineData("es")]
    public async Task Con_menos_de_tres_letras_no_busca(string texto)
    {
        var (vm, repo) = Crear();

        vm.Texto = texto;
        await vm.UltimaBusqueda;

        Assert.Equal(0, repo.Busquedas);
        Assert.False(vm.MostrarSugerencias);
    }

    [Fact]
    public async Task Con_tres_letras_muestra_los_productos_que_coinciden()
    {
        var (vm, _) = Crear();

        vm.Texto = "esm";
        await vm.UltimaBusqueda;

        Assert.True(vm.MostrarSugerencias);
        Assert.Equal(2, vm.Sugerencias.Count);
        Assert.All(vm.Sugerencias, p => Assert.Contains("Esmalte", p.Nombre));
        Assert.Same(vm.Sugerencias[0], vm.Seleccionado);
    }

    [Fact]
    public async Task Sin_coincidencias_no_abre_la_lista()
    {
        var (vm, _) = Crear();

        vm.Texto = "xyz";
        await vm.UltimaBusqueda;

        Assert.False(vm.MostrarSugerencias);
        Assert.Empty(vm.Sugerencias);
    }

    [Fact]
    public async Task Elegir_notifica_el_producto_y_no_vuelve_a_buscar()
    {
        var (vm, repo) = Crear();
        Producto? elegido = null;
        vm.ProductoElegido += p => elegido = p;
        vm.Texto = "cem";
        await vm.UltimaBusqueda;
        var busquedas = repo.Busquedas;

        vm.Elegir(vm.Seleccionado);

        Assert.NotNull(elegido);
        Assert.Equal(elegido!.Nombre, vm.Texto);
        Assert.False(vm.MostrarSugerencias);
        Assert.Equal(busquedas, repo.Busquedas);
    }

    [Fact]
    public async Task Escribir_rapido_solo_consulta_el_ultimo_texto()
    {
        var repo = new ProductoRepositorioEnMemoria(DatosPrueba.Catalogo());
        var vm = new BuscadorProductoViewModel(repo) { Espera = TimeSpan.FromMilliseconds(200) };

        vm.Texto = "esm";
        vm.Texto = "esma";
        vm.Texto = "esmal";
        await vm.UltimaBusqueda;

        Assert.Equal(1, repo.Busquedas);
        Assert.Equal(2, vm.Sugerencias.Count);
    }

    [Fact]
    public async Task Limpiar_deja_el_campo_listo_para_el_siguiente_producto()
    {
        var (vm, _) = Crear();
        vm.Texto = "fie";
        await vm.UltimaBusqueda;

        vm.Limpiar();

        Assert.Equal(string.Empty, vm.Texto);
        Assert.Empty(vm.Sugerencias);
        Assert.Null(vm.Seleccionado);
    }

    [Fact]
    public async Task Si_falla_la_base_de_datos_muestra_el_error()
    {
        var vm = new BuscadorProductoViewModel(new RepositorioQueFalla()) { Espera = TimeSpan.Zero };

        vm.Texto = "cem";
        await vm.UltimaBusqueda;

        Assert.NotNull(vm.Error);
        Assert.False(vm.MostrarSugerencias);
    }

    private sealed class RepositorioQueFalla : Condor.Dominio.Contratos.IProductoRepositorio
    {
        public Task<Producto> RegistrarAsync(Producto producto, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Producto>> BuscarPorNombreAsync(string texto, int limite = 20, CancellationToken ct = default) =>
            throw new InvalidOperationException("sin conexión");

        public Task<IReadOnlyList<Producto>> ListarAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
