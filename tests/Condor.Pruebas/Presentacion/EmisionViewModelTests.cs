using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Ventas;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

public class EmisionViewModelTests
{
    private sealed record Pantalla(MainViewModel Vm, ComprobanteRepositorioEnMemoria Comprobantes);

    /// <summary>Pantalla Nueva venta con catálogo, clientes y la PC ya registrada.</summary>
    private static Pantalla Crear(bool registrada = true)
    {
        var comprobantes = new ComprobanteRepositorioEnMemoria();
        var vm = new MainViewModel(new ProductoRepositorioEnMemoria(DatosPrueba.Catalogo()),
            new ClienteRepositorioEnMemoria(DatosPrueba.Clientes()), comprobantes);
        vm.Buscador!.Espera = TimeSpan.Zero;
        vm.IniciarSesion(new Sesion(new Terminal { Id = registrada ? 1 : 0, Nombre = "PC-1" }));
        return new Pantalla(vm, comprobantes);
    }

    private static async Task AgregarAsync(MainViewModel vm, string texto, string cantidad)
    {
        vm.Buscador!.Texto = texto;
        await vm.Buscador.UltimaBusqueda;
        vm.Buscador.Elegir(vm.Buscador.Seleccionado);
        vm.Venta.CantidadTexto = cantidad;
        Assert.True(vm.Venta.Agregar());
    }

    [Fact]
    public async Task Emitir_guarda_la_boleta_y_deja_la_pantalla_lista_para_otra_venta()
    {
        var (vm, comprobantes) = Crear();
        await AgregarAsync(vm, "cemento", "2");
        await AgregarAsync(vm, "clavo", "1");
        Comprobante? emitido = null;
        vm.Emision!.Emitido += c => emitido = c;

        await vm.Emision.EmitirCommand.EjecutarAsync();

        var boleta = Assert.Single(comprobantes.Comprobantes);
        Assert.Same(boleta, emitido);
        Assert.Equal("B001-00000001", boleta.NumeroCompleto);
        Assert.Equal(72.50m, boleta.Total);
        Assert.Equal(1, boleta.TerminalId);
        Assert.Equal("PC-1", boleta.TerminalNombre);
        Assert.Equal(2, boleta.Detalle.Count);
        Assert.Empty(vm.Venta.Detalle);
        Assert.Contains("B001-00000001", vm.Mensaje);
        Assert.Null(vm.Emision.Error);
    }

    [Fact]
    public void Sin_productos_Emitir_esta_deshabilitado()
    {
        var (vm, _) = Crear();

        Assert.False(vm.Emision!.EmitirCommand.CanExecute(null));
    }

    [Fact]
    public async Task Boleta_con_DNI_sin_cliente_senala_el_campo_y_no_emite()
    {
        var (vm, comprobantes) = Crear();
        await AgregarAsync(vm, "fierro", "1");
        vm.Emision!.EsBoletaConDni = true;

        await vm.Emision.EmitirCommand.EjecutarAsync();

        Assert.Empty(comprobantes.Comprobantes);
        Assert.True(vm.Cliente!.TieneError);
        Assert.Contains("DNI", vm.Emision.Error);
        Assert.Single(vm.Venta.Detalle); // la venta se conserva
    }

    [Fact]
    public async Task Boleta_con_DNI_lleva_al_cliente_buscado()
    {
        var (vm, comprobantes) = Crear();
        await AgregarAsync(vm, "fierro", "1");
        vm.Cliente!.Documento = "45879632";
        await vm.Cliente.BuscarCommand.EjecutarAsync();
        vm.Emision!.EsBoletaConDni = true;

        await vm.Emision.EmitirCommand.EjecutarAsync();

        var boleta = Assert.Single(comprobantes.Comprobantes);
        Assert.Equal(vm.Emision.Ultimo, boleta);
        Assert.Equal("45879632", boleta.Cliente!.NumeroDocumento);
        Assert.False(vm.Cliente.HayCliente); // la siguiente venta empieza sin cliente
        Assert.True(vm.Emision.EsBoleta);
    }

    [Fact]
    public async Task Sin_conexion_no_emite_y_conserva_la_venta()
    {
        var (vm, comprobantes) = Crear();
        await AgregarAsync(vm, "esmalte", "2");
        comprobantes.ErrorAlEmitir = new SinConexionException(new TimeoutException());
        vm.Conexion = new EstadoConexionViewModel(_ => Task.FromResult(true));

        await vm.Emision!.EmitirCommand.EjecutarAsync();

        Assert.Contains("NO se emitió", vm.Emision.Error);
        Assert.Single(vm.Venta.Detalle);
        Assert.False(vm.Conexion.Conectado);
    }

