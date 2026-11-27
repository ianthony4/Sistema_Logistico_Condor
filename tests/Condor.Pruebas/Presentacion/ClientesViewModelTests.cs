using Condor.Dominio.Entidades;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

/// <summary>Ventana Clientes (F3, T-97).</summary>
public class ClientesViewModelTests
{
    private static (ClientesViewModel Vm, ClienteRepositorioEnMemoria Repo) Ventana()
    {
        var repo = new ClienteRepositorioEnMemoria(DatosPrueba.Clientes());
        return (new ClientesViewModel(repo), repo);
    }

    [Fact]
    public async Task Lista_todos_ordenados_por_nombre()
    {
        var (vm, _) = Ventana();

        await vm.BuscarAsync();

        Assert.Equal(new[] { "Constructora Valle de Majes S.A.C.", "Juan Pérez Quispe" }, vm.Clientes.Select(c => c.Nombre));
        Assert.Equal("2 clientes.", vm.Mensaje);
    }

    [Theory]
    [InlineData("4587", "Juan Pérez Quispe")]
    [InlineData("valle", "Constructora Valle de Majes S.A.C.")]
    public async Task Busca_por_documento_o_nombre(string texto, string nombre)
    {
        var (vm, _) = Ventana();
        vm.Texto = texto;

        await vm.BuscarAsync();

        Assert.Equal(nombre, Assert.Single(vm.Clientes).Nombre);
    }

    [Fact]
    public async Task Un_DNI_que_no_esta_se_propone_en_Nuevo_cliente_y_queda_elegido()
    {
        var (vm, _) = Ventana();
        vm.Texto = "41234567";
        await vm.BuscarAsync();
        var nuevo = vm.CrearNuevoCliente();
        nuevo.Nombre = "Rosa Mamani Ccori";

        await nuevo.GuardarCommand.EjecutarAsync();
        await vm.ClienteRegistradoAsync(nuevo.Guardado!);

        Assert.Equal("41234567", nuevo.Guardado!.NumeroDocumento);
        Assert.Equal("Rosa Mamani Ccori", vm.Seleccionado!.Nombre);
        Assert.Contains("Cliente registrado", vm.Mensaje);
    }

    [Fact]
    public async Task Usar_en_la_venta_deja_al_cliente_elegido_en_Nueva_venta()
    {
        var repo = new ClienteRepositorioEnMemoria(DatosPrueba.Clientes());
        var main = new MainViewModel(clientes: repo);
        main.IrA(Modulo.Clientes);
        var vm = (ClientesViewModel)main.ContenidoModulo!;
        vm.Texto = "20131312955";
        await vm.BuscarAsync();

        vm.UsarCommand.Execute(null);

        Assert.True(main.EnVenta); // vuelve sola a Nueva venta
        Assert.Equal("20131312955", main.Cliente!.Seleccionado!.NumeroDocumento);
        Assert.Contains("Cliente de la venta", main.Mensaje);
    }

    [Fact]
    public async Task Sin_conexion_avisa()
    {
        var (vm, repo) = Ventana();
        repo.Error = new Condor.Dominio.Contratos.SinConexionException(new TimeoutException());
        var avisos = 0;
        vm.ConexionPerdida += () => avisos++;

        await vm.BuscarAsync();

        Assert.Equal(1, avisos);
        Assert.Contains("Sin conexión", vm.Mensaje);
    }

    [Fact]
    public void Un_modulo_sin_configurar_avisa_que_revise_condor_json()
    {
        var main = new MainViewModel();

        main.ModuloPendienteCommand.Execute("Clientes");

        Assert.Null(main.CrearClientes());
        Assert.Contains("condor.json", main.Mensaje);
        Assert.DoesNotContain("sprint", main.Mensaje);
    }
}
