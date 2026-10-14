using Condor.Datos;
using Condor.Dominio.Entidades;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;
using Npgsql;

namespace Condor.Pruebas.Aceptacion;

/// <summary>
/// HU-14: acceso con el perfil único de Usuario Maestro.
/// Criterio 1: al abrir el sistema se muestra la pantalla Nueva venta con el
/// perfil MAESTRO, sin pedir usuario ni contraseña.
/// Criterio 2: cada documento queda registrado con el perfil MAESTRO y con el
/// nombre de la terminal desde la que se emitió.
/// </summary>
[Collection(BaseDatos.Nombre)]
public class HU14_AccesoMaestroTests
{
    private readonly BaseDatosFixture _bd;

    public HU14_AccesoMaestroTests(BaseDatosFixture bd) => _bd = bd;

    [Fact]
    public void Al_abrir_se_muestra_Nueva_venta_con_el_perfil_MAESTRO()
    {
        var vm = new MainViewModel();

        vm.IniciarSesion(Sesion.Local("PC-2"));

        Assert.Contains("Nueva venta", vm.Titulo);
        Assert.Equal("MAESTRO", vm.Usuario);
        Assert.Equal("PC-2", vm.Terminal);
    }

    [Fact]
    public void La_sesion_no_tiene_credenciales()
    {
        // El perfil es fijo: no existe una propiedad de usuario ni de clave que completar.
        var propiedades = typeof(Sesion).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(propiedades, p => p.Contains("Clave") || p.Contains("Password"));
        Assert.Equal(Perfiles.Maestro, Sesion.Local("PC-1").Usuario);
    }

    [Fact]
    public void La_cabecera_se_actualiza_al_confirmar_la_terminal()
    {
        var vm = new MainViewModel();
        var cambios = new List<string?>();
        vm.PropertyChanged += (_, e) => cambios.Add(e.PropertyName);

        vm.IniciarSesion(new Sesion(new Terminal { Id = 3, Nombre = "PC-3" }));

        Assert.Contains(nameof(MainViewModel.Terminal), cambios);
        Assert.Contains(nameof(MainViewModel.Usuario), cambios);
        Assert.True(vm.Sesion!.Registrada);
    }

    [FactConBD]
    public async Task Abrir_el_sistema_dos_veces_en_la_misma_PC_no_duplica_la_terminal()
    {
        var repo = new TerminalRepositorio(_bd.Conexiones);
        var nombre = DatosPrueba.NombreUnico("PC-T10");

        var primera = await repo.RegistrarAccesoAsync(nombre);
        var segunda = await repo.RegistrarAccesoAsync(nombre);

        Assert.Equal(primera.Id, segunda.Id);
        Assert.True(segunda.UltimoAcceso >= primera.UltimoAcceso);
        Assert.Equal(1L, await ContarTerminales(nombre));
    }

    [FactConBD]
    public async Task Tres_PC_que_arrancan_a_la_vez_quedan_registradas_por_separado()
    {
        var repo = new TerminalRepositorio(_bd.Conexiones);
        var nombres = Enumerable.Range(1, 3).Select(i => DatosPrueba.NombreUnico($"PC-{i}")).ToArray();

        var terminales = await Task.WhenAll(nombres.Select(n => repo.RegistrarAccesoAsync(n)));

        Assert.Equal(3, terminales.Select(t => t.Id).Distinct().Count());
        Assert.Equal(nombres.Order(), terminales.Select(t => t.Nombre).Order());
    }

    [Fact]
    public async Task Nombre_de_terminal_vacio_no_se_acepta()
    {
        // Se valida antes de conectarse, así que no hace falta base de datos.
        await using var fabrica = new FabricaConexiones("Host=localhost");
        var repo = new TerminalRepositorio(fabrica);

        await Assert.ThrowsAsync<ArgumentException>(() => repo.RegistrarAccesoAsync("   "));
    }

    private async Task<long> ContarTerminales(string nombre)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM terminal WHERE nombre = @n", conexion);
        cmd.Parameters.AddWithValue("n", nombre);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
