using Condor.Datos;
using Condor.Pruebas.Infraestructura;
using Npgsql;

namespace Condor.Pruebas.Datos;

[Collection(BaseDatos.Nombre)]
public class ProductoBusquedaTests
{
    private readonly BaseDatosFixture _bd;

    public ProductoBusquedaTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task Busca_sin_distinguir_mayusculas_ni_tildes()
    {
        var repo = new ProductoRepositorio(_bd.Conexiones);
        var marca = DatosPrueba.NombreUnico("Pegamento");
        await repo.RegistrarAsync(DatosPrueba.Nuevo($"{marca} sintético PVC", 12.50m));

        var resultado = await repo.BuscarPorNombreAsync($"{marca.ToUpperInvariant()} SINTETICO");

        Assert.Single(resultado);
    }

    [FactConBD]
    public async Task Los_que_empiezan_por_el_texto_salen_primero()
    {
        var repo = new ProductoRepositorio(_bd.Conexiones);
        var marca = DatosPrueba.NombreUnico("Zq").Replace(" ", "");
        await repo.RegistrarAsync(DatosPrueba.Nuevo($"Tubo {marca}", 5m));
        await repo.RegistrarAsync(DatosPrueba.Nuevo($"{marca} codo", 3m));

        var resultado = await repo.BuscarPorNombreAsync(marca);

        Assert.Equal(2, resultado.Count);
        Assert.StartsWith(marca, resultado[0].Nombre);
    }

    [FactConBD]
    public async Task La_consulta_usa_el_indice_de_trigramas()
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using (var off = new NpgsqlCommand("SET enable_seqscan = off", conexion))
            await off.ExecuteNonQueryAsync();

        await using var cmd = new NpgsqlCommand(
            """
            EXPLAIN SELECT id FROM producto
            WHERE activo AND texto_busqueda(nombre) LIKE '%' || texto_busqueda('esmal') || '%'
            """, conexion);
        var plan = new List<string>();
        await using var lector = await cmd.ExecuteReaderAsync();
        while (await lector.ReadAsync())
            plan.Add(lector.GetString(0));

        Assert.Contains(plan, linea => linea.Contains("ix_producto_nombre_trgm"));
    }
}
