using System.Globalization;
using System.Text.RegularExpressions;
using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Npgsql;

namespace Condor.Datos.Respaldos;

/// <summary>
/// Respaldo manual de la base de datos central con pg_dump (HU-15, T-61).
/// </summary>
/// <remarks>
/// Usa el formato personalizado de pg_dump (comprimido, se restaura con
/// pg_restore; ver docs/respaldo.md). El avance se calcula contando las
/// tablas que pg_dump informa con --verbose. Se escribe primero en un
/// archivo ".parcial" y se renombra al terminar: si se desconecta el disco
/// o falla algo, no queda un archivo que parezca un respaldo bueno.
/// </remarks>
public sealed partial class GeneradorRespaldo : IGeneradorRespaldo
{
    public const string ExtensionParcial = ".parcial";

    private readonly FabricaConexiones _conexiones;
    private readonly string _pgDump;
    private readonly IEjecutorProceso _ejecutor;
    private readonly Func<DateTimeOffset> _ahora;

    /// <param name="pgDump">Ruta de pg_dump.exe de condor.json; si está vacía se busca en la instalación de PostgreSQL.</param>
    /// <param name="ejecutor">Para las pruebas; por defecto ejecuta el programa de verdad.</param>
    /// <param name="ahora">Para las pruebas; por defecto la hora de la PC.</param>
    public GeneradorRespaldo(FabricaConexiones conexiones, string? pgDump = null, IEjecutorProceso? ejecutor = null,
        Func<DateTimeOffset>? ahora = null)
    {
        _conexiones = conexiones;
        _pgDump = BuscarPgDump(pgDump);
        _ejecutor = ejecutor ?? new EjecutorProceso();
        _ahora = ahora ?? (() => DateTimeOffset.Now);
    }

    /// <summary>Programa que se ejecuta: la ruta encontrada o "pg_dump" (del PATH).</summary>
    public string PgDump => _pgDump;

