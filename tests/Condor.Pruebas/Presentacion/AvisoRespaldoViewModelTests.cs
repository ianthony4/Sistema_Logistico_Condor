using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

/// <summary>Aviso de respaldo vencido al abrir el sistema (HU-15, criterio 2; T-63).</summary>
public class AvisoRespaldoViewModelTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 20, 8, 0, 0, TimeSpan.FromHours(-5));

    private static MainViewModel Pantalla(RespaldoRepositorioEnMemoria registro, int horas = 24)
    {
        var vm = new MainViewModel();
        vm.IniciarSesion(new Sesion(new Terminal { Id = 1, Nombre = "PC-PRINCIPAL" }));
        var generador = new GeneradorRespaldoFalso { Ahora = () => Ahora };
        vm.ConfigurarRespaldo(generador, registro, DatosPrueba.DiscoExterno, horas, () => Ahora);
        return vm;
    }

    [Fact]
    public async Task Con_el_ultimo_respaldo_de_hace_mas_de_24_horas_avisa_al_abrir()
    {
        var vm = Pantalla(new RespaldoRepositorioEnMemoria(new[] { DatosPrueba.RespaldoDeHace(26, Ahora) }));

        await vm.AvisoRespaldo!.RevisarAsync();

        Assert.Contains("El último respaldo es del 19/11/2026", vm.AvisoRespaldo.Aviso);
        Assert.Contains("hace 26 horas", vm.AvisoRespaldo.Aviso);
        Assert.Contains("Respaldo (F9)", vm.AvisoRespaldo.Aviso);
    }

    [Fact]
    public async Task Con_un_respaldo_de_hoy_no_avisa()
    {
        var vm = Pantalla(new RespaldoRepositorioEnMemoria(new[] { DatosPrueba.RespaldoDeHace(5, Ahora) }));

        await vm.AvisoRespaldo!.RevisarAsync();

        Assert.Null(vm.AvisoRespaldo.Aviso);
    }

    [Fact]
    public async Task Si_nunca_se_genero_un_respaldo_avisa()
    {
        var vm = Pantalla(new RespaldoRepositorioEnMemoria());

        await vm.AvisoRespaldo!.RevisarAsync();

        Assert.StartsWith("Aún no se ha generado ningún respaldo", vm.AvisoRespaldo.Aviso);
    }

    [Fact]
    public async Task Al_generar_el_respaldo_el_aviso_desaparece()
    {
        var vm = Pantalla(new RespaldoRepositorioEnMemoria(new[] { DatosPrueba.RespaldoDeHace(72, Ahora) }));
        await vm.AvisoRespaldo!.RevisarAsync();
        Assert.Contains("hace 3 días", vm.AvisoRespaldo.Aviso);

        await vm.CrearRespaldo()!.GenerarCommand.EjecutarAsync();

        Assert.Null(vm.AvisoRespaldo.Aviso);
    }

    [Fact]
    public async Task Mas_tarde_oculta_el_aviso()
    {
        var vm = Pantalla(new RespaldoRepositorioEnMemoria());
        await vm.AvisoRespaldo!.RevisarAsync();

        vm.AvisoRespaldo.DescartarCommand.Execute(null);

        Assert.Null(vm.AvisoRespaldo.Aviso);
    }

    [Fact]
    public async Task Las_horas_del_aviso_salen_de_condor_json()
    {
        var vm = Pantalla(new RespaldoRepositorioEnMemoria(new[] { DatosPrueba.RespaldoDeHace(13, Ahora) }), horas: 12);

        await vm.AvisoRespaldo!.RevisarAsync();

        Assert.NotNull(vm.AvisoRespaldo.Aviso);
    }

    [Fact]
    public async Task Sin_la_PC_principal_no_falla_ni_avisa()
    {
        var registro = new RespaldoRepositorioEnMemoria { Error = new SinConexionException(new TimeoutException()) };
        var vm = Pantalla(registro);

        await vm.AvisoRespaldo!.RevisarAsync();

        Assert.Null(vm.AvisoRespaldo.Aviso);
    }

    [Theory]
    [InlineData(23.9, false)]
    [InlineData(24.1, true)]
    public void Vence_a_las_24_horas(double horas, bool vencido)
    {
        Assert.Equal(vencido, AvisoRespaldoViewModel.Vencido(DatosPrueba.RespaldoDeHace(horas, Ahora), Ahora, 24));
        Assert.True(AvisoRespaldoViewModel.Vencido(null, Ahora, 24));
    }
}
