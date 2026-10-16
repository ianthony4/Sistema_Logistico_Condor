using Condor.Datos;
using Condor.Pruebas.Infraestructura;
using Npgsql;

namespace Condor.Pruebas.Datos;

[Collection(BaseDatos.Nombre)]
public class SerieRepositorioTests
{
    private readonly BaseDatosFixture _bd;

    public SerieRepositorioTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task Cada_llamada_devuelve_el_numero_siguiente()
    {
        var serie = await CrearSerieAsync();
        var repo = new SerieRepositorio(_bd.Conexiones);

        var primero = await repo.SiguienteCorrelativoAsync(serie);
        var segundo = await repo.SiguienteCorrelativoAsync(serie);

        Assert.Equal(1, primero);
        Assert.Equal(2, segundo);
    }

    [FactConBD]
    public async Task Si_la_transaccion_se_deshace_el_numero_no_se_pierde()
    {
        var serie = await CrearSerieAsync();

        await using (var conexion = await _bd.Conexiones.AbrirAsync())
        await using (var tx = await conexion.BeginTransactionAsync())
        {
            Assert.Equal(1, await SerieRepositorio.SiguienteCorrelativoAsync(conexion, tx, serie));
            await tx.RollbackAsync();
        }

        // El 1 se vuelve a entregar: no quedan números saltados.
        Assert.Equal(1, await new SerieRepositorio(_bd.Conexiones).SiguienteCorrelativoAsync(serie));
    }

    [FactConBD]
    public async Task Serie_inexistente_da_un_error_claro()
    {
        var repo = new SerieRepositorio(_bd.Conexiones);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.SiguienteCorrelativoAsync("Z999"));
        Assert.Contains("Z999", ex.Message);
    }

    /// <summary>Serie nueva de pruebas (X + 3 dígitos) para no tocar B001 ni F001.</summary>
    private async Task<string> CrearSerieAsync()
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO serie (tipo_comprobante, codigo)
            SELECT 'NV', 'X' || lpad(n::text, 3, '0')
            FROM generate_series(1, 999) AS n
            WHERE NOT EXISTS (SELECT 1 FROM serie WHERE codigo = 'X' || lpad(n::text, 3, '0'))
            LIMIT 1
            RETURNING codigo
            """, conexion);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }
}
