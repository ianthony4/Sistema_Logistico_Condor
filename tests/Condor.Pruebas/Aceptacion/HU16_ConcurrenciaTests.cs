using Condor.Datos;
using Condor.Pruebas.Infraestructura;
using Npgsql;

namespace Condor.Pruebas.Aceptacion;

/// <summary>
/// HU-16: base de datos central y trabajo simultáneo en LAN (T-07).
/// Criterio: si dos (o tres) terminales confirman un comprobante de la misma
/// serie en el mismo segundo, cada una recibe un correlativo distinto y
/// ninguno se repite ni se salta.
/// </summary>
[Collection(BaseDatos.Nombre)]
public class HU16_ConcurrenciaTests
{
    private const int ComprobantesPorPc = 40;

    private readonly BaseDatosFixture _bd;

    public HU16_ConcurrenciaTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task Tres_terminales_emitiendo_a_la_vez_no_repiten_ni_saltan_numeros()
    {
        await using var local = new TresPC();
        var (serieId, codigo) = await CrearSerieAsync();
        var terminales = await Task.WhenAll(local.Pc.Select((pc, i) =>
            new TerminalRepositorio(pc).RegistrarAccesoAsync(DatosPrueba.NombreUnico($"PC-{i + 1}"))));

        // Las tres PC arrancan juntas, como en la hora punta.
        using var largada = new Barrier(3);
        var tareas = local.Pc.Select((pc, i) => Task.Run(async () =>
        {
            largada.SignalAndWait();
            for (var n = 0; n < ComprobantesPorPc; n++)
                await EmitirAsync(pc, serieId, codigo, terminales[i].Id);
        }));
        await Task.WhenAll(tareas);

        var numeros = await NumerosAsync(serieId);
        var total = 3 * ComprobantesPorPc;
        Assert.Equal(total, numeros.Count);
        Assert.Equal(total, numeros.Distinct().Count());                    // ninguno repetido
        Assert.Equal(Enumerable.Range(1, total).Select(x => (long)x), numeros); // ninguno saltado
        Assert.Equal(3L, await TerminalesQueEmitieronAsync(serieId));
    }

    [FactConBD]
    public async Task Si_una_PC_falla_al_guardar_la_otra_recibe_ese_mismo_numero()
    {
        await using var local = new TresPC();
        var (_, codigo) = await CrearSerieAsync();

        // PC-1 toma el número 1 pero aún no confirma: la fila queda bloqueada.
        await using var conexion1 = await local.Pc[0].AbrirAsync();
        var tx1 = await conexion1.BeginTransactionAsync();
        Assert.Equal(1, await SerieRepositorio.SiguienteCorrelativoAsync(conexion1, tx1, codigo));

        // PC-2 pide número y queda esperando a la PC-1.
        var pc2 = new SerieRepositorio(local.Pc[1]).SiguienteCorrelativoAsync(codigo);
        await Task.Delay(300);
        Assert.False(pc2.IsCompleted);

        // La PC-1 falla (por ejemplo, se cae la red) y su transacción se deshace.
        await tx1.RollbackAsync();

        Assert.Equal(1, await pc2);
    }

    private static async Task EmitirAsync(FabricaConexiones pc, int serieId, string codigo, int terminalId)
    {
        await using var conexion = await pc.AbrirAsync();
        await using var tx = await conexion.BeginTransactionAsync();
        var numero = await SerieRepositorio.SiguienteCorrelativoAsync(conexion, tx, codigo);

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO comprobante (serie_id, numero, terminal_id, op_gravada, igv, total)
            VALUES (@s, @n, @t, 8.47, 1.53, 10.00)
            """, conexion, tx);
        cmd.Parameters.AddWithValue("s", serieId);
        cmd.Parameters.AddWithValue("n", numero);
        cmd.Parameters.AddWithValue("t", terminalId);
        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    private async Task<(int Id, string Codigo)> CrearSerieAsync()
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO serie (tipo_comprobante, codigo)
            SELECT '03', 'Y' || lpad(n::text, 3, '0')
            FROM generate_series(1, 999) AS n
            WHERE NOT EXISTS (SELECT 1 FROM serie WHERE codigo = 'Y' || lpad(n::text, 3, '0'))
            LIMIT 1
            RETURNING id, codigo
            """, conexion);
        await using var lector = await cmd.ExecuteReaderAsync();
        await lector.ReadAsync();
        return (lector.GetInt32(0), lector.GetString(1));
    }

    private async Task<List<long>> NumerosAsync(int serieId)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand("SELECT numero FROM comprobante WHERE serie_id = @s ORDER BY numero", conexion);
        cmd.Parameters.AddWithValue("s", serieId);
        var numeros = new List<long>();
        await using var lector = await cmd.ExecuteReaderAsync();
        while (await lector.ReadAsync())
            numeros.Add(lector.GetInt64(0));
        return numeros;
    }

    private async Task<long> TerminalesQueEmitieronAsync(int serieId)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(DISTINCT terminal_id) FROM comprobante WHERE serie_id = @s", conexion);
        cmd.Parameters.AddWithValue("s", serieId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
