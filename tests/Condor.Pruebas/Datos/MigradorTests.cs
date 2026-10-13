using Condor.Datos;
using Condor.Pruebas.Infraestructura;
using Npgsql;

namespace Condor.Pruebas.Datos;

public class MigradorTests
{
    [Fact]
    public void Las_migraciones_estan_incrustadas_y_en_orden()
    {
        var migraciones = Migrador.Disponibles();

        Assert.True(migraciones.Count >= 2);
        Assert.Equal(1, migraciones[0].Version);
        Assert.Equal("esquema_inicial", migraciones[0].Nombre);
        Assert.Equal(migraciones.Select(m => m.Version).Order(), migraciones.Select(m => m.Version));
        Assert.All(migraciones, m => Assert.False(string.IsNullOrWhiteSpace(m.Sql)));
    }

    [FactConBD]
    public async Task Aplicar_dos_veces_no_repite_las_migraciones()
    {
        await using var fabrica = new FabricaConexiones(Environment.GetEnvironmentVariable(FactConBDAttribute.Variable)!);
        var migrador = new Migrador(fabrica);

        await migrador.AplicarAsync();
        var segundaVez = await migrador.AplicarAsync();

        Assert.Equal(0, segundaVez);

        await using var conexion = await fabrica.AbrirAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM serie WHERE codigo IN ('B001', 'F001')", conexion);
        Assert.Equal(2L, await cmd.ExecuteScalarAsync());
    }

    [FactConBD]
    public async Task Dos_PC_que_migran_a_la_vez_no_chocan()
    {
        await using var fabrica = new FabricaConexiones(Environment.GetEnvironmentVariable(FactConBDAttribute.Variable)!);

        var resultados = await Task.WhenAll(
            new Migrador(fabrica).AplicarAsync(),
            new Migrador(fabrica).AplicarAsync());

        Assert.All(resultados, r => Assert.True(r >= 0));
    }
}
