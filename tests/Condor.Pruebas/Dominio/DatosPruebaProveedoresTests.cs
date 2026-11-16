using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Dominio.Validacion;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Dominio;

/// <summary>Los datos de prueba del Sprint 3 cumplen las reglas del dominio (T-50).</summary>
public class DatosPruebaProveedoresTests
{
    [Fact]
    public void Los_proveedores_de_prueba_son_validos()
    {
        Assert.All(DatosPrueba.Proveedores(), p => Assert.Empty(ValidadorProveedor.Errores(p)));
    }

    [Fact]
    public async Task El_repositorio_en_memoria_no_admite_RUC_repetidos()
    {
        var repo = new ProveedorRepositorioEnMemoria(DatosPrueba.Proveedores());

        await Assert.ThrowsAsync<ProveedorDuplicadoException>(() => repo.RegistrarAsync(DatosPrueba.ProveedorAceros()));
        Assert.Equal(3, repo.Proveedores.Count);
    }

    [Fact]
    public async Task El_repositorio_en_memoria_busca_como_el_real()
    {
        var repo = new ProveedorRepositorioEnMemoria(DatosPrueba.Proveedores());

        Assert.Equal("Yura S.A.", Assert.Single(await repo.BuscarAsync("yura")).RazonSocial);
        Assert.Single(await repo.BuscarAsync("quimicos"));      // sin tilde encuentra "Químicos"
        Assert.Single(await repo.BuscarAsync("2037014"));       // inicio del RUC
        Assert.Equal(3, (await repo.BuscarAsync("")).Count);
    }

    [Fact]
    public async Task La_SUNAT_falsa_acepta_por_defecto_y_simula_la_falta_de_internet()
    {
        var sunat = new EnvioSunatFalso();
        var boleta = new Comprobante { Id = 1, SerieCodigo = "B001", Numero = 1, Total = 32.50m, OpGravada = 27.54m, Igv = 4.96m };

        var respuesta = await sunat.EnviarAsync(DatosPrueba.Empresa(), boleta);
        Assert.Equal(EstadoSunat.Aceptado, respuesta.Estado);
        Assert.Equal("20600000005-03-B001-1", respuesta.NombreArchivo);
        Assert.Single(sunat.Enviados);

        sunat.SinInternet = true;
        await Assert.ThrowsAsync<SunatNoDisponibleException>(() => sunat.EnviarAsync(DatosPrueba.Empresa(), boleta));
    }

    [Fact]
    public void Las_respuestas_de_prueba_dan_un_resumen_claro()
    {
        Assert.Equal("Aceptada por la SUNAT.", EnvioSunatFalso.Aceptada().Resumen);
        Assert.StartsWith("Rechazada por la SUNAT (2017)", EnvioSunatFalso.Rechazada().Resumen);
        Assert.Contains("4252", EnvioSunatFalso.Observada().Resumen);
        Assert.True(EnvioSunatFalso.Observada().Aceptado);
        Assert.False(EnvioSunatFalso.Rechazada().Aceptado);
    }
}
