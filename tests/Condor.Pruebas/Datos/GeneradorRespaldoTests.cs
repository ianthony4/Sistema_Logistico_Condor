using Condor.Datos.Respaldos;
using Condor.Dominio.Contratos;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Datos;

/// <summary>Respaldo con pg_dump (HU-15, T-61). pg_dump se simula; la base de pruebas da la conexión.</summary>
[Collection(BaseDatos.Nombre)]
public sealed class GeneradorRespaldoTests : IDisposable
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 16, 18, 30, 0, TimeSpan.FromHours(-5));

    private readonly BaseDatosFixture _bd;
    private readonly string _disco = Path.Combine(Path.GetTempPath(), "condor-respaldo-" + Guid.NewGuid().ToString("N"));

    public GeneradorRespaldoTests(BaseDatosFixture bd)
    {
        _bd = bd;
        Directory.CreateDirectory(_disco);
    }

    public void Dispose() => Directory.Delete(_disco, recursive: true);

    [FactConBD]
    public async Task Crea_el_archivo_con_la_fecha_y_la_hora_e_informa_el_avance()
    {
        var pgDump = new PgDump();
        var avances = new List<ProgresoRespaldo>();

        var respaldo = await Generador(pgDump).GenerarAsync(_disco, new ProgresoSincrono(avances.Add));

        Assert.Equal("condor_pruebas_2026-11-16_18-30.backup", respaldo.Archivo);
        Assert.True(File.Exists(Path.Combine(_disco, respaldo.Archivo)));
        Assert.Equal(new FileInfo(respaldo.Ruta).Length, respaldo.Bytes);
        Assert.Equal(0, avances[0].Porcentaje);
        Assert.Equal(100, avances[^1].Porcentaje);
        Assert.Contains(avances, a => a.Paso.StartsWith("Copiando producto"));
        Assert.Equal(avances.Select(a => a.Porcentaje).Order(), avances.Select(a => a.Porcentaje));
        Assert.Empty(Directory.GetFiles(_disco, "*" + GeneradorRespaldo.ExtensionParcial));
    }

    [FactConBD]
    public async Task Llama_a_pg_dump_con_la_conexion_de_condor_json_y_el_formato_personalizado()
    {
        var pgDump = new PgDump();

        await Generador(pgDump).GenerarAsync(_disco);

        Assert.Contains("--format=custom", pgDump.Argumentos);
        Assert.Contains("--verbose", pgDump.Argumentos);
        Assert.Equal("condor_pruebas", pgDump.Argumentos[pgDump.Argumentos.IndexOf("--dbname") + 1]);
        Assert.Equal("condor_app", pgDump.Argumentos[pgDump.Argumentos.IndexOf("--username") + 1]);
        Assert.False(string.IsNullOrEmpty(pgDump.Entorno["PGPASSWORD"]));
        Assert.DoesNotContain(pgDump.Argumentos, a => a.Contains(pgDump.Entorno["PGPASSWORD"]));
    }

    [FactConBD]
    public async Task Sin_el_disco_externo_avisa_y_no_ejecuta_pg_dump()
    {
        var pgDump = new PgDump();

        var ex = await Assert.ThrowsAsync<RespaldoException>(() =>
            Generador(pgDump).GenerarAsync(Path.Combine(_disco, "no-conectado")));

        Assert.Contains("Conecte el disco externo", ex.Message);
        Assert.Null(pgDump.Argumentos.FirstOrDefault());
    }

    [FactConBD]
    public async Task Si_pg_dump_falla_no_queda_ningun_archivo()
    {
        var pgDump = new PgDump { Codigo = 1, Error = "pg_dump: error: could not write to output file: No space left on device" };

        var ex = await Assert.ThrowsAsync<RespaldoException>(() => Generador(pgDump).GenerarAsync(_disco));

        Assert.Contains("No space left on device", ex.Message);
        Assert.Empty(Directory.GetFiles(_disco));
    }

    [FactConBD]
    public async Task Sin_pg_dump_instalado_dice_como_configurarlo()
    {
        var pgDump = new PgDump { NoInstalado = true };

        var ex = await Assert.ThrowsAsync<RespaldoException>(() => Generador(pgDump).GenerarAsync(_disco));

        Assert.Contains("PgDump", ex.Message);
    }

    [FactConBD]
    public async Task Dos_respaldos_en_el_mismo_minuto_no_se_pisan()
    {
        var generador = Generador(new PgDump());

        var primero = await generador.GenerarAsync(_disco);
        var segundo = await generador.GenerarAsync(_disco);

        Assert.NotEqual(primero.Archivo, segundo.Archivo);
        Assert.Equal("condor_pruebas_2026-11-16_18-30-2.backup", segundo.Archivo);
        Assert.Equal(2, Directory.GetFiles(_disco).Length);
    }

    [Fact]
    public void Busca_el_pg_dump_de_la_version_mas_nueva_instalada()
    {
        var programFiles = Path.Combine(_disco, "Program Files");
        foreach (var version in new[] { "15", "16" })
        {
            Directory.CreateDirectory(Path.Combine(programFiles, "PostgreSQL", version, "bin"));
            File.WriteAllText(Path.Combine(programFiles, "PostgreSQL", version, "bin", "pg_dump.exe"), "");
        }

        Assert.Equal(Path.Combine(programFiles, "PostgreSQL", "16", "bin", "pg_dump.exe"),
            GeneradorRespaldo.BuscarPgDump(null, programFiles));
        Assert.Equal(@"D:\pg\pg_dump.exe", GeneradorRespaldo.BuscarPgDump(@"D:\pg\pg_dump.exe", programFiles));
        Assert.Equal("pg_dump", GeneradorRespaldo.BuscarPgDump(null, Path.Combine(_disco, "vacio")));
    }

    private GeneradorRespaldo Generador(PgDump pgDump) =>
        new(_bd.Conexiones, pgDump: null, ejecutor: pgDump, ahora: () => Ahora);

    /// <summary>pg_dump simulado: escribe el archivo de --file e informa tres tablas como lo hace --verbose.</summary>
    private sealed class PgDump : IEjecutorProceso
    {
        public int Codigo { get; init; }

        public string? Error { get; init; }

        public bool NoInstalado { get; init; }

        public List<string> Argumentos { get; } = new();

        public Dictionary<string, string> Entorno { get; } = new();

        public async Task<int> EjecutarAsync(string programa, IReadOnlyList<string> argumentos,
            IReadOnlyDictionary<string, string> entorno, Action<string> alLeerError, CancellationToken ct = default)
        {
            if (NoInstalado)
                throw new FileNotFoundException("pg_dump", programa);
            Argumentos.AddRange(argumentos);
            foreach (var (clave, valor) in entorno)
                Entorno[clave] = valor;

            var archivo = argumentos[argumentos.ToList().IndexOf("--file") + 1];
            await File.WriteAllTextAsync(archivo, "PGDMP respaldo de prueba", ct);
            alLeerError("pg_dump: last built-in OID is 16383");
            foreach (var tabla in new[] { "cliente", "comprobante", "producto" })
                alLeerError($"pg_dump: dumping contents of table \"public.{tabla}\"");
            if (Error is not null)
                alLeerError(Error);
            return Codigo;
        }
    }

    /// <summary>Progress que avisa en el acto (Progress&lt;T&gt; lo haría después, en otro hilo).</summary>
    private sealed class ProgresoSincrono(Action<ProgresoRespaldo> alInformar) : IProgress<ProgresoRespaldo>
    {
        public void Report(ProgresoRespaldo value) => alInformar(value);
    }
}
