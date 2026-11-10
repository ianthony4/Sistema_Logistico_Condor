using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

/// <summary>Historial (F5) con el estado SUNAT de cada comprobante (T-48).</summary>
public class ComprobantesRecientesViewModelTests
{
    [Fact]
    public async Task Cuenta_los_pendientes_y_rechazados_y_lo_dice_en_el_mensaje()
    {
        var repo = new ComprobanteRepositorioEnMemoria();
        await EmitirAsync(repo, EstadoSunat.Aceptado);
        await EmitirAsync(repo, EstadoSunat.Pendiente, "No hay conexión a internet.");
        await EmitirAsync(repo, EstadoSunat.Pendiente);
        await EmitirAsync(repo, EstadoSunat.Rechazado, "2800 - El tipo de documento del receptor no es valido");
        var vm = Historial(repo);

        await vm.CargarAsync();

        Assert.Equal(2, vm.PendientesSunat);
        Assert.Equal(1, vm.RechazadosSunat);
        Assert.Equal("2 pendientes de envío a la SUNAT · 1 rechazada por la SUNAT. Pase el mouse por el estado para ver el motivo.", vm.Mensaje);
        Assert.Contains(vm.Comprobantes, c => c.MensajeSunat == "No hay conexión a internet.");
    }

    [Fact]
    public async Task Si_todo_esta_aceptado_no_hay_aviso()
    {
        var repo = new ComprobanteRepositorioEnMemoria();
        await EmitirAsync(repo, EstadoSunat.Aceptado);
        await EmitirAsync(repo, EstadoSunat.Observado);
        var vm = Historial(repo);

        await vm.CargarAsync();

        Assert.Equal(0, vm.PendientesSunat);
        Assert.Null(vm.Mensaje);
    }

    [Fact]
    public async Task Las_notas_de_venta_no_cuentan_como_pendientes_de_la_SUNAT()
    {
        var repo = new ComprobanteRepositorioEnMemoria();
        var nota = await EmitirAsync(repo, EstadoSunat.Pendiente);
        nota.TipoComprobante = TipoComprobante.NotaVenta;
        var vm = Historial(repo);

        await vm.CargarAsync();

        Assert.Equal(0, vm.PendientesSunat);
    }

    private static ComprobantesRecientesViewModel Historial(ComprobanteRepositorioEnMemoria repo) =>
        new(repo, new ImpresionViewModel(new ImpresoraFalsa(), new EmpresaRepositorioEnMemoria(), repo));

    private static async Task<Comprobante> EmitirAsync(ComprobanteRepositorioEnMemoria repo, string estado, string? mensaje = null)
    {
        var comprobante = await repo.EmitirAsync(new Comprobante { SerieCodigo = "B001", Total = 10m, Impresiones = 1 });
        comprobante.EstadoSunat = estado;
        comprobante.MensajeSunat = mensaje;
        return comprobante;
    }
}
