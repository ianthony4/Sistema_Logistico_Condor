using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;

namespace Condor.Pruebas.Dominio;

public class ValidadorCompraTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 26);

    /// <summary>Factura de Aceros Arequipa: 50 varillas a S/ 31.50 y 10 kg de alambre a S/ 6.20 (S/ 1637.00).</summary>
    private static Compra FacturaAceros() => new()
    {
        ProveedorId = 3,
        TipoDocumento = TipoDocumentoCompra.Factura,
        Serie = "f001",
        Numero = 12345,
        FechaEmision = Hoy.AddDays(-2),
        Detalle =
        {
            new() { ProductoId = 2, Descripcion = "Fierro corrugado 1/2 in x 9 m", Unidad = "VAR", Cantidad = 50, CostoUnitario = 31.50m },
            new() { ProductoId = 6, Descripcion = "Alambre negro N.° 16", Unidad = "KG", Cantidad = 10, CostoUnitario = 6.20m },
        },
    };

    [Fact]
    public void Prepara_la_compra_con_sus_importes_y_el_IGV_incluido()
    {
        var compra = ValidadorCompra.Preparar(FacturaAceros(), Hoy);

        Assert.Equal("F001", compra.Serie);
        Assert.Equal("F001-00012345", compra.Documento);
        Assert.Equal("Factura F001-00012345", compra.DocumentoCompleto);
        Assert.Equal(new[] { 1, 2 }, compra.Detalle.Select(l => l.Item));
        Assert.Equal(new[] { 1575.00m, 62.00m }, compra.Detalle.Select(l => l.Importe));
        Assert.Equal(1637.00m, compra.Total);
        Assert.Equal(1387.29m, compra.OpGravada);
        Assert.Equal(249.71m, compra.Igv);
        Assert.Equal(compra.Total, compra.OpGravada + compra.Igv);
    }

    [Fact]
    public void Sin_proveedor_no_se_registra()
    {
        var compra = FacturaAceros();
        compra.ProveedorId = 0;

        var error = Assert.Single(ValidadorCompra.Errores(compra, Hoy));
        Assert.Equal(CampoCompra.Proveedor, error.Campo);
    }

    [Theory]
    [InlineData("F01")]
    [InlineData("F0001")]
    [InlineData("F-01")]
    [InlineData("")]
    public void La_serie_del_proveedor_tiene_4_caracteres(string serie)
    {
        var compra = FacturaAceros();
        compra.Serie = serie;

        var error = Assert.Single(ValidadorCompra.Errores(compra, Hoy));
        Assert.Equal(CampoCompra.Documento, error.Campo);
        Assert.Contains("serie", error.Mensaje);
    }

    [Theory]
    [InlineData("E001")]
    [InlineData("FA01")]
    [InlineData("B002")]
    public void Acepta_las_series_electronicas_y_fisicas(string serie)
    {
        var compra = FacturaAceros();
        compra.Serie = serie;

        Assert.Empty(ValidadorCompra.Errores(compra, Hoy));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100000000)]
    public void El_numero_va_de_1_a_99999999(long numero)
    {
        var compra = FacturaAceros();
        compra.Numero = numero;

        Assert.Equal(CampoCompra.Documento, Assert.Single(ValidadorCompra.Errores(compra, Hoy)).Campo);
    }

    [Fact]
    public void Solo_se_registran_facturas_y_boletas_del_proveedor()
    {
        var compra = FacturaAceros();
        compra.TipoDocumento = "07";

        Assert.Contains("factura o boleta", Assert.Single(ValidadorCompra.Errores(compra, Hoy)).Mensaje);
    }

    [Fact]
    public void La_fecha_no_puede_ser_futura()
    {
        var compra = FacturaAceros();
        compra.FechaEmision = Hoy.AddDays(1);

        var error = Assert.Single(ValidadorCompra.Errores(compra, Hoy));
        Assert.Equal(CampoCompra.Fecha, error.Campo);
        Assert.Contains("posterior a hoy", error.Mensaje);
    }

    [Fact]
    public void Una_compra_de_hoy_se_registra()
    {
        var compra = FacturaAceros();
        compra.FechaEmision = Hoy;

        Assert.Empty(ValidadorCompra.Errores(compra, Hoy));
    }

    [Fact]
    public void Una_fecha_de_hace_mas_de_un_ano_se_avisa()
    {
        var compra = FacturaAceros();
        compra.FechaEmision = Hoy.AddDays(-400);

        Assert.Contains("más de un año", Assert.Single(ValidadorCompra.Errores(compra, Hoy)).Mensaje);
    }

    [Fact]
    public void Sin_productos_no_se_registra()
    {
        var compra = FacturaAceros();
        compra.Detalle.Clear();

        Assert.Equal(CampoCompra.Detalle, Assert.Single(ValidadorCompra.Errores(compra, Hoy)).Campo);
    }

    [Fact]
    public void Un_costo_en_cero_no_se_acepta()
    {
        var compra = FacturaAceros();
        compra.Detalle[1].CostoUnitario = 0;

        Assert.Contains("costo", Assert.Single(ValidadorCompra.Errores(compra, Hoy)).Mensaje);
    }

    [Fact]
    public void Preparar_una_compra_invalida_lanza_la_excepcion_con_todos_los_errores()
    {
        var compra = new Compra { Serie = "x", FechaEmision = Hoy };

        var ex = Assert.Throws<CompraInvalidaException>(() => ValidadorCompra.Preparar(compra, Hoy));

        Assert.Equal(new[] { CampoCompra.Proveedor, CampoCompra.Documento, CampoCompra.Documento, CampoCompra.Detalle },
            ex.Errores.Select(e => e.Campo));
    }

    [Fact]
    public void La_observacion_vacia_queda_en_null()
    {
        var compra = FacturaAceros();
        compra.Observacion = "   ";

        Assert.Null(ValidadorCompra.Preparar(compra, Hoy).Observacion);
    }

    [Fact]
    public void La_compra_duplicada_dice_el_documento_y_el_proveedor()
    {
        var ex = new CompraDuplicadaException("Factura F001-00012345", "Aceros Arequipa");

        Assert.Contains("Factura F001-00012345 de Aceros Arequipa ya está registrada", ex.Message);
    }
}
