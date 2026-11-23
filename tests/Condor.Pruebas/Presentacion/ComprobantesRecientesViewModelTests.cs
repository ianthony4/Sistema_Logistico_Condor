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

    // HU-18: filtros y total del período (T-76).

    private static readonly DateOnly Hoy = DatosPrueba.Hoy();

    [Fact]
    public void Al_abrir_muestra_las_ventas_de_hoy()
    {
        var vm = Historial(new ComprobanteRepositorioEnMemoria());

        var filtro = vm.Filtro()!;
        Assert.Equal(Hoy, filtro.Desde);
        Assert.Equal(Hoy, filtro.Hasta);
        Assert.Null(filtro.Tipo);
        Assert.Equal("Todas las ventas", vm.Tipo.Nombre);
    }

    [Fact]
    public async Task Envia_el_filtro_de_fechas_tipo_y_texto_y_muestra_el_total()
    {
        var repo = new ComprobanteRepositorioEnMemoria();
        await EmitirAsync(repo, EstadoSunat.Aceptado);
        var factura = await repo.EmitirAsync(DatosPrueba.FacturaObra());
        var vm = Historial(repo);
        vm.Desde = Hoy.AddDays(-10).ToDateTime(TimeOnly.MinValue);
        vm.Hasta = Hoy.ToDateTime(TimeOnly.MinValue);
        vm.Tipo = ComprobantesRecientesViewModel.Tipos.Single(t => t.Codigo == TipoComprobante.Factura);
        vm.Texto = "20131312955";

        await vm.ActualizarCommand.EjecutarAsync();

        var filtro = repo.UltimoFiltro!;
        Assert.Equal(Hoy.AddDays(-10), filtro.Desde);
        Assert.Equal(TipoComprobante.Factura, filtro.Tipo);
        Assert.Equal("20131312955", filtro.Texto);
        Assert.Same(factura, Assert.Single(vm.Comprobantes));
        Assert.Equal(1030.00m, vm.Total);
        Assert.Equal("1 comprobante · Ventas del período: S/ 1030.00", vm.Resumen);
    }

    [Fact]
    public async Task El_total_no_cuenta_las_proformas_ni_las_rechazadas()
    {
        var repo = new ComprobanteRepositorioEnMemoria();
        await EmitirAsync(repo, EstadoSunat.Aceptado);                                   // S/ 10.00
        await EmitirAsync(repo, EstadoSunat.Rechazado, "2017 - DNI no válido");           // no cuenta
        await repo.EmitirAsync(DatosPrueba.ProformaObra());                              // no es venta
        await repo.EmitirAsync(DatosPrueba.NotaVentaMostrador());                         // S/ 22.50
        var vm = Historial(repo);

        await vm.CargarAsync();

        Assert.Equal(3, vm.Comprobantes.Count); // la proforma no aparece entre las ventas
        Assert.Equal(32.50m, vm.Total);
        Assert.StartsWith("3 comprobantes · Ventas del período: S/ 32.50", vm.Resumen);
    }

    [Fact]
    public async Task Las_proformas_se_ven_eligiendo_su_tipo_y_sin_total_de_ventas()
    {
        var repo = new ComprobanteRepositorioEnMemoria();
        var proforma = await repo.EmitirAsync(DatosPrueba.ProformaObra());
        var vm = Historial(repo);
        vm.Tipo = ComprobantesRecientesViewModel.Tipos.Single(t => t.Codigo == TipoComprobante.Proforma);

        await vm.CargarAsync();

        Assert.Same(proforma, Assert.Single(vm.Comprobantes));
        Assert.Equal("1 comprobante", vm.Resumen);
    }

    [Fact]
    public async Task Este_mes_va_del_dia_1_a_hoy_y_Todo_quita_los_filtros()
    {
        var repo = new ComprobanteRepositorioEnMemoria();
        var vm = Historial(repo);
        vm.Texto = "Juan";

        await vm.MesCommand.EjecutarAsync();
        var delMes = repo.UltimoFiltro!;
        await vm.LimpiarCommand.EjecutarAsync();
        var todo = repo.UltimoFiltro!;

        Assert.Equal(new DateOnly(Hoy.Year, Hoy.Month, 1), delMes.Desde);
        Assert.Equal(Hoy, delMes.Hasta);
        Assert.Equal("Juan", delMes.Texto);
        Assert.Null(todo.Desde);
        Assert.Null(todo.Hasta);
        Assert.Equal(string.Empty, todo.Texto);
        Assert.Equal("Todas las ventas", vm.Tipo.Nombre);
    }

    [Fact]
    public async Task Con_las_fechas_al_reves_no_busca_y_lo_dice()
    {
        var repo = new ComprobanteRepositorioEnMemoria();
        var vm = Historial(repo);
        vm.Desde = new DateTime(2026, 11, 23);
        vm.Hasta = new DateTime(2026, 11, 13);

        await vm.CargarAsync();

        Assert.Null(repo.UltimoFiltro);
        Assert.Contains("posterior", vm.ErrorFiltro);
    }

    [Fact]
    public async Task Si_hay_mas_de_los_que_se_muestran_lo_avisa()
    {
        var repo = new ComprobanteRepositorioEnMemoria();
        for (var i = 0; i < ComprobantesRecientesViewModel.Cantidad + 5; i++)
            await EmitirAsync(repo, EstadoSunat.Aceptado);
        var vm = Historial(repo);

        await vm.CargarAsync();

        Assert.Equal(ComprobantesRecientesViewModel.Cantidad, vm.Comprobantes.Count);
        Assert.Equal(ComprobantesRecientesViewModel.Cantidad + 5, vm.Encontrados);
        Assert.Equal((ComprobantesRecientesViewModel.Cantidad + 5) * 10m, vm.Total);
        Assert.Contains($"se muestran los {ComprobantesRecientesViewModel.Cantidad} más recientes", vm.Resumen);
    }

    [Fact]
    public async Task Sin_resultados_lo_dice_sobre_la_lista()
    {
        var vm = Historial(new ComprobanteRepositorioEnMemoria());

        await vm.CargarAsync();

        Assert.Equal("No hay comprobantes con esos filtros.", vm.Resumen);
        Assert.Null(vm.Seleccionado);
        Assert.False(vm.ReimprimirCommand.CanExecute(null));
    }

    private static ComprobantesRecientesViewModel Historial(ComprobanteRepositorioEnMemoria repo) =>
        new(repo, new ImpresionViewModel(new ImpresoraFalsa(), new EmpresaRepositorioEnMemoria(), repo), hoy: () => Hoy);

    private static async Task<Comprobante> EmitirAsync(ComprobanteRepositorioEnMemoria repo, string estado, string? mensaje = null)
    {
        var comprobante = await repo.EmitirAsync(new Comprobante { SerieCodigo = "B001", Total = 10m, Impresiones = 1 });
        comprobante.EstadoSunat = estado;
        comprobante.MensajeSunat = mensaje;
        return comprobante;
    }
}
