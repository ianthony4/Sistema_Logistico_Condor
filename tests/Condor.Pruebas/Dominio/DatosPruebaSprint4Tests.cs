using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Dominio.Validacion;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Dominio;

/// <summary>Los datos de prueba del Sprint 4 cumplen las reglas del dominio (T-65).</summary>
public class DatosPruebaSprint4Tests
{
    [Fact]
    public void La_factura_de_la_obra_va_con_RUC_valido_y_serie_F001()
    {
        var factura = DatosPrueba.FacturaObra();

        Assert.Equal(TipoComprobante.Factura, factura.TipoComprobante);
        Assert.Equal("F001", factura.SerieCodigo);
        Assert.True(ValidadorDocumento.EsRucValido(factura.Cliente!.NumeroDocumento));
        Assert.Equal(1030.00m, factura.Total);
        Assert.Equal(factura.Total, factura.OpGravada + factura.Igv);
    }

    [Fact]
    public async Task El_registro_de_respaldos_en_memoria_da_el_mas_reciente()
    {
        var repo = new RespaldoRepositorioEnMemoria(new[] { DatosPrueba.RespaldoDeHace(30), DatosPrueba.RespaldoDeHace(2) });

        var ultimo = await repo.UltimoAsync();

        Assert.Equal(2, ultimo!.Id);
        Assert.Equal(DatosPrueba.DiscoExterno, ultimo.Carpeta);
        Assert.Null(await new RespaldoRepositorioEnMemoria().UltimoAsync());
    }

    [Fact]
    public async Task El_generador_falso_informa_el_avance_y_simula_el_disco_desconectado()
    {
        var generador = new GeneradorRespaldoFalso();
        var avances = new List<int>();

        var respaldo = await generador.GenerarAsync(DatosPrueba.DiscoExterno, new Progreso(p => avances.Add(p.Porcentaje)));

        Assert.EndsWith(Respaldo.Extension, respaldo.Archivo);
        Assert.Equal(0, avances[0]);
        Assert.Equal(100, avances[^1]);
        Assert.Single(generador.Generados);
        await Assert.ThrowsAsync<RespaldoException>(() => generador.GenerarAsync(@"F:\otro disco"));
    }

    [Fact]
    public async Task La_SUNAT_falsa_puede_quedarse_sin_internet_a_mitad()
    {
        var sunat = new EnvioSunatFalso { SinInternetDespuesDe = 2 };
        var factura = DatosPrueba.FacturaObra();

        await sunat.EnviarAsync(DatosPrueba.Empresa(), factura);
        await sunat.EnviarAsync(DatosPrueba.Empresa(), factura);

        await Assert.ThrowsAsync<SunatNoDisponibleException>(() => sunat.EnviarAsync(DatosPrueba.Empresa(), factura));
        Assert.True(sunat.SinInternet);
        Assert.Equal(2, sunat.Enviados.Count);
    }

    [Fact]
    public async Task Los_pendientes_en_memoria_se_reservan_para_una_sola_PC()
    {
        var registro = new EnvioSunatRepositorioEnMemoria();
        registro.SinIntentos.UnionWith(new long[] { 1, 2, 3 });

        var pc1 = await registro.ReservarPendientesAsync("PC-1", TimeSpan.Zero, limite: 2);
        var pc2 = await registro.ReservarPendientesAsync("PC-2", TimeSpan.Zero);

        Assert.Equal(new long[] { 1, 2 }, pc1);
        Assert.Equal(new long[] { 3 }, pc2);
        Assert.Equal(3, (await registro.ResumenPendientesAsync()).Cantidad);
    }

    private sealed class Progreso(Action<ProgresoRespaldo> alInformar) : IProgress<ProgresoRespaldo>
    {
        public void Report(ProgresoRespaldo value) => alInformar(value);
    }
}
