using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

/// <summary>Ventana Respaldo (F9, HU-15, T-62) con el generador falso y el registro en memoria.</summary>
public class RespaldoViewModelTests
{
    private sealed record Pantalla(MainViewModel Vm, RespaldoViewModel Respaldo, GeneradorRespaldoFalso Generador,
        RespaldoRepositorioEnMemoria Registro);

    private static Pantalla Crear(bool principal = true, bool registrada = true)
    {
        var generador = new GeneradorRespaldoFalso();
        var registro = new RespaldoRepositorioEnMemoria(new[] { DatosPrueba.RespaldoDeHace(30) });
        var vm = new MainViewModel();
        vm.IniciarSesion(new Sesion(new Terminal { Id = registrada ? 1 : 0, Nombre = "PC-PRINCIPAL" }));
        vm.ConfigurarRespaldo(principal ? generador : null, registro, DatosPrueba.DiscoExterno);
        return new Pantalla(vm, vm.CrearRespaldo()!, generador, registro);
    }

    [Fact]
    public async Task Al_abrir_muestra_el_ultimo_respaldo()
    {
        var (_, respaldo, _, _) = Crear();

        await respaldo.CargarAsync();

        Assert.Single(respaldo.Recientes);
        Assert.StartsWith("Último respaldo:", respaldo.TextoUltimo);
        Assert.Equal(DatosPrueba.DiscoExterno, respaldo.Carpeta);
        Assert.True(respaldo.GenerarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Generar_crea_el_archivo_con_fecha_y_hora_y_confirma_al_terminar()
    {
        var (vm, respaldo, generador, registro) = Crear();
        generador.Ahora = () => new DateTimeOffset(2026, 11, 19, 18, 30, 0, TimeSpan.FromHours(-5));
        Respaldo? generado = null;
        respaldo.RespaldoGenerado += r => generado = r;

        await respaldo.GenerarCommand.EjecutarAsync();

        Assert.Null(respaldo.Error);
        Assert.Equal("condor_2026-11-19_18-30.backup", generado!.Archivo);
        Assert.Equal(100, respaldo.Porcentaje);
        Assert.Contains("Respaldo terminado: condor_2026-11-19_18-30.backup", respaldo.Mensaje);
        Assert.Contains(DatosPrueba.DiscoExterno, respaldo.Mensaje);
        Assert.Equal(2, registro.Respaldos.Count);
        Assert.Equal(1, generado.TerminalId); // anotado a nombre de la PC principal
        Assert.Same(generado, respaldo.Ultimo);
        Assert.Contains("Respaldo generado", vm.Mensaje);
        Assert.False(respaldo.Generando);
    }

    [Fact]
    public async Task Mientras_genera_muestra_el_avance_y_no_deja_generar_otro()
    {
        var (_, respaldo, generador, _) = Crear();
        generador.Demora = new TaskCompletionSource();

        var enCurso = respaldo.GenerarCommand.EjecutarAsync();

        Assert.True(respaldo.Generando);
        Assert.InRange(respaldo.Porcentaje, 1, 99);
        Assert.StartsWith("Copiando", respaldo.Paso);
        Assert.False(respaldo.GenerarCommand.CanExecute(null));

        generador.Demora.SetResult();
        await enCurso;
        Assert.Equal(100, respaldo.Porcentaje);
    }

    [Fact]
    public async Task Sin_el_disco_externo_conectado_avisa_y_no_registra_nada()
    {
        var (_, respaldo, _, registro) = Crear();
        respaldo.Carpeta = @"F:\Respaldos Condor";

        await respaldo.GenerarCommand.EjecutarAsync();

        Assert.Contains("Conecte el disco externo", respaldo.Error);
        Assert.Null(respaldo.Mensaje);
        Assert.Equal(0, respaldo.Porcentaje);
        Assert.Single(registro.Respaldos);
    }

    [Fact]
    public async Task Si_pg_dump_falla_a_mitad_avisa_el_motivo()
    {
        var (_, respaldo, generador, registro) = Crear();
        generador.Falla = "could not write to output file: No space left on device";

        await respaldo.GenerarCommand.EjecutarAsync();

        Assert.Contains("No space left on device", respaldo.Error);
        Assert.Single(registro.Respaldos);
        Assert.False(respaldo.Generando);
    }

    [Fact]
    public async Task Si_se_cae_la_red_al_anotarlo_dice_que_el_archivo_quedo_en_el_disco()
    {
        var (vm, respaldo, generador, registro) = Crear();
        vm.Conexion = new EstadoConexionViewModel(_ => Task.FromResult(true));
        registro.Error = new SinConexionException(new TimeoutException());

        await respaldo.GenerarCommand.EjecutarAsync();

        Assert.Single(generador.Generados);
        Assert.Contains("quedó en el disco", respaldo.Error);
        Assert.False(vm.Conexion.Conectado);
    }

    [Fact]
    public void En_una_PC_que_no_es_la_principal_no_se_puede_generar()
    {
        var (_, respaldo, _, _) = Crear(principal: false);

        Assert.False(respaldo.PuedeGenerar);
        Assert.False(respaldo.GenerarCommand.CanExecute(null));
        Assert.Contains("PC principal", respaldo.Indicacion);
    }

    [Fact]
    public async Task Si_la_PC_aun_no_se_registro_no_genera()
    {
        var (_, respaldo, generador, _) = Crear(registrada: false);

        await respaldo.GenerarCommand.EjecutarAsync();

        Assert.Empty(generador.Generados);
        Assert.Contains("no se registró", respaldo.Error);
    }

    [Fact]
    public void Sin_respaldos_configurados_la_ventana_no_se_crea()
    {
        Assert.Null(new MainViewModel().CrearRespaldo());
    }
}
