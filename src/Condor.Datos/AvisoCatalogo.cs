using Npgsql;

namespace Condor.Datos;

/// <summary>
/// Escucha el canal 'catalogo' de PostgreSQL (LISTEN) para enterarse al
/// instante de los productos que se registran en otra PC (T-13).
/// </summary>
/// <remarks>
/// Usa una conexión propia, fuera del pool, que queda abierta mientras el
/// programa está abierto. Si la red se cae, reintenta cada pocos segundos.
/// El evento se dispara en un hilo de fondo: la interfaz debe pasarlo a su hilo.
/// </remarks>
public sealed class AvisoCatalogo : IAsyncDisposable
{
    public const string Canal = "catalogo";

    private readonly string _cadenaConexion;
    private readonly CancellationTokenSource _detener = new();
    private Task? _escucha;

    public AvisoCatalogo(string cadenaConexion)
    {
        // Conexión dedicada: no debe volver al pool mientras escucha.
        _cadenaConexion = new NpgsqlConnectionStringBuilder(cadenaConexion)
        {
            Pooling = false,
            KeepAlive = 30,
        }.ConnectionString;
    }

    /// <summary>Se dispara cuando otra PC (o esta misma) cambia el catálogo.</summary>
    public event Action? CatalogoCambiado;

    /// <summary>True mientras la escucha está conectada.</summary>
    public bool Conectado { get; private set; }

    public TimeSpan EsperaReintento { get; set; } = TimeSpan.FromSeconds(5);

    public void Iniciar() => _escucha ??= Task.Run(() => EscucharAsync(_detener.Token));

    private async Task EscucharAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var conexion = new NpgsqlConnection(_cadenaConexion);
                await conexion.OpenAsync(ct);
                conexion.Notification += (_, e) =>
                {
                    if (e.Channel == Canal)
                        CatalogoCambiado?.Invoke();
                };

                await using (var cmd = new NpgsqlCommand($"LISTEN {Canal}", conexion))
                    await cmd.ExecuteNonQueryAsync(ct);
                Conectado = true;

                while (!ct.IsCancellationRequested)
                    await conexion.WaitAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // Red caída o PC principal apagada: se reintenta más tarde.
                Conectado = false;
                try
                {
                    await Task.Delay(EsperaReintento, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        Conectado = false;
    }

    public async ValueTask DisposeAsync()
    {
        _detener.Cancel();
        if (_escucha is not null)
        {
            try
            {
                await _escucha;
            }
            catch (OperationCanceledException)
            {
            }
        }
        _detener.Dispose();
    }
}
