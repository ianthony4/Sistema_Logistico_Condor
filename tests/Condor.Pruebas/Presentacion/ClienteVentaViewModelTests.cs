using Condor.Dominio.Contratos;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

public class ClienteVentaViewModelTests
{
    private static (ClienteVentaViewModel Vm, ClienteRepositorioEnMemoria Repo) Crear()
    {
        var repo = new ClienteRepositorioEnMemoria(DatosPrueba.Clientes());
        return (new ClienteVentaViewModel(repo), repo);
    }

    [Theory]
    [InlineData("45879632", "Juan Pérez Quispe")]
    [InlineData("20131312955", "Constructora Valle de Majes S.A.C.")]
    [InlineData(" 4587 9632 ", "Juan Pérez Quispe")]
    public async Task Buscar_un_documento_registrado_completa_el_cliente(string documento, string nombre)
    {
        var (vm, _) = Crear();
        vm.Documento = documento;

        await vm.BuscarCommand.EjecutarAsync();

        Assert.True(vm.HayCliente);
        Assert.Equal(nombre, vm.Seleccionado!.Nombre);
        Assert.NotNull(vm.Seleccionado.Direccion);
        Assert.False(vm.TieneError);
    }

    [Fact]
    public async Task Documento_valido_no_registrado_lo_indica_sin_error()
    {
        var (vm, _) = Crear();
        vm.Documento = "12345678";

        await vm.BuscarCommand.EjecutarAsync();

        Assert.False(vm.HayCliente);
        Assert.True(vm.NoEncontrado);
        Assert.False(vm.TieneError);
    }

    [Theory]
    [InlineData("20131312954", "dígito verificador")]
    [InlineData("1234", "8 dígitos")]
    [InlineData("1234567890", "11 dígitos")]
    public async Task Documento_invalido_marca_el_campo_y_no_consulta(string documento, string mensaje)
    {
        var (vm, repo) = Crear();
        vm.Documento = documento;

        await vm.BuscarCommand.EjecutarAsync();

        Assert.True(vm.TieneError);
        Assert.Contains(mensaje, vm.Error);
        Assert.Equal(0, repo.Busquedas);
        Assert.False(vm.HayCliente);
    }

    [Fact]
    public async Task Cambiar_el_numero_quita_el_cliente_elegido()
    {
        var (vm, _) = Crear();
        vm.Documento = "45879632";
        await vm.BuscarCommand.EjecutarAsync();

        vm.Documento = "4587963";

        Assert.False(vm.HayCliente);
    }

    [Fact]
    public async Task Quitar_deja_la_venta_sin_cliente()
    {
        var (vm, _) = Crear();
        vm.Documento = "45879632";
        await vm.BuscarCommand.EjecutarAsync();

        vm.QuitarCommand.Execute(null);

        Assert.False(vm.HayCliente);
        Assert.Equal(string.Empty, vm.Documento);
    }

    [Fact]
    public async Task Sin_conexion_avisa_y_conserva_lo_digitado()
    {
        var (vm, repo) = Crear();
        repo.Error = new SinConexionException(new TimeoutException());
        var avisado = false;
        vm.ConexionPerdida += () => avisado = true;
        vm.Documento = "45879632";

        await vm.BuscarCommand.EjecutarAsync();

        Assert.True(avisado);
        Assert.Contains("Sin conexión", vm.Error);
        Assert.Equal("45879632", vm.Documento);
    }

    [Fact]
    public void Buscar_esta_deshabilitado_con_el_campo_vacio()
    {
        var (vm, _) = Crear();

        Assert.False(vm.BuscarCommand.CanExecute(null));
        vm.Documento = "4";
        Assert.True(vm.BuscarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancelar_la_venta_tambien_quita_el_cliente()
    {
        var vm = new MainViewModel(new ProductoRepositorioEnMemoria(DatosPrueba.Catalogo()),
            new ClienteRepositorioEnMemoria(DatosPrueba.Clientes()));
        vm.Cliente!.Documento = "45879632";
        await vm.Cliente.BuscarCommand.EjecutarAsync();

        vm.CancelarVenta();

        Assert.False(vm.Cliente.HayCliente);
    }
}
