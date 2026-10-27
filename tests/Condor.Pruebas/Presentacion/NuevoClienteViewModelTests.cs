using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

public class NuevoClienteViewModelTests
{
    [Fact]
    public void Guardar_esta_deshabilitado_sin_numero_o_sin_nombre()
    {
        var vm = new NuevoClienteViewModel(new ClienteRepositorioEnMemoria());
        Assert.False(vm.GuardarCommand.CanExecute(null));

        vm.NumeroDocumento = "45879632";
        Assert.False(vm.GuardarCommand.CanExecute(null));

        vm.Nombre = "Juan Pérez";
        Assert.True(vm.GuardarCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("45879632", TipoDocumento.Dni, "_Nombre completo")]
    [InlineData("20131312955", TipoDocumento.Ruc, "_Razón social")]
    [InlineData("123", TipoDocumento.Dni, "_Nombre completo")]
    public void Proponer_elige_DNI_o_RUC_segun_el_largo(string documento, TipoDocumento tipo, string etiqueta)
    {
        var vm = new NuevoClienteViewModel(new ClienteRepositorioEnMemoria());

        vm.Proponer(documento);

        Assert.Equal(tipo, vm.TipoDocumento);
        Assert.Equal(documento, vm.NumeroDocumento);
        Assert.Equal(etiqueta, vm.EtiquetaNombre);
    }

    [Fact]
    public async Task Guardar_registra_el_cliente_y_cierra_el_dialogo()
    {
        var repo = new ClienteRepositorioEnMemoria();
        var vm = new NuevoClienteViewModel(repo)
        {
            EsRuc = true,
            NumeroDocumento = "20131312955",
            Nombre = "  Constructora   Valle de Majes S.A.C. ",
            Direccion = "Av. Principal 450, Majes",
        };
        bool? cerrado = null;
        vm.Cerrar += g => cerrado = g;

        await vm.GuardarCommand.EjecutarAsync();

        Assert.True(cerrado);
        var guardado = Assert.Single(repo.Clientes);
        Assert.Equal(TipoDocumento.Ruc, guardado.TipoDocumento);
        Assert.Equal("Constructora Valle de Majes S.A.C.", guardado.Nombre);
        Assert.Same(guardado, vm.Guardado);
    }

    [Fact]
    public async Task RUC_con_digito_verificador_mal_no_se_guarda()
    {
        var repo = new ClienteRepositorioEnMemoria();
        var vm = new NuevoClienteViewModel(repo) { EsRuc = true, NumeroDocumento = "20131312954", Nombre = "Empresa" };
        var cerrado = false;
        vm.Cerrar += _ => cerrado = true;

        await vm.GuardarCommand.EjecutarAsync();

        Assert.False(cerrado);
        Assert.Contains("dígito verificador", vm.Error);
        Assert.Empty(repo.Clientes);
    }

    [Fact]
    public async Task Documento_ya_registrado_avisa_y_el_dialogo_sigue_abierto()
    {
        var repo = new ClienteRepositorioEnMemoria(DatosPrueba.Clientes());
        var vm = new NuevoClienteViewModel(repo) { NumeroDocumento = "45879632", Nombre = "Otro nombre" };
        var cerrado = false;
        vm.Cerrar += _ => cerrado = true;

        await vm.GuardarCommand.EjecutarAsync();

        Assert.False(cerrado);
        Assert.Contains("Ya existe", vm.Error);
    }

    [Fact]
    public async Task Sin_conexion_conserva_lo_escrito_para_reintentar()
    {
        var repo = new ClienteRepositorioEnMemoria { Error = new SinConexionException(new TimeoutException()) };
        var vm = new NuevoClienteViewModel(repo) { NumeroDocumento = "45879632", Nombre = "Juan Pérez" };
        var avisado = false;
        vm.ConexionPerdida += () => avisado = true;

        await vm.GuardarCommand.EjecutarAsync();

        Assert.True(avisado);
        Assert.Contains("reintentar", vm.Error);
        Assert.Equal("Juan Pérez", vm.Nombre);
    }

    [Fact]
    public async Task Desde_la_venta_propone_lo_digitado_y_al_guardar_queda_elegido()
    {
        var vm = new MainViewModel(new ProductoRepositorioEnMemoria(), new ClienteRepositorioEnMemoria());
        vm.Cliente!.Documento = "45879632";
        await vm.Cliente.BuscarCommand.EjecutarAsync();
        Assert.True(vm.Cliente.NoEncontrado);

        var dialogo = vm.CrearNuevoCliente()!;
        dialogo.Nombre = "Juan Pérez Quispe";
        await dialogo.GuardarCommand.EjecutarAsync();
        vm.ClienteRegistrado(dialogo.Guardado!);

        Assert.Equal("45879632", dialogo.NumeroDocumento);
        Assert.True(vm.Cliente.HayCliente);
        Assert.Equal("Juan Pérez Quispe", vm.Cliente.Seleccionado!.Nombre);
        Assert.False(vm.Cliente.NoEncontrado);
    }
}
