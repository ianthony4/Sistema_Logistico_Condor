using Condor.Datos;
using Npgsql;

namespace Condor.Pruebas.Infraestructura;

/// <summary>
/// Ayudas para las pruebas del reenvío a la SUNAT (HU-17) en la base de
/// pruebas, donde quedan pendientes de otras pruebas y de ejecuciones
/// anteriores.
/// </summary>
public static class PendientesDePrueba
{
    /// <summary>
    /// Deja todos los pendientes que ya hay en la base apartados por otra PC
    /// durante un día: así la prueba solo reserva los que ella emite.
    /// </summary>
    public static async Task ApartarLosDeOtrasPruebasAsync(FabricaConexiones conexiones)
    {
        await using var conexion = await conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO comprobante_sunat AS cs (comprobante_id, reservado_hasta, reservado_por)
            SELECT id, now() + interval '1 day', 'PC-OTRA-PRUEBA' FROM comprobante WHERE estado_sunat = 'PENDIENTE'
            ON CONFLICT (comprobante_id) DO UPDATE SET
                reservado_hasta = EXCLUDED.reservado_hasta, reservado_por = EXCLUDED.reservado_por
            """, conexion);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Atrasa la emisión y el último intento del comprobante, como si hubiera pasado ese tiempo.</summary>
    public static async Task AtrasarAsync(FabricaConexiones conexiones, long comprobanteId, TimeSpan tiempo)
    {
        await using var conexion = await conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE comprobante SET fecha = fecha - make_interval(secs => @s) WHERE id = @id;
            UPDATE comprobante_sunat SET ultimo_intento = ultimo_intento - make_interval(secs => @s) WHERE comprobante_id = @id;
            """, conexion);
        cmd.Parameters.AddWithValue("id", comprobanteId);
        cmd.Parameters.AddWithValue("s", tiempo.TotalSeconds);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>PC que tiene apartado el comprobante; null si nadie lo tiene apartado.</summary>
    public static async Task<string?> ReservadoPorAsync(FabricaConexiones conexiones, long comprobanteId)
    {
        await using var conexion = await conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT reservado_por FROM comprobante_sunat WHERE comprobante_id = @id AND reservado_hasta > now()", conexion);
        cmd.Parameters.AddWithValue("id", comprobanteId);
        return await cmd.ExecuteScalarAsync() as string;
    }
}