    [Fact]
    public async Task Si_la_PC_aun_no_se_registro_no_emite()
    {
        var (vm, comprobantes) = Crear(registrada: false);
        await AgregarAsync(vm, "clavo", "1");

        await vm.Emision!.EmitirCommand.EjecutarAsync();

        Assert.Empty(comprobantes.Comprobantes);
        Assert.Contains("no se registró", vm.Emision.Error);
    }

    [Fact]
    public async Task Las_boletas_seguidas_llevan_numeros_seguidos()
    {
        var (vm, comprobantes) = Crear();
        for (var i = 0; i < 3; i++)
        {
            await AgregarAsync(vm, "clavo", "1");
            await vm.Emision!.EmitirCommand.EjecutarAsync();
        }

        Assert.Equal(new long[] { 1, 2, 3 }, comprobantes.Comprobantes.Select(c => c.Numero));
    }

    [Fact]
    public void Elegir_con_DNI_cambia_el_tipo()
    {
        var (vm, _) = Crear();

        vm.Emision!.EsBoletaConDni = true;

        Assert.Equal(TipoEmision.BoletaConDni, vm.Emision.Tipo);
        Assert.False(vm.Emision.EsBoleta);
    }

    [Fact]
    public async Task Factura_con_el_RUC_buscado_sale_con_la_serie_F001()
    {
        var (vm, comprobantes) = Crear();
        await AgregarAsync(vm, "cemento", "20");
        vm.Emision!.EsFactura = true;
        vm.Cliente!.Documento = "20131312955";
        await vm.Cliente.BuscarCommand.EjecutarAsync();

        await vm.Emision.EmitirCommand.EjecutarAsync();

        var factura = Assert.Single(comprobantes.Comprobantes);
        Assert.Equal("F001-00000001", factura.NumeroCompleto);
        Assert.Equal(TipoComprobante.Factura, factura.TipoComprobante);
        Assert.Equal("Constructora Valle de Majes S.A.C.", factura.Cliente!.Nombre);
        Assert.Contains("Factura electrónica F001-00000001 emitida", vm.Mensaje);
        // La venta siguiente vuelve a ser boleta y el campo vuelve a aceptar DNI o RUC.
        Assert.True(vm.Emision.EsBoleta);
        Assert.False(vm.Cliente.ExigeRuc);
    }

    [Fact]
    public async Task Factura_sin_cliente_no_se_emite_y_pide_el_RUC()
    {
        var (vm, comprobantes) = Crear();
        await AgregarAsync(vm, "fierro", "4");
        var pedidos = 0;
        vm.Emision!.RucRequerido += () => pedidos++;

        vm.Emision.EsFactura = true;
        await vm.Emision.EmitirCommand.EjecutarAsync();

        Assert.Empty(comprobantes.Comprobantes);
        Assert.Equal(2, pedidos); // al elegir Factura y al intentar emitir
        Assert.Contains("RUC", vm.Emision.Error);
        Assert.True(vm.Cliente!.TieneError);
        Assert.Equal("_RUC del cliente (factura)", vm.Cliente.EtiquetaDocumento);
        Assert.Single(vm.Venta.Detalle); // la venta se conserva
    }

    [Fact]
    public async Task Factura_con_un_cliente_de_DNI_pide_el_RUC_y_no_se_emite()
    {
        var (vm, comprobantes) = Crear();
        await AgregarAsync(vm, "clavo", "2");
        vm.Cliente!.Documento = "45879632";
        await vm.Cliente.BuscarCommand.EjecutarAsync();

        vm.Emision!.EsFactura = true;
        Assert.Contains("RUC", vm.Cliente.Error);
        await vm.Emision.EmitirCommand.EjecutarAsync();

        Assert.Empty(comprobantes.Comprobantes);
        Assert.Contains("RUC", vm.Emision.Error);
    }

    [Fact]
    public async Task Con_Factura_elegida_un_DNI_no_se_busca()
    {
        var (vm, _) = Crear();
        vm.Emision!.EsFactura = true;
        vm.Cliente!.Documento = "45879632";

        await vm.Cliente.BuscarCommand.EjecutarAsync();

        Assert.False(vm.Cliente.HayCliente);
        Assert.Contains("requiere el RUC", vm.Cliente.Error);
    }