    /// <summary>
    /// Ruta de pg_dump: la configurada si existe; si no, la de la versión más
    /// nueva instalada en C:\Program Files\PostgreSQL; si no, "pg_dump" del PATH.
    /// </summary>
    public static string BuscarPgDump(string? configurado, string? programFiles = null)
    {
        if (!string.IsNullOrWhiteSpace(configurado))
            return configurado.Trim();

        var raiz = Path.Combine(programFiles ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PostgreSQL");
        if (Directory.Exists(raiz))
        {
            var instalado = Directory.GetDirectories(raiz)
                .Select(d => (Version: int.TryParse(Path.GetFileName(d), out var v) ? v : 0,
                              Ruta: Path.Combine(d, "bin", "pg_dump.exe")))
                .Where(x => File.Exists(x.Ruta))
                .OrderByDescending(x => x.Version)
                .FirstOrDefault();
            if (instalado.Ruta is not null)
                return instalado.Ruta;
        }
        return "pg_dump";
    }

    public async Task<Respaldo> GenerarAsync(string carpeta, IProgress<ProgresoRespaldo>? progreso = null, CancellationToken ct = default)
    {
        carpeta = (carpeta ?? string.Empty).Trim();
        if (carpeta.Length == 0 || !Directory.Exists(carpeta))
            throw new RespaldoException(
                $"No se encuentra la carpeta «{carpeta}». Conecte el disco externo a la PC principal y vuelva a intentarlo.");

        progreso?.Report(new ProgresoRespaldo(0, "Preparando el respaldo…"));
        var inicio = _ahora();
        var cadena = new NpgsqlConnectionStringBuilder(_conexiones.CadenaConexion);
        var destino = RutaLibre(carpeta, inicio, cadena.Database ?? "condor");
        var parcial = destino + ExtensionParcial;
        var tablas = await ContarTablasAsync(ct);

        var argumentos = new List<string>
        {
            "--host", cadena.Host ?? "localhost",
            "--port", cadena.Port.ToString(CultureInfo.InvariantCulture),
            "--username", cadena.Username ?? "condor_app",
            "--dbname", cadena.Database ?? "condor",
            "--format=custom",
            "--verbose",
            "--no-password",
            "--file", parcial,
        };
        var entorno = new Dictionary<string, string>
        {
            ["PGPASSWORD"] = cadena.Password ?? string.Empty,
            ["PGCONNECT_TIMEOUT"] = "10",
            // Mensajes en inglés: así se reconoce el avance aunque PostgreSQL esté en español.
            ["LC_MESSAGES"] = "C",
            ["LANG"] = "C",
        };

        var copiadas = 0;
        string? ultimoError = null;
        void AlLeer(string linea)
        {
            if (TablaCopiada().Match(linea) is { Success: true } m)
            {
                copiadas++;
                var porcentaje = 5 + 90 * Math.Min(copiadas, tablas) / Math.Max(tablas, 1);
                progreso?.Report(new ProgresoRespaldo(porcentaje, $"Copiando {m.Groups["tabla"].Value} ({copiadas} de {tablas})…"));
            }
            else if (linea.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                ultimoError = linea.Replace("pg_dump: ", string.Empty).Trim();
            }
        }

        int codigo;
        try
        {
            codigo = await _ejecutor.EjecutarAsync(_pgDump, argumentos, entorno, AlLeer, ct);
        }
        catch (FileNotFoundException ex)
        {
            Borrar(parcial);
            throw new RespaldoException(
                "No se encontró pg_dump. Instale PostgreSQL 16 (o solo sus herramientas) en esta PC o ponga la ruta de " +
                "pg_dump.exe en condor.json (sección Respaldo, PgDump).", ex);
        }
        catch
        {
            Borrar(parcial);
            throw;
        }

        if (codigo != 0 || !File.Exists(parcial) || new FileInfo(parcial).Length == 0)
        {
            Borrar(parcial);
            throw new RespaldoException(
                $"El respaldo no se completó (pg_dump salió con el código {codigo}): {ultimoError ?? "sin detalle"}. " +
                "Revise que el disco externo tenga espacio y vuelva a intentarlo.");
        }

        try
        {
            File.Move(parcial, destino);
        }
        catch (IOException ex)
        {
            Borrar(parcial);
            throw new RespaldoException($"No se pudo guardar el respaldo en «{carpeta}»: {ex.Message}", ex);
        }

        progreso?.Report(new ProgresoRespaldo(100, "Respaldo terminado."));
        return new Respaldo
        {
            Archivo = Path.GetFileName(destino),
            Carpeta = carpeta,
            Bytes = new FileInfo(destino).Length,
            IniciadoEn = inicio,
            TerminadoEn = _ahora(),
        };
    }

    /// <summary>Ruta con la fecha y la hora; si ya hay un respaldo de ese minuto, se agrega -2, -3...</summary>
    private static string RutaLibre(string carpeta, DateTimeOffset fecha, string baseDatos)
    {
        var ruta = Path.Combine(carpeta, Respaldo.NombreArchivo(fecha, baseDatos));
        for (var n = 2; File.Exists(ruta); n++)
            ruta = Path.Combine(carpeta, Path.GetFileNameWithoutExtension(Respaldo.NombreArchivo(fecha, baseDatos)) + $"-{n}{Respaldo.Extension}");
        return ruta;
    }

    /// <summary>Tablas con datos que pg_dump va a copiar, para calcular el porcentaje.</summary>
    private async Task<int> ContarTablasAsync(CancellationToken ct)
    {
        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM pg_tables WHERE schemaname = 'public'", conexion);
        return (int)(long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private static void Borrar(string ruta)
    {
        try
        {
            if (File.Exists(ruta))
                File.Delete(ruta);
        }
        catch (IOException)
        {
            // El disco se desconectó: no hay nada que borrar.
        }
    }

    // pg_dump: dumping contents of table "public.producto"
    [GeneratedRegex("""dumping contents of table "?(?:public\.)?(?<tabla>[\w]+)"?""")]
    private static partial Regex TablaCopiada();
}
