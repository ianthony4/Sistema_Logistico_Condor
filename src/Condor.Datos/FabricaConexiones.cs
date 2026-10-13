using Npgsql;

namespace Condor.Datos;

/// <summary>
/// Punto único para abrir conexiones a la base de datos central. Usa un
/// NpgsqlDataSource, que reutiliza las conexiones (pool) entre operaciones.
/// </summary>
public sealed class FabricaConexiones : IAsyncDisposable
{
    private readonly NpgsqlDataSource _origen;

    public FabricaConexiones(string cadenaConexion)
    {
        CadenaConexion = cadenaConexion;
        _origen = NpgsqlDataSource.Create(cadenaConexion);
    }

    public FabricaConexiones(ConfiguracionConexion config, string? terminal = null)
        : this(config.CadenaConexion(terminal))
    {
    }

    public string CadenaConexion { get; }

    /// <summary>Abre una conexión del pool. Quien la recibe debe liberarla (await using).</summary>
    public async Task<NpgsqlConnection> AbrirAsync(CancellationToken ct = default)
    {
        try
        {
            return await _origen.OpenConnectionAsync(ct);
        }
        catch (Exception ex) when (EsFallaDeConexion(ex))
        {
            throw new SinConexionException(ex);
        }
    }

    /// <summary>True si la base de datos responde; no lanza excepciones.</summary>
    public async Task<bool> ProbarAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conexion = await AbrirAsync(ct);
            await using var cmd = new NpgsqlCommand("SELECT 1", conexion);
            await cmd.ExecuteScalarAsync(ct);
            return true;
        }
        catch (SinConexionException)
        {
            return false;
        }
    }

    /// <summary>Distingue una caída de red o del servidor de un error de SQL.</summary>
    public static bool EsFallaDeConexion(Exception ex) => ex switch
    {
        SinConexionException => true,
        NpgsqlException { IsTransient: true } => true,
        System.Net.Sockets.SocketException => true,
        TimeoutException => true,
        IOException => true,
        _ when ex.InnerException is not null => EsFallaDeConexion(ex.InnerException),
        _ => false,
    };

    public ValueTask DisposeAsync() => _origen.DisposeAsync();
}

/// <summary>
/// La PC principal no responde: red caída, PC apagada o servicio detenido.
/// </summary>
public sealed class SinConexionException : Exception
{
    public SinConexionException(Exception causa)
        : base("No hay conexión con la base de datos central (PC principal).", causa)
    {
    }
}