    [Fact]
    public async Task Volver_a_Boleta_quita_el_pedido_del_RUC()
    {
        var (vm, _) = Crear();
        vm.Emision!.EsFactura = true;
        vm.Cliente!.Documento = "4587";
        await vm.Cliente.BuscarCommand.EjecutarAsync();
        Assert.True(vm.Cliente.TieneError);

        vm.Emision.EsBoleta = true;

        Assert.False(vm.Cliente.ExigeRuc);
        Assert.False(vm.Cliente.TieneError);
        Assert.Equal("_DNI o RUC", vm.Cliente.EtiquetaDocumento);
    }

    [Fact]
    public async Task Boletas_y_facturas_llevan_su_propia_numeracion()
    {
        var (vm, comprobantes) = Crear();

        await AgregarAsync(vm, "clavo", "1");
        await vm.Emision!.EmitirCommand.EjecutarAsync();
        await AgregarAsync(vm, "cemento", "10");
        vm.Emision.EsFactura = true;
        vm.Cliente!.Documento = "20131312955";
        await vm.Cliente.BuscarCommand.EjecutarAsync();
        await vm.Emision.EmitirCommand.EjecutarAsync();
        await AgregarAsync(vm, "clavo", "1");
        await vm.Emision.EmitirCommand.EjecutarAsync();

        Assert.Equal(new[] { "B001-00000001", "F001-00000001", "B001-00000002" },
            comprobantes.Comprobantes.Select(c => c.NumeroCompleto));
    }

    // HU-08: nota de venta (T-68).

    [Fact]
    public async Task Nota_de_venta_sale_con_la_serie_NV01_sin_pedir_documento()
    {
        var (vm, comprobantes) = Crear();
        await AgregarAsync(vm, "cemento", "30"); // S/ 975.00: más de S/ 700, sin DNI
        vm.Emision!.EsNotaVenta = true;

        await vm.Emision.EmitirCommand.EjecutarAsync();

        var nota = Assert.Single(comprobantes.Comprobantes);
        Assert.Null(vm.Emision.Error);
        Assert.Equal("NV01-00000001", nota.NumeroCompleto);
        Assert.Equal(TipoComprobante.NotaVenta, nota.TipoComprobante);
        Assert.Null(nota.Cliente);
        Assert.Contains("Nota de venta NV01-00000001 emitida", vm.Mensaje);
        Assert.True(vm.Emision.EsBoleta); // la siguiente venta vuelve a Boleta
    }

    [Fact]
    public async Task Nota_de_venta_lleva_al_cliente_buscado()
    {
        var (vm, comprobantes) = Crear();
        await AgregarAsync(vm, "fierro", "2");
        vm.Cliente!.Documento = "20131312955";
        await vm.Cliente.BuscarCommand.EjecutarAsync();
        vm.Emision!.EsNotaVenta = true;

        await vm.Emision.EmitirCommand.EjecutarAsync();

        Assert.Equal("20131312955", Assert.Single(comprobantes.Comprobantes).Cliente!.NumeroDocumento);
    }

    [Fact]
    public void Elegir_Nota_de_venta_no_pide_el_RUC()
    {
        var (vm, _) = Crear();
        var pedidos = 0;
        vm.Emision!.RucRequerido += () => pedidos++;
        vm.Emision.EsFactura = true;

        vm.Emision.EsNotaVenta = true;

        Assert.Equal(TipoEmision.NotaVenta, vm.Emision.Tipo);
        Assert.False(vm.Emision.EsFactura);
        Assert.False(vm.Cliente!.ExigeRuc);
        Assert.Equal(1, pedidos); // solo al elegir Factura
    }

    [Fact]
    public async Task Las_notas_de_venta_llevan_su_propia_numeracion()
    {
        var (vm, comprobantes) = Crear();

        await AgregarAsync(vm, "clavo", "1");
        await vm.Emision!.EmitirCommand.EjecutarAsync();
        await AgregarAsync(vm, "clavo", "1");
        vm.Emision.EsNotaVenta = true;
        await vm.Emision.EmitirCommand.EjecutarAsync();
        await AgregarAsync(vm, "clavo", "1");
        vm.Emision.EsNotaVenta = true;
        await vm.Emision.EmitirCommand.EjecutarAsync();

        Assert.Equal(new[] { "B001-00000001", "NV01-00000001", "NV01-00000002" },
            comprobantes.Comprobantes.Select(c => c.NumeroCompleto));
    }
}
