using System.Text.Json;
using Condor.Dominio.Impresion;

namespace Condor.Impresion;

/// <summary>
/// Sección "Ticketera" de condor.json (HU-12, T-32). Cada PC indica el nombre
/// de su ticketera en Windows; si lo deja vacío, el ticket se muestra en
/// pantalla (útil en las PC sin ticketera y para las pruebas).
/// </summary>
public sealed class ConfiguracionTicketera
{
    public const string NombreArchivo = "condor.json";
    public const string Seccion = "Ticketera";

    private static readonly JsonSerializerOptions OpcionesJson = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Nombre de la impresora tal como aparece en Configuración > Impresoras de Windows.</summary>
    public string? Nombre { get; set; }

    /// <summary>Caracteres por línea: 48 en 80 mm (fuente A); 42 o 32 en papeles más angostos.</summary>
    public int Columnas { get; set; } = FormatoTicket.ColumnasPorDefecto;

    /// <summary>Segundos que se espera a que salga el ticket antes de avisar que falló.</summary>
    public int EsperaSegundos { get; set; } = 10;

    public bool Configurada => !string.IsNullOrWhiteSpace(Nombre);

    /// <summary>
    /// Lee la sección Ticketera de condor.json. Si el archivo o la sección no
    /// existen, devuelve la configuración por defecto (vista previa).
    /// </summary>
    /// <exception cref="InvalidDataException">Si la sección está mal escrita o fuera de rango.</exception>
    public static ConfiguracionTicketera Cargar(string? carpeta = null)
    {
        var ruta = Path.Combine(carpeta ?? AppContext.BaseDirectory, NombreArchivo);
        if (!File.Exists(ruta))
            return new ConfiguracionTicketera();

        ConfiguracionTicketera? config = null;
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
                    config = propiedad.Value.Deserialize<ConfiguracionTicketera>(OpcionesJson);
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"La sección {Seccion} de {NombreArchivo} no es válida: {ex.Message}", ex);
        }

        config ??= new ConfiguracionTicketera();
        config.Validar();
        return config;
    }

    public void Validar()
    {
        if (Columnas is < 32 or > 64)
            throw new InvalidDataException($"{Seccion} > Columnas debe estar entre 32 y 64 (48 para papel de 80 mm).");
        if (EsperaSegundos is < 1 or > 120)
            throw new InvalidDataException($"{Seccion} > EsperaSegundos debe estar entre 1 y 120.");
    }

    public override string ToString() => Configurada ? Nombre! : "vista previa";
}
