using Condor.Datos;
using Condor.Dominio.Validacion;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Datos;

/// <summary>Código de barras de los productos en la base central (HU-13, T-86).</summary>
[Collection(BaseDatos.Nombre)]
public class ProductoCodigoBarrasTests
{
    private readonly BaseDatosFixture _bd;

    public ProductoCodigoBarrasTests(BaseDatosFixture bd) => _bd = bd;

    private static string CodigoNuevo() => "77" + Random.Shared.NextInt64(10_000_000_000, 99_999_999_999);

    [FactConBD]
    public async Task Registra_el_producto_con_su_codigo_y_lo_encuentra_por_el_codigo()
    {
        var repo = new ProductoRepositorio(_bd.Conexiones);
        var codigo = CodigoNuevo();
        var producto = DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Silicona"), 18.90m);
        producto.CodigoBarras = codigo;

        await repo.RegistrarAsync(producto);
        var leido = await repo.BuscarPorCodigoBarrasAsync($" {codigo} ");

        Assert.Equal(producto.Id, leido!.Id);
        Assert.Equal(codigo, leido.CodigoBarras);
        Assert.Null(await repo.BuscarPorCodigoBarrasAsync(CodigoNuevo()));
    }

    [FactConBD]
    public async Task Asigna_y_quita_el_codigo_de_un_producto_existente()
    {
        var repo = new ProductoRepositorio(_bd.Conexiones);
        var producto = await repo.RegistrarAsync(DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Lija"), 2.50m));
        var codigo = "cond-" + Random.Shared.Next(100_000, 999_999);

        var conCodigo = await repo.AsignarCodigoBarrasAsync(producto.Id, codigo);
        var encontrado = await repo.BuscarPorCodigoBarrasAsync(codigo);
        var sinCodigo = await repo.AsignarCodigoBarrasAsync(producto.Id, null);

        Assert.Equal(codigo.ToUpperInvariant(), conCodigo.CodigoBarras);
        Assert.Equal(producto.Id, encontrado!.Id);
        Assert.Null(sinCodigo.CodigoBarras);
        Assert.Null(await repo.BuscarPorCodigoBarrasAsync(codigo));
    }

    [FactConBD]
    public async Task Un_codigo_no_puede_ser_de_dos_productos()
    {
        var repo = new ProductoRepositorio(_bd.Conexiones);
        var codigo = CodigoNuevo();
        var primero = DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Brocha"), 9.00m);
        primero.CodigoBarras = codigo;
        await repo.RegistrarAsync(primero);
        var segundo = await repo.RegistrarAsync(DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Rodillo"), 15.00m));
        var tercero = DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Espátula"), 6.00m);
        tercero.CodigoBarras = codigo;

        var alAsignar = await Assert.ThrowsAsync<CodigoBarrasDuplicadoException>(() => repo.AsignarCodigoBarrasAsync(segundo.Id, codigo));
        var alRegistrar = await Assert.ThrowsAsync<CodigoBarrasDuplicadoException>(() => repo.RegistrarAsync(tercero));

        Assert.Contains(primero.Nombre, alAsignar.Message);
        Assert.Contains(codigo, alRegistrar.Message);
    }

    [FactConBD]
    public async Task Un_codigo_invalido_no_se_asigna()
    {
        var repo = new ProductoRepositorio(_bd.Conexiones);
        var producto = await repo.RegistrarAsync(DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Tiza"), 1.00m));

        await Assert.ThrowsAsync<ProductoInvalidoException>(() => repo.AsignarCodigoBarrasAsync(producto.Id, "12"));
    }
}
