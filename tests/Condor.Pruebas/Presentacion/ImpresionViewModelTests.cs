using Condor.Dominio.Entidades;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

public class ImpresionViewModelTests
{
    private sealed record Pantalla(MainViewModel Vm, ComprobanteRepositorioEnMemoria Comprobantes, ImpresoraFalsa Ticketera);

    private static Pantalla Crear()
    {
        var comprobantes = new ComprobanteRepositorioEnMemoria();
        var ticketera = new ImpresoraFalsa();
        var vm = new MainViewModel(new ProductoRepositorioEnMemoria(DatosPrueba.Catalogo()),
            new ClienteRepositorioEnMemoria(DatosPrueba.Clientes()), comprobantes)
        {
            Impresion = new ImpresionViewModel(ticketera, new EmpresaRepositorioEnMemoria(), comprobantes),
        };
        vm.Buscador!.Espera = TimeSpan.Zero;
        vm.IniciarSesion(new Sesion(new Terminal { Id = 1, Nombre = "PC-1" }));
        return new Pantalla(vm, comprobantes, ticketera);
    }

    private static async Task VenderAsync(MainViewModel vm, string texto = "cemento")
    {
        vm.Buscador!.Texto = texto;
        await vm.Buscador.UltimaBusqueda;
        vm.Buscador.Elegir(vm.Buscador.Seleccionado);
        Assert.True(vm.Venta.Agregar());
        await vm.Emision!.EmitirCommand.EjecutarAsync();
        await vm.UltimaImpresion;
    }

    [Fact]
    public async Task Al_emitir_sale_el_ticket_y_se_anota_la_impresion()
    {
        var (vm, comprobantes, ticketera) = Crear();

        await VenderAsync(vm);

        var ticket = Assert.Single(ticketera.Impresos);
        Assert.Contains("B001-00000001", ticket.ComoTexto());
        Assert.Equal(1, comprobantes.Comprobantes[0].Impresiones);
        Assert.Equal(1, comprobantes.ImpresionesRegistradas[comprobantes.Comprobantes[0].Id]);
        Assert.Contains("Impresa en POS-80", vm.Mensaje);
        Assert.Null(vm.Impresion!.Error);
    }

    [Fact]
    public async Task Si_la_ticketera_falla_el_comprobante_queda_registrado_y_se_avisa()
    {
        var (vm, comprobantes, ticketera) = Crear();
        ticketera.Falla = "La ticketera no tiene papel.";

        await VenderAsync(vm);

        var boleta = Assert.Single(comprobantes.Comprobantes);
        Assert.Equal(0, boleta.Impresiones);
        Assert.Contains("B001-00000001 quedó registrada", vm.Impresion!.Error);
        Assert.Contains("no tiene papel", vm.Impresion.Error);
        Assert.Contains("F11", vm.Mensaje);
        Assert.Empty(vm.Venta.Detalle); // se puede seguir vendiendo
    }

    [Fact]
    public async Task Reimprimir_con_F11_saca_el_ultimo_ticket_cuando_vuelve_el_papel()
    {
        var (vm, comprobantes, ticketera) = Crear();
        ticketera.Falla = "Sin papel.";
        await VenderAsync(vm);

        ticketera.Falla = null;
        await vm.Impresion!.ReimprimirCommand.EjecutarAsync();

        Assert.Single(ticketera.Impresos);
        Assert.Null(vm.Impresion.Error);
        Assert.Equal(1, comprobantes.Comprobantes[0].Impresiones);
    }

    [Fact]
    public async Task Desde_los_ultimos_comprobantes_se_reimprime_el_que_no_salio()
    {
        var (vm, comprobantes, ticketera) = Crear();
        await VenderAsync(vm, "cemento");
        ticketera.Falla = "Apagada.";
        await VenderAsync(vm, "fierro");
        ticketera.Falla = null;

        var historial = vm.CrearComprobantesRecientes()!;
        await historial.CargarAsync();

        Assert.Equal(2, historial.Comprobantes.Count);
        Assert.Equal("B001-00000002", historial.Seleccionado!.NumeroCompleto); // el que quedó sin imprimir
        await historial.ReimprimirCommand.EjecutarAsync();

        Assert.Equal(2, ticketera.Impresos.Count);
        Assert.Contains("Fierro", ticketera.Impresos[1].ComoTexto());
        Assert.Equal(1, historial.Seleccionado.Impresiones);
        Assert.Contains("reimpreso", historial.Mensaje);
    }

    [Fact]
    public void Sin_impresion_no_hay_historial_de_reimpresion()
    {
        var vm = new MainViewModel(new ProductoRepositorioEnMemoria(), null, new ComprobanteRepositorioEnMemoria());

        Assert.Null(vm.CrearComprobantesRecientes());
    }

    [Fact]
    public void Reimprimir_esta_deshabilitado_si_aun_no_se_emitio_nada()
    {
        var (vm, _, _) = Crear();

        Assert.False(vm.Impresion!.ReimprimirCommand.CanExecute(null));
    }
}
