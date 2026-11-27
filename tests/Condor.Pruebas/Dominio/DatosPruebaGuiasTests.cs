using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Dominio.Validacion;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Dominio;

/// <summary>Los datos de prueba de guías y códigos de barras cumplen las reglas (T-98).</summary>
public class DatosPruebaGuiasTests
{
    [Fact]
    public void La_guia_de_la_obra_es_valida_en_las_dos_modalidades()
    {
        var privada = DatosPrueba.GuiaObra();
        var publica = DatosPrueba.GuiaObra(publico: true);

        Assert.Empty(ValidadorGuia.Errores(privada, DatosPrueba.Hoy()));
        Assert.Empty(ValidadorGuia.Errores(publica, DatosPrueba.Hoy()));
        Assert.Equal("V1A952", privada.VehiculoPlaca);
        Assert.Null(publica.VehiculoPlaca);
        Assert.Equal(2, privada.Detalle.Count);
    }

    [Fact]
    public void El_catalogo_con_codigos_tiene_el_cemento_etiquetado()
    {
        var catalogo = DatosPrueba.CatalogoConCodigos();

        Assert.Equal(DatosPrueba.CodigoCemento, catalogo[0].CodigoBarras);
        Assert.True(ValidadorProducto.PareceCodigo(DatosPrueba.CodigoCemento));
    }

    [Fact]
    public async Task Las_guias_en_memoria_numeran_la_T001_y_conservan_el_estado_definitivo()
    {
        var repo = new GuiaRemisionRepositorioEnMemoria();
        var primera = await repo.EmitirAsync(DatosPrueba.GuiaObra());
        var segunda = await repo.EmitirAsync(DatosPrueba.GuiaObra());

        await repo.RegistrarRespuestaAsync(primera.Id, new RespuestaSunat { Estado = EstadoSunat.Aceptado, Ticket = "t1" });
        await repo.RegistrarRespuestaAsync(primera.Id, new RespuestaSunat { Estado = EstadoSunat.Rechazado });

        Assert.Equal("T001-00000002", segunda.NumeroCompleto);
        Assert.Equal(EstadoSunat.Aceptado, primera.EstadoSunat);
        Assert.Equal("t1", primera.Ticket);
        Assert.Equal(segunda.Id, Assert.Single(await repo.ListarPendientesAsync()).Id);
        Assert.Single(await repo.BuscarAsync(new FiltroGuias(Texto: "T001-2")));
    }

    [Fact]
    public async Task La_SUNAT_de_guias_falsa_deja_en_proceso_y_acepta_al_consultar_el_ticket()
    {
        var sunat = new EnvioGuiaFalso { Demora = true };
        var guia = DatosPrueba.GuiaObra();
        guia.Numero = 7;

        var primera = await sunat.EnviarAsync(DatosPrueba.Empresa(), guia);
        guia.Ticket = primera.Ticket;
        var segunda = await sunat.EnviarAsync(DatosPrueba.Empresa(), guia);

        Assert.Equal(EstadoSunat.Pendiente, primera.Estado);
        Assert.Equal("ticket-7", primera.Ticket);
        Assert.Equal(EstadoSunat.Aceptado, segunda.Estado);
        Assert.Equal(new[] { "ticket-7" }, sunat.Consultas);
        Assert.Single(sunat.Enviadas);
    }
}
