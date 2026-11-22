using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Dominio;

/// <summary>Los datos de prueba del Sprint 5 cumplen las reglas del dominio (T-85).</summary>
public class DatosPruebaSprint5Tests
{
    [Fact]
    public void La_nota_de_venta_del_mostrador_va_con_NV01_y_sin_cliente()
    {
        var nota = DatosPrueba.NotaVentaMostrador();

        Assert.Equal(TipoComprobante.NotaVenta, nota.TipoComprobante);
        Assert.Equal("NV01", nota.SerieCodigo);
        Assert.Null(nota.Cliente);
        Assert.Equal(22.50m, nota.Total);
        Assert.False(nota.EsElectronico);
    }

    [Fact]
    public void La_proforma_de_la_obra_cotiza_el_mismo_pedido_de_la_factura()
    {
        var proforma = DatosPrueba.ProformaObra();

        Assert.Equal(TipoComprobante.Proforma, proforma.TipoComprobante);
        Assert.Equal("PR01", proforma.SerieCodigo);
        Assert.True(ValidadorDocumento.EsRucValido(proforma.Cliente!.NumeroDocumento));
        Assert.Equal(DatosPrueba.FacturaObra().Total, proforma.Total);
    }

    [Fact]
    public void Las_compras_de_prueba_son_validas_y_cuadran()
    {
        var aceros = DatosPrueba.CompraAceros();
        var cemento = DatosPrueba.CompraCemento();

        Assert.Empty(ValidadorCompra.Errores(aceros, DatosPrueba.Hoy()));
        Assert.Empty(ValidadorCompra.Errores(cemento, DatosPrueba.Hoy()));
        Assert.Equal(1637.00m, aceros.Total);
        Assert.Equal(1112.00m, cemento.Total);
        Assert.Equal("Boleta B004-00000731", cemento.DocumentoCompleto);
        Assert.True(ValidadorDocumento.EsRucValido(aceros.Proveedor!.Ruc));
    }

    [Fact]
    public async Task Las_compras_en_memoria_no_repiten_el_comprobante_del_proveedor()
    {
        var repo = new CompraRepositorioEnMemoria(new[] { DatosPrueba.CompraAceros() });

        await Assert.ThrowsAsync<CompraDuplicadaException>(() => repo.RegistrarAsync(DatosPrueba.CompraAceros()));
        await repo.RegistrarAsync(DatosPrueba.CompraAceros(numero: 12346));

        Assert.Equal(2, repo.Compras.Count);
    }

    [Fact]
    public async Task Las_compras_en_memoria_suman_todo_el_filtro_aunque_se_corte_la_lista()
    {
        var repo = new CompraRepositorioEnMemoria(new[]
        {
            DatosPrueba.CompraAceros(), DatosPrueba.CompraAceros(numero: 2), DatosPrueba.CompraCemento(),
        });

        var resultado = await repo.BuscarAsync(new FiltroCompras(Limite: 1));
        var soloAceros = await repo.BuscarAsync(new FiltroCompras(Proveedor: "aceros"));

        Assert.Single(resultado.Compras);
        Assert.Equal(3, resultado.Cantidad);
        Assert.Equal(1637.00m * 2 + 1112.00m, resultado.Total);
        Assert.Equal(2, soloAceros.Cantidad);
    }

    [Fact]
    public async Task Las_proformas_en_memoria_se_convierten_una_sola_vez()
    {
        var repo = new ComprobanteRepositorioEnMemoria();
        var proforma = await repo.EmitirAsync(DatosPrueba.ProformaObra());
        var factura = DatosPrueba.FacturaObra();
        factura.ProformaId = proforma.Id;

        await repo.EmitirAsync(factura);
        var otra = DatosPrueba.FacturaObra();
        otra.ProformaId = proforma.Id;

        Assert.Equal(factura.NumeroCompleto, proforma.ConvertidaEn);
        Assert.NotNull(proforma.ValidaHasta);
        await Assert.ThrowsAsync<Condor.Dominio.Ventas.ProformaYaConvertidaException>(() => repo.EmitirAsync(otra));
    }
}
