using System.Text.Json;

namespace Condor.Datos.Respaldos;

/// <summary>
/// Sección "Respaldo" de condor.json (HU-15, T-57): la carpeta del disco
/// externo, dónde está pg_dump y cada cuántas horas se avisa. Solo la llena
/// la PC principal; en las demás el botón Generar respaldo explica que se
/// hace desde la principal.
/// </summary>
public sealed class ConfiguracionRespaldo
{
    public const string NombreArchivo = "condor.json";
    public const string Seccion = "Respaldo";

    private static readonly JsonSerializerOptions OpcionesJson = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Carpeta del disco externo (E:\Respaldos Condor). Vacía: esta PC no genera respaldos.</summary>
    public string Carpeta { get; set; } = string.Empty;

    /// <summary>Ruta de pg_dump.exe; vacía para buscar el de PostgreSQL instalado.</summary>
    public string PgDump { get; set; } = string.Empty;

    /// <summary>Si el último respaldo tiene más de estas horas, se avisa al abrir el sistema (criterio 2).</summary>
    public int AvisoHoras { get; set; } = 24;

    public bool Configurada => !string.IsNullOrWhiteSpace(Carpeta);

    /// <summary>
    /// Lee la sección Respaldo de condor.json. Si el archivo o la sección no
    /// existen, devuelve una configuración vacía (esta PC no genera respaldos).
    /// </summary>
    /// <exception cref="InvalidDataException">Si la sección está mal escrita o fuera de rango.</exception>
    public static ConfiguracionRespaldo Cargar(string? carpeta = null)
    {
        var ruta = Path.Combine(carpeta ?? AppContext.BaseDirectory, NombreArchivo);
        if (!File.Exists(ruta))
            return new ConfiguracionRespaldo();

        ConfiguracionRespaldo? config = null;
        try
        {
            using var documento = JsonDocument.Parse(File.ReadAllText(ruta), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            foreach (var propiedad in documento.RootElement.EnumerateObject())
            {
                if (string.Equals(propiedad.Name, Seccion, StringComparison.OrdinalIgnoreCase))
                    config = propiedad.Value.Deserialize<ConfiguracionRespaldo>(OpcionesJson);
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"La sección {Seccion} de {NombreArchivo} no es válida: {ex.Message}", ex);
        }

        config ??= new ConfiguracionRespaldo();
        config.Validar();
        return config;
    }

    public void Validar()
    {
        if (AvisoHoras is < 1 or > 168)
            throw new InvalidDataException($"{Seccion} > AvisoHoras debe estar entre 1 y 168 (24 = un día).");
        if (Carpeta.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            throw new InvalidDataException($"{Seccion} > Carpeta tiene caracteres no válidos.");
    }

    public override string ToString() => Configurada ? Carpeta : "sin disco de respaldo";
}
