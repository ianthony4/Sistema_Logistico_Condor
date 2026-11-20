using Condor.Datos;
using Condor.Datos.Respaldos;
using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Aceptacion;

/// <summary>
/// HU-15: respaldo manual de la base de datos.
/// Criterio 1: si el disco externo está conectado a la PC principal, al
/// presionar Generar respaldo el sistema crea un archivo con la fecha y la
/// hora en el nombre, muestra el progreso y confirma al terminar.
/// Criterio 2: si el último respaldo tiene más de 24 horas, al abrir el
/// sistema se muestra un aviso para generar uno nuevo.
/// </summary>
/// <remarks>
/// Las pruebas [FactConBD] simulan pg_dump; la [FactConPgDump] ejecuta el
/// pg_dump real contra la base de pruebas.
/// </remarks>
[Collection(BaseDatos.Nombre)]
public sealed class HU15_RespaldoTests : IDisposable
{
    private readonly BaseDatosFixture _bd;
    private readonly string _disco = Directory.CreateTempSubdirectory("condor_disco_externo_").FullName;

    public HU15_RespaldoTests(BaseDatosFixture bd) => _bd = bd;

    public void Dispose() => Directory.Delete(_disco, recursive: true);

    [FactConBD]
    public async Task CA1_con_el_disco_conectado_crea_el_archivo_con_fecha_y_hora_muestra_el_progreso_y_confirma()
    {
        var hora = new DateTimeOffset(2026, 11, 20, 18, 45, 0, TimeSpan.FromHours(-5));
        var (vm, _) = await PantallaPrincipalAsync(new PgDumpSimulado(), () => hora);
        var respaldo = vm.CrearRespaldo()!;
        var avances = new List<int>();
        respaldo.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RespaldoViewModel.Porcentaje))
                avances.Add(respaldo.Porcentaje);
        };

        await respaldo.GenerarCommand.EjecutarAsync();

        Assert.Null(respaldo.Error);
        var archivo = Path.Combine(_disco, $"condor_pruebas_2026-11-20_18-45{Respaldo.Extension}");
        Assert.True(File.Exists(archivo));
        Assert.Contains(avances, p => p is > 0 and < 100); // se vio el avance
        Assert.Equal(100, avances[^1]);
        Assert.Contains("Respaldo terminado", respaldo.Mensaje);
        Assert.Contains(_disco, respaldo.Mensaje);

        // Queda anotado en la base central: las otras PC lo ven como el último.
        await using var local = new TresPC();
        var ultimo = await new RespaldoRepositorio(local.Pc[1]).ListarAsync(100);
        Assert.Contains(ultimo, r => r.Archivo == Path.GetFileName(archivo) && r.Carpeta == _disco);
    }

    [FactConBD]
    public async Task CA1_sin_el_disco_externo_no_se_genera_y_se_dice_que_conectarlo()
    {
        var (vm, _) = await PantallaPrincipalAsync(new PgDumpSimulado(), () => DateTimeOffset.Now);
        var respaldo = vm.CrearRespaldo()!;
        respaldo.Carpeta = Path.Combine(_disco, "disco-desconectado");

        await respaldo.GenerarCommand.EjecutarAsync();

        Assert.Contains("Conecte el disco externo", respaldo.Error);
        Assert.Null(respaldo.Mensaje);
        Assert.Empty(Directory.GetFiles(_disco));
    }

    [FactConBD]
    public async Task CA2_con_el_ultimo_respaldo_de_hace_mas_de_24_horas_avisa_al_abrir()
    {
        var (vm, registro) = await PantallaPrincipalAsync(new PgDumpSimulado(), () => DateTimeOffset.Now);
        await vm.CrearRespaldo()!.GenerarCommand.EjecutarAsync();
        var ultimo = (await registro.UltimoAsync())!;

        // Al día siguiente, 25 horas después del último respaldo, se abre el sistema en otra PC.
        var otraPc = new MainViewModel();
        otraPc.ConfigurarRespaldo(null, registro, carpeta: null, avisoHoras: 24, ahora: () => ultimo.TerminadoEn.AddHours(25));
        await otraPc.AvisoRespaldo!.RevisarAsync();

        Assert.Contains("El último respaldo es del", otraPc.AvisoRespaldo.Aviso);
        Assert.Contains("Respaldo (F9)", otraPc.AvisoRespaldo.Aviso);
    }

    [FactConBD]
    public async Task CA2_con_un_respaldo_reciente_no_hay_aviso()
    {
        var (vm, registro) = await PantallaPrincipalAsync(new PgDumpSimulado(), () => DateTimeOffset.Now);
        await vm.CrearRespaldo()!.GenerarCommand.EjecutarAsync();
        var ultimo = (await registro.UltimoAsync())!;

        var otraPc = new MainViewModel();
        otraPc.ConfigurarRespaldo(null, registro, carpeta: null, avisoHoras: 24, ahora: () => ultimo.TerminadoEn.AddHours(3));
        await otraPc.AvisoRespaldo!.RevisarAsync();

        Assert.Null(otraPc.AvisoRespaldo.Aviso);
    }

    [FactConPgDump]
    public async Task CA1_con_el_pg_dump_real_el_respaldo_se_puede_restaurar()
    {
        var generador = new GeneradorRespaldo(_bd.Conexiones, Environment.GetEnvironmentVariable(FactConPgDumpAttribute.Variable));
        var pasos = new List<string>();

        var respaldo = await generador.GenerarAsync(_disco, new ProgresoSincrono(p => pasos.Add(p.Paso)));

        // Formato personalizado de pg_dump: empieza con la firma PGDMP.
        var cabecera = new byte[5];
        await using (var archivo = File.OpenRead(respaldo.Ruta))
            await archivo.ReadExactlyAsync(cabecera);
        Assert.Equal("PGDMP"u8.ToArray(), cabecera);
        Assert.True(respaldo.Bytes > 10_000);
        Assert.Contains(pasos, p => p.StartsWith("Copiando comprobante "));
        Assert.Equal("Respaldo terminado.", pasos[^1]);
    }

    /// <summary>Pantalla de la PC principal: el respaldo va a la carpeta temporal que hace de disco externo.</summary>
    private async Task<(MainViewModel Vm, RespaldoRepositorio Registro)> PantallaPrincipalAsync(IEjecutorProceso pgDump,
        Func<DateTimeOffset> ahora)
    {
        var registro = new RespaldoRepositorio(_bd.Conexiones);
        var vm = new MainViewModel();
        vm.IniciarSesion(new Sesion(await new TerminalRepositorio(_bd.Conexiones).RegistrarAccesoAsync("PC-PRINCIPAL")));
        vm.ConfigurarRespaldo(new GeneradorRespaldo(_bd.Conexiones, ejecutor: pgDump, ahora: ahora), registro, _disco);
        return (vm, registro);
    }

    /// <summary>pg_dump simulado: escribe el archivo e informa las tablas como --verbose.</summary>
    private sealed class PgDumpSimulado : IEjecutorProceso
    {
        public async Task<int> EjecutarAsync(string programa, IReadOnlyList<string> argumentos,
            IReadOnlyDictionary<string, string> entorno, Action<string> alLeerError, CancellationToken ct = default)
        {
            var archivo = argumentos[argumentos.ToList().IndexOf("--file") + 1];
            await File.WriteAllTextAsync(archivo, "PGDMP respaldo simulado", ct);
            foreach (var tabla in new[] { "cliente", "comprobante", "producto", "respaldo" })
                alLeerError($"pg_dump: dumping contents of table \"public.{tabla}\"");
            return 0;
        }
    }

    private sealed class ProgresoSincrono(Action<ProgresoRespaldo> alInformar) : IProgress<ProgresoRespaldo>
    {
        public void Report(ProgresoRespaldo value) => alInformar(value);
    }
}
