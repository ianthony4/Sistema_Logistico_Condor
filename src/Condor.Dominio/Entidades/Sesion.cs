namespace Condor.Dominio.Entidades;

/// <summary>
/// Perfiles de usuario. El sistema tiene un único perfil, sin credenciales (RF-14).
/// </summary>
public static class Perfiles
{
    public const string Maestro = "MAESTRO";
}

/// <summary>
/// Sesión de trabajo en una PC: siempre con el perfil MAESTRO y la terminal
/// desde la que se abrió el sistema (HU-14).
/// </summary>
public sealed class Sesion
{
    public Sesion(Terminal terminal) => Terminal = terminal;

    public Terminal Terminal { get; }

    public string Usuario => Perfiles.Maestro;

    /// <summary>Sesión que se abre al instante, antes de confirmar la terminal con la base de datos.</summary>
    public static Sesion Local(string nombreEquipo) => new(new Terminal { Nombre = nombreEquipo });

    /// <summary>True cuando la terminal ya quedó registrada en la base de datos central.</summary>
    public bool Registrada => Terminal.Id > 0;
}
