using Condor.Dominio.Ventas;

namespace Condor.Pruebas.Dominio;

public class CalculadoraVentaTests
{
    [Fact]
    public void Ejemplo_del_wireframe_cemento_fierro_y_esmalte()
    {
        // 10 x 32.50 + 8 x 38.00 + 2 x 58.00 = 745.00
        var importes = new[]
        {
            CalculadoraVenta.Importe(10, 32.50m),
            CalculadoraVenta.Importe(8, 38.00m),
            CalculadoraVenta.Importe(2, 58.00m),
        };

        var totales = CalculadoraVenta.Totales(importes);

        Assert.Equal(745.00m, totales.Total);
        Assert.Equal(631.36m, totales.OpGravada);
        Assert.Equal(113.64m, totales.Igv);
    }

    [Theory]
    [InlineData(1.00)]
    [InlineData(0.10)]
    [InlineData(99.99)]
    [InlineData(12345.67)]
    public void Gravada_mas_IGV_siempre_da_el_total(decimal total)
    {
        var totales = CalculadoraVenta.Totales(new[] { total });

        Assert.Equal(totales.Total, totales.OpGravada + totales.Igv);
    }

    [Fact]
    public void Venta_vacia_da_ceros()
    {
        Assert.Equal(TotalesVenta.Cero, CalculadoraVenta.Totales(Array.Empty<decimal>()));
    }

    [Theory]
    [InlineData(2.5, 7.50, 18.75)]
    [InlineData(0.333, 10.00, 3.33)]
    [InlineData(1.5, 0.35, 0.53)]
    public void Importe_se_redondea_a_centimos(decimal cantidad, decimal precio, decimal esperado)
    {
        Assert.Equal(esperado, CalculadoraVenta.Importe(cantidad, precio));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, 0)]
    public void Cantidad_o_precio_no_positivos_no_se_aceptan(decimal cantidad, decimal precio)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CalculadoraVenta.Importe(cantidad, precio));
    }
}
