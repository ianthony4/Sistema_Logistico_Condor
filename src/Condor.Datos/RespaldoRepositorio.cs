using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Npgsql;

namespace Condor.Datos;

/// <summary>
/// Registro de los respaldos manuales en la base de datos central (HU-15,
/// T-60). Las tres PC leen de aquí la fecha del último respaldo para el
/// aviso de las 24 horas.
/// </summary>
public sealed class RespaldoRepositorio : IRespaldoRepositorio
{
    private const string Consulta = """
        SELECT r.id, r.archivo, r.carpeta, r.bytes, r.iniciado_en, r.terminado_en, r.terminal_id, t.nombre
        FROM respaldo r
        JOIN terminal t ON t.id = r.terminal_id
        """;

    private readonly FabricaConexiones _conexiones;

    public RespaldoRepositorio(FabricaConexiones conexiones) => _conexiones = conexiones;

    public async Task<Respaldo> RegistrarAsync(Respaldo respaldo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(respaldo.Archivo) || respaldo.Bytes <= 0)
            throw new ArgumentException("El respaldo no tiene archivo o está vacío.", nameof(respaldo));
        if (respaldo.TerminalId <= 0)
            throw new InvalidOperationException("Esta PC aún no está registrada en la base de datos central.");

        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO respaldo (archivo, carpeta, bytes, iniciado_en, terminado_en, terminal_id)
            VALUES (@archivo, @carpeta, @bytes, @iniciado, @terminado, @terminal)
            RETURNING id
            """, conexion);
        cmd.Parameters.AddWithValue("archivo", respaldo.Archivo);
        cmd.Parameters.AddWithValue("carpeta", respaldo.Carpeta);
        cmd.Parameters.AddWithValue("bytes", respaldo.Bytes);
        cmd.Parameters.AddWithValue("iniciado", respaldo.IniciadoEn.ToUniversalTime());
        cmd.Parameters.AddWithValue("terminado", respaldo.TerminadoEn.ToUniversalTime());
        cmd.Parameters.AddWithValue("terminal", respaldo.TerminalId);
        respaldo.Id = (long)(await cmd.ExecuteScalarAsync(ct))!;
        return respaldo;
    }

    public async Task<Respaldo?> UltimoAsync(CancellationToken ct = default) =>
        (await ListarAsync(1, ct)).FirstOrDefault();

    public async Task<IReadOnlyList<Respaldo>> ListarAsync(int limite = 10, CancellationToken ct = default)
    {
        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand($"{Consulta} ORDER BY r.terminado_en DESC, r.id DESC LIMIT @limite", conexion);
        cmd.Parameters.AddWithValue("limite", Math.Clamp(limite, 1, 100));

        var respaldos = new List<Respaldo>();
        await using var lector = await cmd.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
        {
            respaldos.Add(new Respaldo
            {
                Id = lector.GetInt64(0),
                Archivo = lector.GetString(1),
                Carpeta = lector.GetString(2),
                Bytes = lector.GetInt64(3),
                IniciadoEn = lector.GetFieldValue<DateTimeOffset>(4),
                TerminadoEn = lector.GetFieldValue<DateTimeOffset>(5),
                TerminalId = lector.GetInt32(6),
                TerminalNombre = lector.GetString(7),
            });
        }
        return respaldos;
    }
}
