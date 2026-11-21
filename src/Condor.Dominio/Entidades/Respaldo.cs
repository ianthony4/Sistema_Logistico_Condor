using System.Globalization;

namespace Condor.Dominio.Entidades;

/// <summary>
/// Respaldo manual de la base de datos central en el disco externo (HU-15).
/// El archivo lo genera pg_dump en la PC principal; la base solo anota
/// dónde quedó, cuánto pesa y cuándo se hizo.
/// </summary>
public sealed class Respaldo
{
    /// <summary>Extensión de los respaldos (formato personalizado de pg_dump, se restaura con pg_restore).</summary>
    public const string Extension = ".backup";

    public long Id { get; set; }

    /// <summary>Nombre del archivo, con la fecha y la hora: condor_2026-11-16_18-30.backup.</summary>
    public string Archivo { get; set; } = string.Empty;

    /// <summary>Carpeta del disco externo donde quedó (E:\Respaldos Condor).</summary>
    public string Carpeta { get; set; } = string.Empty;

    public long Bytes { get; set; }

    public DateTimeOffset IniciadoEn { get; set; }

    public DateTimeOffset TerminadoEn { get; set; }

    public int TerminalId { get; set; }

    /// <summary>PC que lo generó (la principal, que tiene el disco externo).</summary>
    public string? TerminalNombre { get; set; }

    public string Ruta => Path.Combine(Carpeta, Archivo);

    /// <summary>Tamaño legible: 845 KB, 12.4 MB.</summary>
    public string Tamano => Bytes switch
    {
        < 1024 * 1024 => $"{Math.Max(1, Bytes / 1024)} KB",
        _ => (Bytes / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " MB",
    };

    /// <summary>
    /// Nombre del archivo con la fecha y la hora locales (criterio 1 de HU-15).
    /// Sin ":" porque Windows no lo admite en un nombre de archivo.
    /// </summary>
    public static string NombreArchivo(DateTimeOffset fecha, string baseDatos = "condor") =>
        $"{baseDatos}_{fecha.ToString("yyyy-MM-dd_HH-mm", CultureInfo.InvariantCulture)}{Extension}";
}
