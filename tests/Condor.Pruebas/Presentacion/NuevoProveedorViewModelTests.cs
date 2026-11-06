using Condor.Dominio.Contratos;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

public class NuevoProveedorViewModelTests
{
    [Fact]
    public void Guardar_esta_deshabilitado_sin_RUC_o_sin_razon_social()
    {
        var vm = new NuevoProveedorViewModel(new ProveedorRepositorioEnMemoria());
        Assert.False(vm.GuardarCommand.CanExecute(null));

        vm.Ruc = "20370146994";
        Assert.False(vm.GuardarCommand.CanExecute(null));

        vm.RazonSocial = "Corporación Aceros Arequipa S.A.";
        Assert.True(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Guardar_registra_el_proveedor_con_sus_datos_comerciales_y_cierra()
    {
        var repo = new ProveedorRepositorioEnMemoria();
        var vm = new NuevoProveedorViewModel(repo)
        {
            Ruc = "20370146994",
            RazonSocial = "  Corporación  Aceros Arequipa S.A. ",
            NombreComercial = "Aceros Arequipa",
            Telefono = "054 286314",
            Correo = "Ventas@AcerosArequipa.com",
            Contacto = "Carlos Huamán",
        };
        bool? cerrado = null;
        vm.Cerrar += g => cerrado = g;

        await vm.GuardarCommand.EjecutarAsync();

        Assert.True(cerrado);
        var guardado = Assert.Single(repo.Proveedores);
        Assert.Equal("Corporación Aceros Arequipa S.A.", guardado.RazonSocial);
        Assert.Equal("ventas@acerosarequipa.com", guardado.Correo);
        Assert.Null(guardado.Direccion);
        Assert.Same(guardado, vm.Guardado);
    }

    [Fact]
    public async Task Un_RUC_invalido_se_avisa_sin_cerrar_el_dialogo()
    {
        var repo = new ProveedorRepositorioEnMemoria();
        var vm = new NuevoProveedorViewModel(repo) { Ruc = "20370146995", RazonSocial = "Aceros" };
        bool? cerrado = null;
        vm.Cerrar += g => cerrado = g;

        await vm.GuardarCommand.EjecutarAsync();

        Assert.Null(cerrado);
        Assert.Contains("dígito verificador", vm.Error);
        Assert.Empty(repo.Proveedores);
    }

    [Fact]
    public async Task Un_RUC_ya_registrado_se_avisa_y_no_se_duplica()
    {
        var repo = new ProveedorRepositorioEnMemoria(new[] { DatosPrueba.ProveedorAceros() });
        var vm = new NuevoProveedorViewModel(repo) { Ruc = "20370146994", RazonSocial = "Otro nombre" };

        await vm.GuardarCommand.EjecutarAsync();

        Assert.Contains("Ya existe un proveedor con RUC 20370146994", vm.Error);
        Assert.Single(repo.Proveedores);
        Assert.Null(vm.Guardado);
    }

    [Fact]
    public async Task Sin_conexion_conserva_lo_escrito_y_avisa_la_perdida()
    {
        var repo = new ProveedorRepositorioEnMemoria { Error = new SinConexionException(new IOException()) };
        var vm = new NuevoProveedorViewModel(repo) { Ruc = "20370146994", RazonSocial = "Aceros Arequipa" };
        var avisos = 0;
        vm.ConexionPerdida += () => avisos++;

        await vm.GuardarCommand.EjecutarAsync();

        Assert.Equal(1, avisos);
        Assert.Contains("Sin conexión", vm.Error);
        Assert.Equal("20370146994", vm.Ruc);
        Assert.Equal("Aceros Arequipa", vm.RazonSocial);
    }

    [Fact]
    public async Task Corregir_un_campo_borra_el_error()
    {
        var vm = new NuevoProveedorViewModel(new ProveedorRepositorioEnMemoria()) { Ruc = "1", RazonSocial = "X" };
        await vm.GuardarCommand.EjecutarAsync();
        Assert.NotNull(vm.Error);

        vm.Ruc = "20370146994";

        Assert.Null(vm.Error);
    }

    [Theory]
    [InlineData("20370146994", "20370146994", "")]
    [InlineData("aceros", "", "aceros")]
    [InlineData("", "", "")]
    public void Proponer_usa_lo_buscado_como_RUC_o_como_razon_social(string texto, string ruc, string razon)
    {
        var vm = new NuevoProveedorViewModel(new ProveedorRepositorioEnMemoria());

        vm.Proponer(texto);

        Assert.Equal(ruc, vm.Ruc);
        Assert.Equal(razon, vm.RazonSocial);
    }

    [Fact]
    public void La_pantalla_principal_crea_el_dialogo_solo_si_hay_repositorio()
    {
        Assert.Null(new MainViewModel().CrearNuevoProveedor());
        Assert.NotNull(new MainViewModel(proveedores: new ProveedorRepositorioEnMemoria()).CrearNuevoProveedor());
    }
}
