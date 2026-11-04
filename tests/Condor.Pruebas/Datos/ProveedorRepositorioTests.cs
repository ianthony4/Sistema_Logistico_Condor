using Condor.Datos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Datos;

[Collection(BaseDatos.Nombre)]
public class ProveedorRepositorioTests
{
    private readonly BaseDatosFixture _bd;

    public ProveedorRepositorioTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task Registrar_y_buscar_por_RUC_devuelve_todos_sus_datos()
    {
        var repo = new ProveedorRepositorio(_bd.Conexiones);
        var ruc = RucNuevo();

        var registrado = await repo.RegistrarAsync(new Proveedor
        {
            Ruc = ruc,
            RazonSocial = "Distribuidora Ferretera del Sur S.A.C.",
            NombreComercial = "Ferresur",
            Direccion = "Av. Parra 302, Arequipa",
            Telefono = "054 286314",
            Correo = "Ventas@Ferresur.pe",
            Contacto = "Rosa Mamani",
        });
        var encontrado = await repo.BuscarPorRucAsync(ruc);

        Assert.True(registrado.Id > 0);
        Assert.NotNull(encontrado);
        Assert.Equal(registrado.Id, encontrado.Id);
        Assert.Equal("Distribuidora Ferretera del Sur S.A.C.", encontrado.RazonSocial);
        Assert.Equal("Ferresur", encontrado.NombreComercial);
        Assert.Equal("Av. Parra 302, Arequipa", encontrado.Direccion);
        Assert.Equal("054 286314", encontrado.Telefono);
        Assert.Equal("ventas@ferresur.pe", encontrado.Correo);
        Assert.Equal("Rosa Mamani", encontrado.Contacto);
        Assert.True(encontrado.Activo);
    }

    [FactConBD]
    public async Task Un_RUC_repetido_avisa_y_no_crea_el_duplicado()
    {
        var repo = new ProveedorRepositorio(_bd.Conexiones);
        var ruc = RucNuevo();
        await repo.RegistrarAsync(new Proveedor { Ruc = ruc, RazonSocial = "Aceros del Sur S.A." });

        var ex = await Assert.ThrowsAsync<ProveedorDuplicadoException>(() =>
            repo.RegistrarAsync(new Proveedor { Ruc = ruc, RazonSocial = "Otra razón social" }));

        Assert.Contains(ruc, ex.Message);
        Assert.Equal("Aceros del Sur S.A.", (await repo.BuscarPorRucAsync(ruc))!.RazonSocial);
    }

    [FactConBD]
    public async Task Un_RUC_invalido_no_llega_a_la_base_de_datos()
    {
        var repo = new ProveedorRepositorio(_bd.Conexiones);

        await Assert.ThrowsAsync<ProveedorInvalidoException>(() =>
            repo.RegistrarAsync(new Proveedor { Ruc = "20131312954", RazonSocial = "RUC mal digitado" }));
        Assert.Null(await repo.BuscarPorRucAsync("20131312954"));
    }

    [FactConBD]
    public async Task Buscar_encuentra_por_parte_de_la_razon_social_sin_tildes_ni_mayusculas()
    {
        var repo = new ProveedorRepositorio(_bd.Conexiones);
        var marca = Guid.NewGuid().ToString("N")[..8];
        await repo.RegistrarAsync(new Proveedor { Ruc = RucNuevo(), RazonSocial = $"Pinturas Sintéticas {marca} S.A." });

        var encontrados = await repo.BuscarAsync($"sinteticas {marca.ToUpperInvariant()}");

        Assert.Contains(encontrados, p => p.RazonSocial.Contains(marca));
    }

    [FactConBD]
    public async Task Buscar_encuentra_por_nombre_comercial_y_por_el_inicio_del_RUC()
    {
        var repo = new ProveedorRepositorio(_bd.Conexiones);
        var marca = Guid.NewGuid().ToString("N")[..8];
        var ruc = RucNuevo();
        await repo.RegistrarAsync(new Proveedor { Ruc = ruc, RazonSocial = "Comercial Andina S.R.L.", NombreComercial = $"Tubos {marca}" });

        Assert.Contains(await repo.BuscarAsync($"tubos {marca}"), p => p.Ruc == ruc);
        Assert.Contains(await repo.BuscarAsync(ruc[..9]), p => p.Ruc == ruc);
    }

    [FactConBD]
    public async Task Buscar_con_el_texto_vacio_lista_ordenado_por_razon_social()
    {
        var repo = new ProveedorRepositorio(_bd.Conexiones);
        var zinc = await repo.RegistrarAsync(new Proveedor { Ruc = RucNuevo(), RazonSocial = "Zzinc y Metales S.A.C." });
        var abrasivos = await repo.RegistrarAsync(new Proveedor { Ruc = RucNuevo(), RazonSocial = "Aabrasivos del Perú S.A.C." });

        var todos = (await repo.BuscarAsync("", limite: 500)).Select(p => p.Id).ToList();

        Assert.Contains(zinc.Id, todos);
        Assert.True(todos.IndexOf(abrasivos.Id) < todos.IndexOf(zinc.Id));
    }

    /// <summary>RUC de persona jurídica al azar con su dígito verificador correcto.</summary>
    private static string RucNuevo()
    {
        var primerosDiez = "20" + Random.Shared.Next(10_000_000, 99_999_999);
        return primerosDiez + ValidadorDocumento.DigitoVerificadorRuc(primerosDiez);
    }
}
