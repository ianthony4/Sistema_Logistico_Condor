using Condor.Datos;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Datos;

/// <summary>Búsqueda de clientes para la ventana Clientes (F3, T-97).</summary>
[Collection(BaseDatos.Nombre)]
public class ClienteBusquedaTests
{
    private readonly BaseDatosFixture _bd;

    public ClienteBusquedaTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task Busca_por_el_inicio_del_documento_o_por_el_nombre_sin_tildes()
    {
        var repo = new ClienteRepositorio(_bd.Conexiones);
        var cliente = DatosPrueba.ClienteDni(DatosPrueba.DniNuevo());
        cliente.Nombre = DatosPrueba.NombreUnico("Núñez Huamaní");
        await repo.RegistrarAsync(cliente);

        var porNombre = await repo.BuscarAsync(cliente.Nombre[..14].Replace("ú", "u").Replace("Ñ", "N").ToUpperInvariant());
        var porDocumento = await repo.BuscarAsync(cliente.NumeroDocumento);

        Assert.Contains(porNombre, c => c.Id == cliente.Id);
        Assert.Equal(cliente.Id, Assert.Single(porDocumento).Id);
        Assert.NotEmpty(await repo.BuscarAsync(null));
    }
}
