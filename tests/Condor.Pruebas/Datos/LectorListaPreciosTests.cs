using Condor.Datos;
using Condor.Datos.Importacion;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Datos;

[Collection(BaseDatos.Nombre)]
public class LectorListaPreciosTests
{
    private readonly BaseDatosFixture _bd;

    public LectorListaPreciosTests(BaseDatosFixture bd) => _bd = bd;

    [Fact]
    public void Lee_el_formato_de_Excel_en_espanol()
    {
        var filas = LectorListaPrecios.Leer(new[]
        {
            "nombre;precio;unidad",
            "Cemento Portland tipo I x 42.5 kg;32,50;bls",
            "Brocha 4 in;12.00",
        });

        Assert.All(filas, f => Assert.True(f.EsValida, f.Error));
        Assert.Equal(32.50m, filas[0].Producto!.Precio);
        Assert.Equal("BLS", filas[0].Producto!.Unidad);
        Assert.Equal("UND", filas[1].Producto!.Unidad);
    }

    [Fact]
    public void Acepta_coma_como_separador_y_comillas()
    {
        var filas = LectorListaPrecios.Leer(new[]
        {
            "nombre,precio,unidad",
            "\"Tubo PVC agua 1/2\"\" x 5 m\",9.80,UND",
        });

        Assert.Equal("Tubo PVC agua 1/2\" x 5 m", Assert.Single(filas).Producto!.Nombre);
    }

    [Fact]
    public void Las_filas_con_error_indican_su_numero_de_linea()
    {
        var filas = LectorListaPrecios.Leer(new[]
        {
            "nombre;precio;unidad",
            "Martillo;25.00;UND",
            ";10;UND",
            "Serrucho;veinte;UND",
            "Pala;0;UND",
            "",
            "Solo nombre",
        });

        Assert.Equal(new[] { 3, 4, 5, 7 }, filas.Where(f => !f.EsValida).Select(f => f.Linea));
        Assert.Single(filas, f => f.EsValida);
    }

    [Fact]
    public void La_lista_de_precios_del_repositorio_no_tiene_errores()
    {
        var ruta = Path.Combine(RaizRepositorio(), "db", "datos", "lista_precios.csv");

        var filas = LectorListaPrecios.LeerArchivo(ruta);

        Assert.True(filas.Count >= 50);
        Assert.All(filas, f => Assert.True(f.EsValida, $"Línea {f.Linea}: {f.Error}"));
        Assert.Equal(filas.Count, filas.Select(f => f.Producto!.Nombre.ToLowerInvariant()).Distinct().Count());
    }

    [FactConBD]
    public async Task Cargar_dos_veces_no_duplica_productos()
    {
        var repo = new ProductoRepositorio(_bd.Conexiones);
        var marca = DatosPrueba.NombreUnico("Carga");
        var lineas = new[] { "nombre;precio", $"{marca} A;1.00", $"{marca} B;2.00" };

        for (var vez = 0; vez < 2; vez++)
        {
            foreach (var fila in LectorListaPrecios.Leer(lineas))
            {
                try { await repo.RegistrarAsync(fila.Producto!); }
                catch (Condor.Dominio.Validacion.ProductoDuplicadoException) { }
            }
        }

        Assert.Equal(2, (await repo.BuscarPorNombreAsync(marca)).Count);
    }

    private static string RaizRepositorio()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CondorVentas.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("No se encontró CondorVentas.sln.");
    }
}
