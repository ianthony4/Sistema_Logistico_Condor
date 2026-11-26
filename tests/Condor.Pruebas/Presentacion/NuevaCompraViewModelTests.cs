using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

public class NuevaCompraViewModelTests
{
    private static readonly DateOnly Hoy = DatosPrueba.Hoy();

    private sealed record Dialogo(NuevaCompraViewModel Vm, CompraRepositorioEnMemoria Compras, ProveedorRepositorioEnMemoria Proveedores);

    private static Dialogo Crear(bool registrada = true)
    {
        var compras = new CompraRepositorioEnMemoria();
        var proveedores = new ProveedorRepositorioEnMemoria(DatosPrueba.Proveedores());
        var sesion = new Sesion(new Terminal { Id = registrada ? 1 : 0, Nombre = "PC-PRINCIPAL" });
        var vm = new NuevaCompraViewModel(compras, proveedores, new ProductoRepositorioEnMemoria(DatosPrueba.Catalogo()),
            () => sesion, () => Hoy);
        vm.Buscador.Espera = TimeSpan.Zero;
        return new Dialogo(vm, compras, proveedores);
    }

    private static async Task AgregarAsync(NuevaCompraViewModel vm, string producto, string cantidad, string costo)
    {
        vm.Buscador.Texto = producto;
        await vm.Buscador.UltimaBusqueda;
        vm.Buscador.Elegir(vm.Buscador.Seleccionado);
        vm.CantidadTexto = cantidad;
        vm.CostoTexto = costo;
        Assert.True(vm.Agregar());
    }

    /// <summary>Factura F001-12345 de Aceros Arequipa con 50 varillas a S/ 31.50.</summary>
    private static async Task LlenarFacturaAcerosAsync(NuevaCompraViewModel vm)
    {
        vm.DocumentoProveedor = "20370146994";
        await vm.BuscarProveedorCommand.EjecutarAsync();
        vm.Serie = "f001";
        vm.NumeroTexto = "12345";
        vm.Fecha = Hoy.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        await AgregarAsync(vm, "fierro", "50", "31.50");
    }

    [Fact]
    public async Task Guarda_la_factura_del_proveedor_con_su_detalle_y_totales()
    {
        var (vm, compras, _) = Crear();
        var cerrado = (bool?)null;
        vm.Cerrar += g => cerrado = g;
        await LlenarFacturaAcerosAsync(vm);
        await AgregarAsync(vm, "clavo", "10", "6.20");
        vm.Observacion = "Pedido de la obra Pedregal";

        await vm.GuardarCommand.EjecutarAsync();

        var compra = Assert.Single(compras.Compras);
        Assert.Same(compra, vm.Guardado);
        Assert.True(cerrado);
        Assert.Null(vm.Error);
        Assert.Equal("Factura F001-00012345", compra.DocumentoCompleto);
        Assert.Equal("Corporación Aceros Arequipa S.A.", compra.Proveedor!.RazonSocial);
        Assert.Equal(Hoy.AddDays(-1), compra.FechaEmision);
        Assert.Equal(1, compra.TerminalId);
        Assert.Equal(new[] { 1575.00m, 62.00m }, compra.Detalle.Select(l => l.Importe));
        Assert.Equal(1637.00m, compra.Total);
        Assert.Equal("Pedido de la obra Pedregal", compra.Observacion);
    }

    [Fact]
    public async Task Los_totales_se_recalculan_al_agregar_y_quitar()
    {
        var (vm, _, _) = Crear();
        await AgregarAsync(vm, "fierro", "50", "31.50");
        await AgregarAsync(vm, "clavo", "10", "6.20");

        Assert.Equal(1637.00m, vm.Total);
        Assert.Equal(vm.Total, vm.OpGravada + vm.Igv);
        Assert.Equal(new[] { 1, 2 }, vm.Lineas.Select(l => l.Item));

        vm.QuitarCommand.Execute(vm.Lineas[0]);

        Assert.Equal(62.00m, vm.Total);
        Assert.Equal(1, vm.Lineas[0].Item);
    }

    [Fact]
    public async Task Busca_al_proveedor_por_parte_de_la_razon_social_y_muestra_su_RUC()
    {
        var (vm, _, _) = Crear();
        vm.DocumentoProveedor = "yura";

        await vm.BuscarProveedorCommand.EjecutarAsync();

        Assert.Equal("Yura S.A.", vm.Proveedor!.RazonSocial);
        Assert.Equal("20100137390", vm.DocumentoProveedor);
    }

    [Fact]
    public async Task Un_RUC_no_registrado_pide_registrarlo_en_Proveedores()
    {
        var (vm, _, _) = Crear();
        vm.DocumentoProveedor = "20131312955";

        await vm.BuscarProveedorCommand.EjecutarAsync();

        Assert.Null(vm.Proveedor);
        Assert.Contains("Proveedores (F6)", vm.Error);
    }

    [Fact]
    public async Task Si_varios_proveedores_coinciden_pide_el_RUC()
    {
        var (vm, _, _) = Crear();
        vm.DocumentoProveedor = "Corporación";

        await vm.BuscarProveedorCommand.EjecutarAsync();

        Assert.Null(vm.Proveedor);
        Assert.Contains("Hay 2 proveedores", vm.Error);
    }

    [Fact]
    public async Task Cambiar_el_RUC_despues_de_buscar_suelta_al_proveedor()
    {
        var (vm, _, _) = Crear();
        vm.DocumentoProveedor = "20370146994";
        await vm.BuscarProveedorCommand.EjecutarAsync();

        vm.DocumentoProveedor = "2037014699";

        Assert.Null(vm.Proveedor);
    }

