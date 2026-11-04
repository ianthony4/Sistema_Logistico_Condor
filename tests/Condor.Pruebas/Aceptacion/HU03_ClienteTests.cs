using Condor.Datos;
using Condor.Dominio.Entidades;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Aceptacion;

/// <summary>
/// HU-03: registrar o buscar cliente por DNI/RUC.
/// Criterio 1: si el cliente ya fue registrado, al digitar su DNI o RUC y
/// presionar Buscar se completan el nombre o la razón social y la dirección.
/// Criterio 2: si el cliente no existe, al presionar + Nuevo cliente y guardar
/// sus datos queda registrado y seleccionado en la venta en curso.
/// Criterio 3: si el RUC no tiene 11 dígitos o su dígito verificador es
/// inválido, el campo se marca en error y no se puede continuar.
/// </summary>
[Collection(BaseDatos.Nombre)]
public class HU03_ClienteTests
{
    private readonly BaseDatosFixture _bd;

    public HU03_ClienteTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task CA1_cliente_registrado_en_la_PC1_se_completa_al_buscarlo_en_la_PC2()
    {
        await using var local = new TresPC();
        var ruc = DatosPrueba.RucNuevo();
        await new ClienteRepositorio(local.Pc[0]).RegistrarAsync(DatosPrueba.ClienteRuc(ruc));

        var panel = new ClienteVentaViewModel(new ClienteRepositorio(local.Pc[1])) { Documento = ruc };
        await panel.BuscarCommand.EjecutarAsync();

        Assert.True(panel.HayCliente, panel.Error);
        Assert.Equal("Constructora Valle de Majes S.A.C.", panel.Seleccionado!.Nombre);
        Assert.Equal("Av. Principal 450, Majes", panel.Seleccionado.Direccion);
        Assert.False(panel.TieneError);
    }

    [FactConBD]
    public async Task CA1_buscar_por_DNI_completa_el_nombre()
    {
        var repo = new ClienteRepositorio(_bd.Conexiones);
        var dni = DatosPrueba.DniNuevo();
        await repo.RegistrarAsync(DatosPrueba.ClienteDni(dni));

        var panel = new ClienteVentaViewModel(repo) { Documento = dni };
        await panel.BuscarCommand.EjecutarAsync();

        Assert.Equal("Juan Pérez Quispe", panel.Seleccionado?.Nombre);
    }

    [FactConBD]
    public async Task CA2_cliente_nuevo_queda_registrado_y_seleccionado_en_la_venta()
    {
        await using var local = new TresPC();
        var dni = DatosPrueba.DniNuevo();
        var venta = new MainViewModel(new ProductoRepositorio(local.Pc[0]), new ClienteRepositorio(local.Pc[0]));

        venta.Cliente!.Documento = dni;
        await venta.Cliente.BuscarCommand.EjecutarAsync();
        Assert.True(venta.Cliente.NoEncontrado);

        var dialogo = venta.CrearNuevoCliente()!;
        dialogo.Nombre = "María Condori Mamani";
        dialogo.Direccion = "Sector B-2, Majes";
        var cerrado = false;
        dialogo.Cerrar += g => cerrado = g;
        await dialogo.GuardarCommand.EjecutarAsync();
        venta.ClienteRegistrado(dialogo.Guardado!);

        Assert.True(cerrado, dialogo.Error);
        Assert.Equal("María Condori Mamani", venta.Cliente.Seleccionado!.Nombre);
        // Registrado en la base central: la PC-3 ya lo encuentra.
        var enPc3 = await new ClienteRepositorio(local.Pc[2]).BuscarPorDocumentoAsync(TipoDocumento.Dni, dni);
        Assert.Equal(venta.Cliente.Seleccionado.Id, enPc3?.Id);
    }

    [Theory]
    [InlineData("2013131295")]   // 10 dígitos
    [InlineData("201313129551")] // 12 dígitos
    [InlineData("20131312954")]  // dígito verificador inválido
    public async Task CA3_RUC_invalido_marca_el_campo_y_no_continua(string ruc)
    {
        var repo = new ClienteRepositorioEnMemoria(DatosPrueba.Clientes());
        var panel = new ClienteVentaViewModel(repo) { Documento = ruc };

        await panel.BuscarCommand.EjecutarAsync();

        Assert.True(panel.TieneError);
        Assert.False(panel.HayCliente);
        Assert.False(panel.NoEncontrado); // no ofrece registrarlo
        Assert.Equal(0, repo.Busquedas);
    }

    [Fact]
    public async Task CA3_el_dialogo_tampoco_registra_un_RUC_invalido()
    {
        var repo = new ClienteRepositorioEnMemoria();
        var dialogo = new NuevoClienteViewModel(repo) { EsRuc = true, NumeroDocumento = "20131312954", Nombre = "Empresa" };

        await dialogo.GuardarCommand.EjecutarAsync();

        Assert.NotNull(dialogo.Error);
        Assert.Empty(repo.Clientes);
    }
}
