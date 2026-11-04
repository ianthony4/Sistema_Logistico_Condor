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
}
