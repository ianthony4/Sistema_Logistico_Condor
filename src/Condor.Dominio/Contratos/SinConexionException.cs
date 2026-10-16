namespace Condor.Dominio.Contratos;

/// <summary>
/// La PC principal no responde: red caída, PC apagada o servicio detenido.
/// Está en el dominio para que la interfaz la distinga de los demás errores
/// y conserve lo que el usuario estaba haciendo (HU-16, T-06).
/// </summary>
public sealed class SinConexionException : Exception
{
    public SinConexionException(Exception causa)
        : base("No hay conexión con la base de datos central (PC principal).", causa)
    {
    }
}
