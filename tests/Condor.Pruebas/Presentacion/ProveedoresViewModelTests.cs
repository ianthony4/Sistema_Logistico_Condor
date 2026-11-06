using Condor.Dominio.Contratos;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

public class ProveedoresViewModelTests
{
    [Fact]
    public async Task Al_abrir_lista_todos_los_proveedores_ordenados()
    {
        var vm = new ProveedoresViewModel(new ProveedorRepositorioEnMemoria(DatosPrueba.Proveedores()));

        await vm.BuscarAsync();

        Assert.Equal(3, vm.Proveedores.Count);
        Assert.Equal("Corporación Aceros Arequipa S.A.", vm.Proveedores[0].RazonSocial);
        Assert.Same(vm.Proveedores[0], vm.Seleccionado);
        Assert.Equal("3 proveedores.", vm.Mensaje);
    }

    [Theory]
    [InlineData("yura", "Yura S.A.")]
    [InlineData("20100079", "Corporación Peruana de Productos Químicos S.A.")]
    [InlineData("cppq", "Corporación Peruana de Productos Químicos S.A.")]
    public async Task Busca_por_RUC_razon_social_o_nombre_comercial(string texto, string esperado)
    {
        var vm = new ProveedoresViewModel(new ProveedorRepositorioEnMemoria(DatosPrueba.Proveedores())) { Texto = texto };

        await vm.BuscarCommand.EjecutarAsync();

        Assert.Equal(esperado, Assert.Single(vm.Proveedores).RazonSocial);
        Assert.Equal("1 proveedor.", vm.Mensaje);
    }

    [Fact]
    public async Task Sin_resultados_invita_a_registrarlo_y_propone_lo_buscado()
    {
        var vm = new ProveedoresViewModel(new ProveedorRepositorioEnMemoria(DatosPrueba.Proveedores())) { Texto = "20131312955" };

        await vm.BuscarAsync();
        var nuevo = vm.CrearNuevoProveedor();

        Assert.Empty(vm.Proveedores);
        Assert.Contains("+ Nuevo proveedor", vm.Mensaje);
        Assert.Equal("20131312955", nuevo.Ruc);
    }

    [Fact]
    public async Task El_proveedor_registrado_aparece_en_la_lista_seleccionado()
    {
        var repo = new ProveedorRepositorioEnMemoria(DatosPrueba.Proveedores());
        var vm = new ProveedoresViewModel(repo) { Texto = "pinturas del sur" };
        await vm.BuscarAsync();

        var dialogo = vm.CrearNuevoProveedor();
        dialogo.Ruc = "20131312955";
        await dialogo.GuardarCommand.EjecutarAsync();
        await vm.ProveedorRegistradoAsync(dialogo.Guardado!);

        Assert.Equal(4, vm.Proveedores.Count);
        Assert.Equal("20131312955", vm.Seleccionado!.Ruc);
        Assert.Equal("pinturas del sur", vm.Seleccionado.RazonSocial);
        Assert.Contains("tres PC", vm.Mensaje);
        Assert.Equal(string.Empty, vm.Texto);
    }

    [Fact]
    public async Task Sin_conexion_avisa_y_no_borra_la_lista()
    {
        var repo = new ProveedorRepositorioEnMemoria(DatosPrueba.Proveedores());
        var vm = new ProveedoresViewModel(repo);
        await vm.BuscarAsync();
        var avisos = 0;
        vm.ConexionPerdida += () => avisos++;
        repo.Error = new SinConexionException(new IOException());

        await vm.BuscarAsync();

        Assert.Equal(1, avisos);
        Assert.Contains("Sin conexión", vm.Mensaje);
        Assert.Equal(3, vm.Proveedores.Count);
    }

    [Fact]
    public void La_pantalla_principal_ofrece_Proveedores_solo_si_hay_repositorio()
    {
        Assert.Null(new MainViewModel().CrearProveedores());
        Assert.NotNull(new MainViewModel(proveedores: new ProveedorRepositorioEnMemoria()).CrearProveedores());
    }
}
