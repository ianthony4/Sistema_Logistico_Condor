using Condor.Datos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Impresion;
using Condor.Impresion;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;
using Npgsql;

namespace Condor.Pruebas.Aceptacion;

/// <summary>
/// HU-12: imprimir en la ticketera térmica.
/// Criterio 1: al emitir un comprobante, la ticketera imprime el ticket de
/// 80 mm con el RUC de la ferretería, el tipo y número de comprobante, el
/// detalle, el IGV y el total.
/// Criterio 2: si la ticketera está apagada o sin papel, el sistema lo
/// informa, el comprobante sigue registrado y se puede reimprimir desde el
/// historial.
/// </summary>
[Collection(BaseDatos.Nombre)]
public class HU12_ImprimirTicketTests
{
    private readonly BaseDatosFixture _bd;

    public HU12_ImprimirTicketTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task CA1_al_emitir_sale_el_ticket_de_80_mm_con_todos_sus_datos()
    {
        var (vm, ticketera) = await PantallaAsync();
        await VenderAsync(vm);

        var ticket = Assert.Single(ticketera.Impresos);
        var texto = ticket.ComoTexto();
        var boleta = vm.Emision!.Ultimo!;
        var empresa = await new EmpresaRepositorio(_bd.Conexiones).ObtenerAsync();

        Assert.Contains($"RUC {empresa.Ruc}", texto);
        Assert.Contains("BOLETA DE VENTA ELECTRÓNICA", texto);
        Assert.Contains(boleta.NumeroCompleto, texto);
        Assert.Contains(boleta.Detalle[0].Descripcion, texto);
        Assert.Contains($"IGV 18%      S/ {FormatoTicket.Monto(boleta.Igv),11}", texto);
        Assert.Contains($"TOTAL        S/ {FormatoTicket.Monto(boleta.Total),11}", texto);
        Assert.All(ticket.Lineas, l => Assert.True(l.Texto.Length <= 48));
        Assert.Equal(1, await ImpresionesAsync(boleta.Id));
    }

    [Fact]
    public void CA1_el_ticket_se_manda_a_la_ticketera_con_corte_de_papel()
    {
        var boleta = new Comprobante { SerieCodigo = "B001", Numero = 7, Fecha = DateTimeOffset.Now, Total = 11.80m, OpGravada = 10m, Igv = 1.80m };
        boleta.Detalle.Add(new DetalleComprobante { Descripcion = "Lija de agua N.° 100", Cantidad = 4, PrecioUnitario = 2.95m, Importe = 11.80m });

        var datos = ComandosEscPos.Generar(FormatoTicket.Generar(DatosPrueba.Empresa(), boleta));

        Assert.Equal(ComandosEscPos.Cortar, datos.TakeLast(ComandosEscPos.Cortar.Length));
        Assert.Contains((byte)0xF8, datos); // "°" en la página 850: los símbolos no se pierden
    }

    [FactConBD]
    public async Task CA2_ticketera_apagada_avisa_y_el_comprobante_sigue_registrado()
    {
        var (vm, ticketera) = await PantallaAsync();
        ticketera.Falla = "La ticketera \"POS-80\" está apagada o desconectada.";

        await VenderAsync(vm);

        var boleta = vm.Emision!.Ultimo!;
        Assert.Contains("apagada", vm.Impresion!.Error);
        Assert.Contains(boleta.NumeroCompleto, vm.Impresion.Error);
        var guardada = await new ComprobanteRepositorio(_bd.Conexiones).ObtenerAsync(boleta.Id);
        Assert.NotNull(guardada);
        Assert.Equal(0, guardada.Impresiones);
        Assert.Empty(vm.Venta.Detalle); // se puede seguir vendiendo
    }

    [FactConBD]
    public async Task CA2_se_reimprime_desde_el_historial_con_el_detalle_completo()
    {
        var (vm, ticketera) = await PantallaAsync();
        ticketera.Falla = "Sin papel.";
        await VenderAsync(vm);
        var boleta = vm.Emision!.Ultimo!;
        ticketera.Falla = null; // se cambió el rollo

        // Otra pantalla (por ejemplo, tras reiniciar el programa) lee el historial de la BD.
        var (otra, otraTicketera) = await PantallaAsync();
        var historial = otra.CrearComprobantesRecientes()!;
        await historial.CargarAsync();
        historial.Seleccionado = historial.Comprobantes.First(c => c.Id == boleta.Id);
        Assert.Empty(historial.Seleccionado.Detalle);

        await historial.ReimprimirCommand.EjecutarAsync();

        var texto = Assert.Single(otraTicketera.Impresos).ComoTexto();
        Assert.Contains(boleta.NumeroCompleto, texto);
        Assert.Contains(boleta.Detalle[0].Descripcion, texto);
        Assert.Equal(1, await ImpresionesAsync(boleta.Id));
    }

    /// <summary>Pantalla Nueva venta contra la base de pruebas, con una ticketera falsa.</summary>
    private async Task<(MainViewModel Vm, ImpresoraFalsa Ticketera)> PantallaAsync()
    {
        var pc = _bd.Conexiones;
        var comprobantes = new ComprobanteRepositorio(pc);
        var ticketera = new ImpresoraFalsa();
        var vm = new MainViewModel(new ProductoRepositorio(pc), new ClienteRepositorio(pc), comprobantes)
        {
            Impresion = new ImpresionViewModel(ticketera, new EmpresaRepositorio(pc), comprobantes),
        };
        vm.Buscador!.Espera = TimeSpan.Zero;
        vm.IniciarSesion(new Sesion(await new TerminalRepositorio(pc).RegistrarAccesoAsync("PC-HU12")));
        return (vm, ticketera);
    }

    private async Task VenderAsync(MainViewModel vm)
    {
        var producto = await new ProductoRepositorio(_bd.Conexiones)
            .RegistrarAsync(DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Tubo PVC agua"), 14.50m));
        vm.Buscador!.Elegir(producto);
        vm.Venta.CantidadTexto = "5";
        Assert.True(vm.Venta.Agregar());

        await vm.Emision!.EmitirCommand.EjecutarAsync();
        Assert.Null(vm.Emision.Error);
        await vm.UltimaImpresion;
    }

    private async Task<int> ImpresionesAsync(long id)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand("SELECT impresiones FROM comprobante WHERE id = @id", conexion);
        cmd.Parameters.AddWithValue("id", id);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}
