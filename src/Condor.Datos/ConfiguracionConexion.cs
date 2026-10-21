using System.Text.Json;
using Npgsql;

namespace Condor.Datos;

/// <summary>
/// Datos de conexión a la base de datos central. Se leen de condor.json, junto
/// al ejecutable, para que cada PC apunte a la IP de la PC principal sin
/// recompilar (T-04).
/// </summary>
public sealed class ConfiguracionConexion
{
    public const string NombreArchivo = "condor.json";

    /// <summary>IP o nombre de la PC principal. En la propia PC principal: localhost.</summary>
    public string Servidor { get; set; } = "localhost";

    public int Puerto { get; set; } = 5432;

    public string BaseDatos { get; set; } = "condor";

    public string Usuario { get; set; } = "condor_app";

    public string Clave { get; set; } = string.Empty;

    /// <summary>Segundos de espera al conectar; bajo para avisar pronto si la red se cae.</summary>
    public int TiempoEsperaSegundos { get; set; } = 5;

    private static readonly JsonSerializerOptions OpcionesJson = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Lee condor.json de la carpeta indicada (por defecto, la del ejecutable).</summary>
    /// <exception cref="FileNotFoundException">Si el archivo no existe.</exception>
    /// <exception cref="InvalidDataException">Si el archivo está mal escrito o incompleto.</exception>
    public static ConfiguracionConexion Cargar(string? carpeta = null)
    {
        var ruta = Path.Combine(carpeta ?? AppContext.BaseDirectory, NombreArchivo);
        if (!File.Exists(ruta))
            throw new FileNotFoundException(
                $"No se encontró {NombreArchivo}. Copie condor.ejemplo.json como {NombreArchivo} y complete la IP de la PC principal y la clave.",
                ruta);

        ConfiguracionConexion? config;
        try
        {
            config = JsonSerializer.Deserialize<ConfiguracionConexion>(File.ReadAllText(ruta), OpcionesJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{NombreArchivo} no tiene un formato JSON válido: {ex.Message}", ex);
        }

        if (config is null)
            throw new InvalidDataException($"{NombreArchivo} está vacío.");

        config.Validar();
        return config;
    }

    public void Validar()
    {
        if (string.IsNullOrWhiteSpace(Servidor))
            throw new InvalidDataException("Falta 'Servidor' en condor.json.");
        if (Puerto is < 1 or > 65535)
            throw new InvalidDataException("'Puerto' debe estar entre 1 y 65535.");
        if (string.IsNullOrWhiteSpace(BaseDatos) || string.IsNullOrWhiteSpace(Usuario))
            throw new InvalidDataException("Faltan 'BaseDatos' o 'Usuario' en condor.json.");
    }

    /// <summary>Cadena de conexión de Npgsql. El nombre de la aplicación identifica la terminal en pg_stat_activity.</summary>
    public string CadenaConexion(string? terminal = null)
    {
        var cadena = new NpgsqlConnectionStringBuilder
        {
            Host = Servidor,
            Port = Puerto,
            Database = BaseDatos,
            Username = Usuario,
            Password = Clave,
            Timeout = TiempoEsperaSegundos,
            CommandTimeout = 15,
            Pooling = true,
            MaxPoolSize = 10,
            ApplicationName = terminal is null ? "CondorVentas" : $"CondorVentas@{terminal}",
        };
        return cadena.ConnectionString;
    }

    public override string ToString() => $"{Servidor}:{Puerto}/{BaseDatos}";
}
