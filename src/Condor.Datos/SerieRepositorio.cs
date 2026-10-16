using Condor.Dominio.Contratos;
using Npgsql;

namespace Condor.Datos;

/// <summary>
/// Genera el número correlativo de cada serie en la base de datos central
/// (HU-16, T-05).
/// </summary>
/// <remarks>
/// <c>UPDATE ... SET ultimo_correlativo = ultimo_correlativo + 1 ... RETURNING</c>
/// bloquea la fila de la serie hasta que termina la transacción. Si dos
/// terminales piden número en el mismo instante, la segunda espera a que la
/// primera confirme y recibe el número siguiente: nunca se repite.
/// Cuando el número se pide dentro de la transacción que guarda el comprobante
/// (Sprint 2), un error al guardar deshace también el incremento, así que
/// tampoco quedan números saltados.
/// </remarks>
public sealed class SerieRepositorio : ISerieRepositorio
{
    private const string Sql = """
        UPDATE serie
        SET ultimo_correlativo = ultimo_correlativo + 1
        WHERE codigo = @codigo
        RETURNING ultimo_correlativo
        """;

    private readonly FabricaConexiones _conexiones;

    public SerieRepositorio(FabricaConexiones conexiones) => _conexiones = conexiones;

    /// <summary>Reserva el siguiente número en una transacción propia.</summary>
    public async Task<long> SiguienteCorrelativoAsync(string codigoSerie, CancellationToken ct = default)
    {
        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var tx = await conexion.BeginTransactionAsync(ct);
        var numero = await SiguienteCorrelativoAsync(conexion, tx, codigoSerie, ct);
        await tx.CommitAsync(ct);
        return numero;
    }

    /// <summary>
    /// Reserva el siguiente número dentro de una transacción existente: la fila
    /// de la serie queda bloqueada hasta el COMMIT o ROLLBACK de esa transacción.
    /// </summary>
    public static async Task<long> SiguienteCorrelativoAsync(
        NpgsqlConnection conexion, NpgsqlTransaction tx, string codigoSerie, CancellationToken ct = default)
    {
        codigoSerie = codigoSerie.Trim().ToUpperInvariant();

        await using var cmd = new NpgsqlCommand(Sql, conexion, tx);
        cmd.Parameters.AddWithValue("codigo", codigoSerie);
        try
        {
            var resultado = await cmd.ExecuteScalarAsync(ct);
            return resultado is long numero
                ? numero
                : throw new InvalidOperationException($"La serie {codigoSerie} no existe.");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.CheckViolation)
        {
            throw new InvalidOperationException(
                $"La serie {codigoSerie} llegó a 99999999. Hay que registrar una serie nueva en la SUNAT.", ex);
        }
    }
}
