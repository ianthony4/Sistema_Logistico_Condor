namespace Condor.Dominio.Entidades;

/// <summary>
/// PC del local desde la que se usa el sistema (PC-1, PC-2, PC-3).
/// </summary>
public sealed class Terminal
{
    public int Id { get; set; }

    /// <summary>Nombre del equipo en la red (Environment.MachineName).</summary>
    public string Nombre { get; set; } = string.Empty;

    public DateTimeOffset RegistradoEn { get; set; }

    public DateTimeOffset UltimoAcceso { get; set; }
}