    [Fact]
    public async Task Sin_proveedor_no_guarda_y_dice_por_que()
    {
        var (vm, compras, _) = Crear();
        vm.Serie = "F001";
        vm.NumeroTexto = "7";
        await AgregarAsync(vm, "fierro", "1", "31.50");

        await vm.GuardarCommand.EjecutarAsync();

        Assert.Empty(compras.Compras);
        Assert.Contains("proveedor", vm.Error);
    }

    [Fact]
    public async Task Sin_serie_ni_numero_o_con_fecha_futura_no_guarda()
    {
        var (vm, compras, _) = Crear();
        await LlenarFacturaAcerosAsync(vm);
        vm.Serie = "";
        vm.NumeroTexto = "abc";
        vm.Fecha = Hoy.AddDays(2).ToDateTime(TimeOnly.MinValue);

        await vm.GuardarCommand.EjecutarAsync();

        Assert.Empty(compras.Compras);
        Assert.Contains("serie", vm.Error);
        Assert.Contains("número", vm.Error);
        Assert.Contains("posterior a hoy", vm.Error);
    }

    [Fact]
    public void Sin_productos_Guardar_esta_deshabilitado()
    {
        var (vm, _, _) = Crear();

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Sin_costo_no_agrega_el_producto()
    {
        var (vm, _, _) = Crear();
        vm.Buscador.Texto = "cemento";
        await vm.Buscador.UltimaBusqueda;
        vm.Buscador.Elegir(vm.Buscador.Seleccionado);
        vm.CostoTexto = "";

        Assert.False(vm.Agregar());
        Assert.Contains("costo unitario", vm.Error);
        Assert.Empty(vm.Lineas);
        Assert.NotNull(vm.ProductoPorAgregar);
    }

    [Fact]
    public async Task El_mismo_comprobante_del_proveedor_no_se_registra_dos_veces()
    {
        var (vm, compras, proveedores) = Crear();
        var aceros = proveedores.Proveedores.Single(p => p.Ruc == "20370146994");
        await compras.RegistrarAsync(DatosPrueba.CompraAceros(aceros, numero: 12345));
        await LlenarFacturaAcerosAsync(vm);

        await vm.GuardarCommand.EjecutarAsync();

        Assert.Single(compras.Compras);
        Assert.Null(vm.Guardado);
        Assert.Contains("ya está registrada", vm.Error);
    }

    [Fact]
    public async Task Sin_conexion_conserva_lo_escrito_para_reintentar()
    {
        var (vm, compras, _) = Crear();
        await LlenarFacturaAcerosAsync(vm);
        compras.Error = new SinConexionException(new TimeoutException());
        var avisos = 0;
        vm.ConexionPerdida += () => avisos++;

        await vm.GuardarCommand.EjecutarAsync();

        Assert.Equal(1, avisos);
        Assert.Contains("NO se guardó", vm.Error);
        Assert.Single(vm.Lineas);
        Assert.NotNull(vm.Proveedor);
    }

    [Fact]
    public async Task Si_la_PC_aun_no_se_registro_no_guarda()
    {
        var (vm, compras, _) = Crear(registrada: false);
        await LlenarFacturaAcerosAsync(vm);

        await vm.GuardarCommand.EjecutarAsync();

        Assert.Empty(compras.Compras);
        Assert.Contains("no se registró", vm.Error);
    }

    [Fact]
    public async Task Tras_guardar_la_ventana_Compras_la_muestra_seleccionada_y_suma_los_egresos()
    {
        var compras = new CompraRepositorioEnMemoria(new[] { DatosPrueba.CompraCemento(fecha: Hoy) });
        var proveedores = new ProveedorRepositorioEnMemoria(DatosPrueba.Proveedores());
        var productos = new ProductoRepositorioEnMemoria(DatosPrueba.Catalogo());
        var sesion = new Sesion(new Terminal { Id = 1, Nombre = "PC-PRINCIPAL" });
        var ventana = new ComprasViewModel(compras, () => Hoy,
            () => new NuevaCompraViewModel(compras, proveedores, productos, () => sesion, () => Hoy) { Buscador = { Espera = TimeSpan.Zero } });
        await ventana.BuscarAsync();
        Assert.True(ventana.PuedeRegistrar);

        var dialogo = ventana.CrearNuevaCompra()!;
        await LlenarFacturaAcerosAsync(dialogo);
        await dialogo.GuardarCommand.EjecutarAsync();
        await ventana.CompraRegistradaAsync(dialogo.Guardado!);

        Assert.Same(dialogo.Guardado, ventana.Seleccionada);
        Assert.Equal(2, ventana.Compras.Count);
        Assert.Equal(1112.00m + 1575.00m, ventana.Total);
        Assert.Contains("Compra registrada: Factura F001-00012345", ventana.Mensaje);
    }

    [Fact]
    public void La_pantalla_principal_ofrece_Nueva_compra_solo_con_proveedores_y_catalogo()
    {
        var conTodo = new MainViewModel(new ProductoRepositorioEnMemoria(), proveedores: new ProveedorRepositorioEnMemoria(),
            compras: new CompraRepositorioEnMemoria()).CrearCompras()!;
        var sinProveedores = new MainViewModel(compras: new CompraRepositorioEnMemoria()).CrearCompras()!;

        Assert.True(conTodo.PuedeRegistrar);
        Assert.NotNull(conTodo.CrearNuevaCompra());
        Assert.False(sinProveedores.PuedeRegistrar);
        Assert.Null(sinProveedores.CrearNuevaCompra());
    }
}
