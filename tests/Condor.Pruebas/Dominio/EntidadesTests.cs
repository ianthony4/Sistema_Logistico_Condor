using Condor.Dominio.Entidades;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Dominio;

public class EntidadesTests
{
    [Theory]
    [InlineData("B001", 1, "B001-00000001")]
    [InlineData("F001", 123, "F001-00000123")]
    [InlineData("B002", 99999999, "B002-99999999")]
    public void Serie_formatea_el_numero_con_ocho_digitos(string serie, long numero, string esperado)
    {
        Assert.Equal(esperado, Serie.Formatear(serie, numero));
    }

    [Fact]
    public void Producto_nuevo_es_activo_y_por_unidad()
    {
        var producto = new Producto { Nombre = "Martillo", Precio = 25m };

        Assert.True(producto.Activo);
        Assert.Equal("UND", producto.Unidad);
        Assert.Null(producto.CodigoBarras);
    }

    [Fact]
    public void Comprobante_se_emite_con_el_perfil_maestro()
    {
        var comprobante = new Comprobante { SerieCodigo = "B001", Numero = 7 };

        Assert.Equal("MAESTRO", comprobante.Usuario);
        Assert.Equal("B001-00000007", comprobante.NumeroCompleto);
    }

    [Fact]
    public void Datos_de_prueba_tienen_precio_positivo_y_nombres_distintos()
    {
        var catalogo = DatosPrueba.Catalogo();

        Assert.All(catalogo, p => Assert.True(p.Precio > 0));
        Assert.Equal(catalogo.Count, catalogo.Select(p => p.Nombre).Distinct().Count());
    }
}
