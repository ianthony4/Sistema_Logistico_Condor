using Condor.Dominio.Entidades;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

/// <summary>Los módulos del menú se muestran en el panel derecho de la ventana principal (T-73).</summary>
public class NavegacionModulosTests
{
    private static MainViewModel Crear()
    {
        var comprobantes = new ComprobanteRepositorioEnMemoria();
        var vm = new MainViewModel(new ProductoRepositorioEnMemoria(DatosPrueba.Catalogo()),
            new ClienteRepositorioEnMemoria(DatosPrueba.Clientes()), comprobantes, new ProveedorRepositorioEnMemoria())
        {
            Impresion = new ImpresionViewModel(new ImpresoraFalsa(), new EmpresaRepositorioEnMemoria(), comprobantes),
        };
        vm.ConfigurarRespaldo(new GeneradorRespaldoFalso(), new RespaldoRepositorioEnMemoria(), DatosPrueba.DiscoExterno);
        return vm;
    }

    [Fact]
    public void Al_abrir_se_ve_Nueva_venta()
    {
        var vm = Crear();

        Assert.Equal(Modulo.Venta, vm.ModuloActual);
        Assert.True(vm.EnVenta);
        Assert.Null(vm.ContenidoModulo);
    }

    [Theory]
    [InlineData(Modulo.Historial, typeof(ComprobantesRecientesViewModel))]
    [InlineData(Modulo.Proveedores, typeof(ProveedoresViewModel))]
    [InlineData(Modulo.Respaldo, typeof(RespaldoViewModel))]
    [InlineData(Modulo.Proformas, typeof(ProformasViewModel))]
    public void El_modulo_elegido_pasa_al_panel_derecho(Modulo modulo, Type tipo)
    {
        var vm = Crear();
        var cambios = 0;
        vm.ModuloCambiado += () => cambios++;

        Assert.True(vm.IrA(modulo));

        Assert.Equal(modulo, vm.ModuloActual);
        Assert.False(vm.EnVenta);
        Assert.IsType(tipo, vm.ContenidoModulo);
        Assert.Equal(1, cambios);
    }

    [Fact]
    public void El_boton_del_menu_usa_el_nombre_del_modulo()
    {
        var vm = Crear();

        vm.IrACommand.Execute("Proveedores");
        var proveedores = vm.ModuloActual;
        vm.IrACommand.Execute("Venta");

        Assert.Equal(Modulo.Proveedores, proveedores);
        Assert.True(vm.EnVenta);
        Assert.Null(vm.ContenidoModulo);
    }

    [Fact]
    public void Volver_a_la_venta_conserva_el_detalle_en_curso()
    {
        var vm = Crear();
        vm.Venta.ProductoPorAgregar = DatosPrueba.Cemento();
        Assert.True(vm.Venta.Agregar());

        vm.IrA(Modulo.Historial);
        vm.IrA(Modulo.Venta);

        Assert.Single(vm.Venta.Detalle);
    }

    [Fact]
    public async Task Convertir_en_venta_desde_el_modulo_Proformas_avisa_a_la_ventana()
    {
        var comprobantes = new ComprobanteRepositorioEnMemoria();
        var proforma = await comprobantes.EmitirAsync(DatosPrueba.ProformaObra());
        var vm = new MainViewModel(new ProductoRepositorioEnMemoria(), new ClienteRepositorioEnMemoria(), comprobantes);
        Comprobante? porConvertir = null;
        vm.ProformaPorConvertir += p => porConvertir = p;
        vm.IrA(Modulo.Proformas);
        var proformas = (ProformasViewModel)vm.ContenidoModulo!;
        await proformas.BuscarAsync();

        await proformas.ConvertirCommand.EjecutarAsync();
        vm.ConvertirProforma(porConvertir!);
        vm.IrA(Modulo.Venta);

        Assert.Same(proforma, porConvertir);
        Assert.True(vm.EnVenta);
        Assert.Equal(2, vm.Venta.Detalle.Count);
    }

    [Fact]
    public void Un_modulo_que_esta_PC_no_tiene_no_se_abre_y_se_avisa()
    {
        var vm = new MainViewModel(new ProductoRepositorioEnMemoria());

        Assert.False(vm.IrA(Modulo.Historial));

        Assert.True(vm.EnVenta);
        Assert.Contains("Historial", vm.Mensaje);
    }

    [Fact]
    public async Task Mientras_se_genera_el_respaldo_no_se_cambia_de_modulo()
    {
        var vm = Crear();
        var generador = new GeneradorRespaldoFalso { Demora = new TaskCompletionSource() };
        vm.ConfigurarRespaldo(generador, new RespaldoRepositorioEnMemoria(), DatosPrueba.DiscoExterno);
        vm.IniciarSesion(new Sesion(new Terminal { Id = 1, Nombre = "PC-PRINCIPAL" }));
        vm.IrA(Modulo.Respaldo);
        var respaldo = (RespaldoViewModel)vm.ContenidoModulo!;
        var generando = respaldo.GenerarCommand.EjecutarAsync();

        var salio = vm.IrA(Modulo.Venta);
        var aviso = vm.Mensaje;
        var enCurso = vm.RespaldoEnCurso;
        generador.Demora.SetResult();
        await generando;

        Assert.False(salio);
        Assert.True(enCurso);
        Assert.False(vm.RespaldoEnCurso);
        Assert.Equal(Modulo.Respaldo, vm.ModuloActual);
        Assert.Contains("Espere a que termine el respaldo", aviso);
        Assert.True(vm.IrA(Modulo.Venta));
    }

    [Theory]
    [InlineData(1, Modulo.Venta)]
    [InlineData(5, Modulo.Historial)]
    [InlineData(9, Modulo.Respaldo)]
    public void Cada_tecla_F_corresponde_a_un_modulo(int tecla, Modulo modulo)
    {
        Assert.Equal(modulo, ModuloMenu.DeTecla(tecla));
        Assert.Null(ModuloMenu.DeTecla(10));
    }
}
