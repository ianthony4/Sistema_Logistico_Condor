using Condor.Dominio.Contratos;
using Condor.Dominio.Sunat;
using Npgsql;

namespace Condor.Datos;

/// <summary>
/// Respuestas de la SUNAT en la base de datos central (HU-07, T-45): el
/// estado en comprobante.estado_sunat y el XML firmado, el CDR y los
/// intentos en comprobante_sunat.
/// </summary>
/// <remarks>
/// Una vez que la SUNAT respondió algo definitivo (aceptado, observado o
/// rechazado) el estado ya no cambia: si otra PC reenvía el mismo
/// comprobante, su respuesta no pisa la primera.
/// </remarks>
public sealed class EnvioSunatRepositorio : IEnvioSunatRepositorio
{
    private readonly FabricaConexiones _conexiones;

    public EnvioSunatRepositorio(FabricaConexiones conexiones) => _conexiones = conexiones;

    public async Task RegistrarRespuestaAsync(long comprobanteId, RespuestaSunat respuesta, CancellationToken ct = default)
    {
        var definitiva = EstadoSunat.EsDefinitivo(respuesta.Estado);

        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var tx = await conexion.BeginTransactionAsync(ct);

        await using (var cmd = new NpgsqlCommand(
            """
            UPDATE comprobante SET estado_sunat = @estado
            WHERE id = @id AND estado_sunat = 'PENDIENTE'
            """, conexion, tx))
        {
            cmd.Parameters.AddWithValue("id", comprobanteId);
            cmd.Parameters.AddWithValue("estado", respuesta.Estado);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO comprobante_sunat AS cs
                (comprobante_id, nombre_archivo, hash, xml_firmado, cdr_zip, codigo, descripcion, notas,
                 intentos, ultimo_intento, respondido_en, ultimo_error)
            VALUES (@id, @archivo, @hash, @xml, @cdr, @codigo, @descripcion, @notas,
                    1, now(), CASE WHEN @definitiva THEN now() END, CASE WHEN @definitiva THEN NULL ELSE @descripcion END)
            ON CONFLICT (comprobante_id) DO UPDATE SET
                intentos       = cs.intentos + 1,
                ultimo_intento = now(),
                nombre_archivo = coalesce(EXCLUDED.nombre_archivo, cs.nombre_archivo),
                hash           = coalesce(EXCLUDED.hash, cs.hash),
                xml_firmado    = coalesce(EXCLUDED.xml_firmado, cs.xml_firmado),
                cdr_zip        = CASE WHEN cs.respondido_en IS NULL THEN coalesce(EXCLUDED.cdr_zip, cs.cdr_zip) ELSE cs.cdr_zip END,
                codigo         = CASE WHEN cs.respondido_en IS NULL THEN EXCLUDED.codigo ELSE cs.codigo END,
                descripcion    = CASE WHEN cs.respondido_en IS NULL THEN EXCLUDED.descripcion ELSE cs.descripcion END,
                notas          = CASE WHEN cs.respondido_en IS NULL THEN EXCLUDED.notas ELSE cs.notas END,
                respondido_en  = coalesce(cs.respondido_en, EXCLUDED.respondido_en),
                ultimo_error   = CASE WHEN @definitiva OR cs.respondido_en IS NOT NULL THEN NULL ELSE @descripcion END
            """, conexion, tx))
        {
            cmd.Parameters.AddWithValue("id", comprobanteId);
            cmd.Parameters.AddWithValue("archivo", Nulo(respuesta.NombreArchivo));
            cmd.Parameters.AddWithValue("hash", Nulo(respuesta.Hash));
            cmd.Parameters.AddWithValue("xml", Nulo(respuesta.XmlFirmado));
            cmd.Parameters.AddWithValue("cdr", (object?)respuesta.CdrZip ?? DBNull.Value);
            cmd.Parameters.AddWithValue("codigo", Nulo(Recortar(respuesta.Codigo, 10)));
            cmd.Parameters.AddWithValue("descripcion", Nulo(Recortar(respuesta.Descripcion, 500)));
            cmd.Parameters.AddWithValue("notas", respuesta.Notas.Count == 0 ? DBNull.Value : string.Join("\n", respuesta.Notas));
            cmd.Parameters.AddWithValue("definitiva", definitiva);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task RegistrarFallaAsync(long comprobanteId, string motivo, CancellationToken ct = default)
    {
        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO comprobante_sunat AS cs (comprobante_id, intentos, ultimo_intento, ultimo_error)
            VALUES (@id, 1, now(), @motivo)
            ON CONFLICT (comprobante_id) DO UPDATE SET
                intentos       = cs.intentos + 1,
                ultimo_intento = now(),
                ultimo_error   = CASE WHEN cs.respondido_en IS NULL THEN EXCLUDED.ultimo_error ELSE cs.ultimo_error END
            """, conexion);
        cmd.Parameters.AddWithValue("id", comprobanteId);
        cmd.Parameters.AddWithValue("motivo", Recortar(motivo, 500));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<long>> ListarPendientesAsync(int limite = 100, CancellationToken ct = default)
    {
        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT c.id
            FROM comprobante c
            JOIN serie s ON s.id = c.serie_id
            WHERE c.estado_sunat = 'PENDIENTE' AND s.tipo_comprobante IN ('01', '03')
            ORDER BY c.id
            LIMIT @limite
            """, conexion);
        cmd.Parameters.AddWithValue("limite", Math.Clamp(limite, 1, 1000));

        var ids = new List<long>();
        await using var lector = await cmd.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
            ids.Add(lector.GetInt64(0));
        return ids;
    }

    private static object Nulo(string? texto) => string.IsNullOrEmpty(texto) ? DBNull.Value : texto;

    private static string Recortar(string? texto, int largo) =>
        texto is null ? string.Empty : texto.Length <= largo ? texto : texto[..largo];
}
