using Condor.Dominio.Validacion;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Dominio;

/// <summary>Los datos de prueba del Sprint 2 cumplen las reglas del dominio (T-35).</summary>
public class DatosPruebaClientesTests
{
    [Fact]
    public void Los_clientes_de_prueba_son_validos()
    {
        Assert.All(DatosPrueba.Clientes(), c => Assert.Empty(ValidadorCliente.Errores(c)));
    }

    [Fact]
    public void Los_documentos_al_azar_son_validos()
    {
        for (var i = 0; i < 50; i++)
        {
            Assert.True(ValidadorDocumento.EsDniValido(DatosPrueba.DniNuevo()));
            Assert.True(ValidadorDocumento.EsRucValido(DatosPrueba.RucNuevo()));
        }
    }

    [Fact]
    public void El_RUC_de_la_empresa_de_prueba_es_valido()
    {
        Assert.True(ValidadorDocumento.EsRucValido(DatosPrueba.Empresa().Ruc));
    }

    [Fact]
    public async Task El_repositorio_en_memoria_no_admite_documentos_repetidos()
    {
        var repo = new ClienteRepositorioEnMemoria(DatosPrueba.Clientes());

        await Assert.ThrowsAsync<ClienteDuplicadoException>(() => repo.RegistrarAsync(DatosPrueba.ClienteDni()));
        Assert.Equal(2, repo.Clientes.Count);
    }

    [Fact]
    public async Task El_repositorio_de_comprobantes_en_memoria_numera_cada_serie_desde_1()
    {
        var repo = new ComprobanteRepositorioEnMemoria();

        var b1 = await repo.EmitirAsync(new() { SerieCodigo = "B001", Total = 10m });
        var b2 = await repo.EmitirAsync(new() { SerieCodigo = "B001", Total = 10m });
        var f1 = await repo.EmitirAsync(new() { SerieCodigo = "F001", Total = 10m });

        Assert.Equal(new long[] { 1, 2, 1 }, new[] { b1.Numero, b2.Numero, f1.Numero });
        Assert.Equal("B001-00000002", b2.NumeroCompleto);
    }
}
