using Condor.Datos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Datos;

[Collection(BaseDatos.Nombre)]
public class ClienteRepositorioTests
{
    private readonly BaseDatosFixture _bd;

    public ClienteRepositorioTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task Registrar_y_buscar_por_DNI_devuelve_sus_datos()
    {
        var repo = new ClienteRepositorio(_bd.Conexiones);
        var dni = DniNuevo();

        var registrado = await repo.RegistrarAsync(new Cliente
        {
            TipoDocumento = TipoDocumento.Dni,
            NumeroDocumento = dni,
            Nombre = "Juan Pérez Quispe",
            Direccion = "Av. Arequipa 120, El Pedregal",
        });
        var encontrado = await repo.BuscarPorDocumentoAsync(TipoDocumento.Dni, dni);

        Assert.True(registrado.Id > 0);
        Assert.NotNull(encontrado);
        Assert.Equal(registrado.Id, encontrado.Id);
        Assert.Equal("Juan Pérez Quispe", encontrado.Nombre);
        Assert.Equal("Av. Arequipa 120, El Pedregal", encontrado.Direccion);
        Assert.Equal(TipoDocumento.Dni, encontrado.TipoDocumento);
    }

    [FactConBD]
    public async Task Buscar_un_documento_no_registrado_devuelve_null()
    {
        var repo = new ClienteRepositorio(_bd.Conexiones);

        Assert.Null(await repo.BuscarPorDocumentoAsync(TipoDocumento.Ruc, RucNuevo()));
    }

    [FactConBD]
    public async Task El_mismo_numero_como_DNI_y_como_RUC_son_clientes_distintos()
    {
        var repo = new ClienteRepositorio(_bd.Conexiones);
        var dni = DniNuevo();
        await repo.RegistrarAsync(new Cliente { TipoDocumento = TipoDocumento.Dni, NumeroDocumento = dni, Nombre = "Persona" });

        Assert.Null(await repo.BuscarPorDocumentoAsync(TipoDocumento.Ruc, dni));
    }

    [FactConBD]
    public async Task Registrar_un_documento_repetido_avisa_y_no_lo_duplica()
    {
        var repo = new ClienteRepositorio(_bd.Conexiones);
        var ruc = RucNuevo();
        await repo.RegistrarAsync(new Cliente { TipoDocumento = TipoDocumento.Ruc, NumeroDocumento = ruc, Nombre = "Constructora Majes S.A.C." });

        var ex = await Assert.ThrowsAsync<ClienteDuplicadoException>(() => repo.RegistrarAsync(
            new Cliente { TipoDocumento = TipoDocumento.Ruc, NumeroDocumento = ruc, Nombre = "Otra razón social" }));

        Assert.Contains(ruc, ex.Message);
        Assert.Equal("Constructora Majes S.A.C.", (await repo.BuscarPorDocumentoAsync(TipoDocumento.Ruc, ruc))!.Nombre);
    }

    [FactConBD]
    public async Task Un_RUC_invalido_no_llega_a_la_base_de_datos()
    {
        var repo = new ClienteRepositorio(_bd.Conexiones);

        await Assert.ThrowsAsync<ClienteInvalidoException>(() => repo.RegistrarAsync(
            new Cliente { TipoDocumento = TipoDocumento.Ruc, NumeroDocumento = "20131312954", Nombre = "RUC mal digitado" }));
        Assert.Null(await repo.BuscarPorDocumentoAsync(TipoDocumento.Ruc, "20131312954"));
    }

    /// <summary>DNI al azar para no chocar con los que dejó una ejecución anterior.</summary>
    private static string DniNuevo() => Random.Shared.Next(10_000_000, 99_999_999).ToString();

    /// <summary>RUC de persona jurídica al azar con su dígito verificador correcto.</summary>
    private static string RucNuevo()
    {
        var primerosDiez = "20" + Random.Shared.Next(10_000_000, 99_999_999);
        return primerosDiez + ValidadorDocumento.DigitoVerificadorRuc(primerosDiez);
    }
}
