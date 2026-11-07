using Condor.Datos;
using Condor.Dominio.Validacion;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Aceptacion;

/// <summary>
/// HU-04: registrar proveedores.
/// Criterio 1: al ingresar en Nuevo proveedor un RUC válido y la razón social
/// (y, si se quiere, nombre comercial, dirección, teléfono, correo y
/// contacto) y presionar Guardar, el proveedor queda registrado y aparece en
/// la ventana Proveedores de las tres PC.
/// Criterio 2: si ya existe un proveedor con el mismo RUC, el sistema lo
/// advierte y no crea el duplicado.
/// Criterio 3: si el RUC no tiene 11 dígitos o su dígito verificador es
/// inválido, el sistema lo señala y no lo guarda.
/// </summary>
[Collection(BaseDatos.Nombre)]
public class HU04_ProveedoresTests
{
    private readonly BaseDatosFixture _bd;

    public HU04_ProveedoresTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task CA1_proveedor_registrado_en_la_PC1_aparece_en_las_PC2_y_PC3()
    {
        await using var local = new TresPC();
        var ruc = DatosPrueba.RucNuevo();

        var pc1 = new ProveedoresViewModel(new ProveedorRepositorio(local.Pc[0]));
        var dialogo = pc1.CrearNuevoProveedor();
        dialogo.Ruc = ruc;
        dialogo.RazonSocial = "Distribuidora Ferretera del Sur S.A.C.";
        dialogo.NombreComercial = "Ferresur";
        dialogo.Direccion = "Av. Parra 302, Arequipa";
        dialogo.Telefono = "054 286314";
        dialogo.Correo = "ventas@ferresur.pe";
        dialogo.Contacto = "Rosa Mamani";
        await dialogo.GuardarCommand.EjecutarAsync();
        Assert.Null(dialogo.Error);
        await pc1.ProveedorRegistradoAsync(dialogo.Guardado!);

        foreach (var pc in local.Pc.Skip(1))
        {
            var lista = new ProveedoresViewModel(new ProveedorRepositorio(pc)) { Texto = ruc };
            await lista.BuscarCommand.EjecutarAsync();

            var encontrado = Assert.Single(lista.Proveedores);
            Assert.Equal("Distribuidora Ferretera del Sur S.A.C.", encontrado.RazonSocial);
            Assert.Equal("Ferresur", encontrado.NombreComercial);
            Assert.Equal("054 286314", encontrado.Telefono);
            Assert.Equal("ventas@ferresur.pe", encontrado.Correo);
            Assert.Equal("Rosa Mamani", encontrado.Contacto);
        }
    }

    [FactConBD]
    public async Task CA1_basta_el_RUC_y_la_razon_social_y_se_encuentra_por_su_nombre()
    {
        var repo = new ProveedorRepositorio(_bd.Conexiones);
        var marca = Guid.NewGuid().ToString("N")[..6];
        var dialogo = new NuevoProveedorViewModel(repo) { Ruc = DatosPrueba.RucNuevo(), RazonSocial = $"Pinturas Andinas {marca} S.A.C." };

        await dialogo.GuardarCommand.EjecutarAsync();
        var lista = new ProveedoresViewModel(repo) { Texto = $"andinas {marca}" };
        await lista.BuscarCommand.EjecutarAsync();

        Assert.Null(dialogo.Error);
        Assert.Equal(dialogo.Guardado!.Id, Assert.Single(lista.Proveedores).Id);
    }

    [FactConBD]
    public async Task CA2_el_mismo_RUC_desde_otra_PC_se_advierte_y_no_se_duplica()
    {
        await using var local = new TresPC();
        var ruc = DatosPrueba.RucNuevo();
        await new ProveedorRepositorio(local.Pc[0]).RegistrarAsync(DatosPrueba.ProveedorAceros(ruc));

        var dialogo = new NuevoProveedorViewModel(new ProveedorRepositorio(local.Pc[2])) { Ruc = ruc, RazonSocial = "Otro nombre S.A." };
        bool? cerrado = null;
        dialogo.Cerrar += g => cerrado = g;
        await dialogo.GuardarCommand.EjecutarAsync();

        Assert.Contains($"Ya existe un proveedor con RUC {ruc}", dialogo.Error);
        Assert.Null(cerrado); // el diálogo sigue abierto
        var registrado = await new ProveedorRepositorio(local.Pc[1]).BuscarPorRucAsync(ruc);
        Assert.Equal("Corporación Aceros Arequipa S.A.", registrado!.RazonSocial);
    }

    [FactConBD]
    public async Task CA2_dos_PC_que_guardan_el_mismo_RUC_a_la_vez_dejan_un_solo_proveedor()
    {
        await using var local = new TresPC();
        var ruc = DatosPrueba.RucNuevo();

        var intentos = local.Pc.Select(pc => Task.Run(async () =>
        {
            try
            {
                await new ProveedorRepositorio(pc).RegistrarAsync(DatosPrueba.ProveedorCemento(ruc));
                return true;
            }
            catch (ProveedorDuplicadoException)
            {
                return false;
            }
        })).ToList();
        var resultados = await Task.WhenAll(intentos);

        Assert.Equal(1, resultados.Count(r => r));
        var lista = await new ProveedorRepositorio(_bd.Conexiones).BuscarAsync(ruc);
        Assert.Single(lista);
    }

    [FactConBD]
    public async Task CA3_un_RUC_con_digito_verificador_invalido_no_se_guarda()
    {
        var repo = new ProveedorRepositorio(_bd.Conexiones);
        var ruc = DatosPrueba.RucNuevo();
        var invalido = ruc[..10] + (char)('0' + (ruc[10] - '0' + 1) % 10);

        var dialogo = new NuevoProveedorViewModel(repo) { Ruc = invalido, RazonSocial = "Aceros del Sur S.A." };
        await dialogo.GuardarCommand.EjecutarAsync();

        Assert.Contains("dígito verificador", dialogo.Error);
        Assert.Null(await repo.BuscarPorRucAsync(invalido));
    }

    [Fact]
    public async Task CA3_un_RUC_de_10_digitos_se_senala_sin_ir_a_la_base_de_datos()
    {
        var repo = new ProveedorRepositorioEnMemoria();
        var dialogo = new NuevoProveedorViewModel(repo) { Ruc = "2037014699", RazonSocial = "Aceros" };

        await dialogo.GuardarCommand.EjecutarAsync();

        Assert.Contains("11 dígitos", dialogo.Error);
        Assert.Empty(repo.Proveedores);
    }
}
