using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

public class ComprasViewModelTests
{
    private static readonly DateOnly Hoy = DatosPrueba.Hoy();

    private static (ComprasViewModel Vm, CompraRepositorioEnMemoria Repo) Ventana(params Compra[] compras)
    {
        var repo = new CompraRepositorioEnMemoria(compras);
        return (new ComprasViewModel(repo, () => Hoy), repo);
    }

    [Fact]
    public void Al_abrir_muestra_las_compras_del_mes()
    {
        var (vm, _) = Ventana();

        var filtro = vm.Filtro()!;
        Assert.Equal(new DateOnly(Hoy.Year, Hoy.Month, 1), filtro.Desde);
        Assert.Equal(Hoy, filtro.Hasta);
        Assert.Equal(string.Empty, filtro.Proveedor);
    }

    [Fact]
    public async Task Lista_las_compras_con_el_total_de_egresos_y_elige_la_mas_reciente()
    {
        var aceros = DatosPrueba.CompraAceros(fecha: Hoy);
        var cemento = DatosPrueba.CompraCemento(fecha: Hoy);
        var (vm, _) = Ventana(aceros, cemento);

        await vm.BuscarAsync();

        Assert.Equal(new[] { cemento, aceros }, vm.Compras);
        Assert.Equal(2749.00m, vm.Total);
        Assert.Equal("2 compras · Egresos del período: S/ 2749.00", vm.Resumen);
        Assert.Same(cemento, vm.Seleccionada);
    }

    [Fact]
    public async Task Filtra_por_proveedor_y_fechas()
    {
        var (vm, repo) = Ventana(DatosPrueba.CompraAceros(fecha: Hoy), DatosPrueba.CompraCemento(fecha: Hoy));
        vm.Proveedor = "Yura";
        vm.Desde = Hoy.AddDays(-7).ToDateTime(TimeOnly.MinValue);

        await vm.BuscarCommand.EjecutarAsync();

        Assert.Equal("Yura", repo.UltimoFiltro!.Proveedor);
        Assert.Equal(Hoy.AddDays(-7), repo.UltimoFiltro.Desde);
        Assert.Equal("Yura S.A.", Assert.Single(vm.Compras).Proveedor!.RazonSocial);
        Assert.Equal("1 compra · Egresos del período: S/ 1112.00", vm.Resumen);
    }

    [Fact]
    public async Task Al_elegir_una_compra_muestra_su_detalle()
    {
        var aceros = DatosPrueba.CompraAceros(fecha: Hoy);
        var (vm, _) = Ventana(aceros, DatosPrueba.CompraCemento(fecha: Hoy.AddDays(-1)));
        await vm.BuscarAsync();

        vm.Seleccionada = aceros;
        await vm.UltimoDetalle;

        Assert.Equal(new[] { "Fierro corrugado 1/2 in x 9 m", "Alambre negro N.° 16" }, vm.Detalle.Select(l => l.Descripcion));
    }

    [Fact]
    public async Task Todo_quita_los_filtros_y_Este_mes_los_vuelve_a_poner()
    {
        var (vm, repo) = Ventana();
        vm.Proveedor = "Aceros";

        await vm.LimpiarCommand.EjecutarAsync();
        var todo = repo.UltimoFiltro!;
        await vm.MesCommand.EjecutarAsync();

        Assert.Null(todo.Desde);
        Assert.Null(todo.Hasta);
        Assert.Equal(string.Empty, todo.Proveedor);
        Assert.Equal(new DateOnly(Hoy.Year, Hoy.Month, 1), repo.UltimoFiltro!.Desde);
    }

    [Fact]
    public async Task Sin_compras_lo_dice_y_no_hay_detalle()
    {
        var (vm, _) = Ventana();

        await vm.BuscarAsync();

        Assert.Equal("No hay compras con esos filtros.", vm.Resumen);
        Assert.Equal(0m, vm.Total);
        Assert.Null(vm.Seleccionada);
        Assert.Empty(vm.Detalle);
    }

    [Fact]
    public async Task Con_las_fechas_al_reves_no_busca()
    {
        var (vm, repo) = Ventana();
        vm.Desde = Hoy.ToDateTime(TimeOnly.MinValue);
        vm.Hasta = Hoy.AddDays(-3).ToDateTime(TimeOnly.MinValue);

        await vm.BuscarAsync();

        Assert.Null(repo.UltimoFiltro);
        Assert.Contains("posterior", vm.Mensaje);
    }

    [Fact]
    public async Task Sin_conexion_avisa_y_conserva_la_lista()
    {
        var (vm, repo) = Ventana(DatosPrueba.CompraAceros(fecha: Hoy));
        await vm.BuscarAsync();
        repo.Error = new SinConexionException(new TimeoutException());
        var avisos = 0;
        vm.ConexionPerdida += () => avisos++;

        await vm.BuscarAsync();

        Assert.Equal(1, avisos);
        Assert.Contains("Sin conexión", vm.Mensaje);
        Assert.Single(vm.Compras);
    }

    [Fact]
    public void La_pantalla_principal_abre_Compras_solo_si_hay_repositorio()
    {
        Assert.Null(new MainViewModel().CrearCompras());
        Assert.NotNull(new MainViewModel(compras: new CompraRepositorioEnMemoria()).CrearCompras());
    }
}
