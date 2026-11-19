using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;
using Condor.Sunat;

namespace Condor.Pruebas.Presentacion;

/// <summary>Reenvío automático en la pantalla de venta y el Historial (HU-17, T-58).</summary>
public class ReenvioSunatViewModelTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 10, 0, 0, TimeSpan.FromHours(-5));

    private sealed record Pantalla(MainViewModel Vm, EnvioSunatFalso Sunat, EnvioSunatRepositorioEnMemoria Registro);

    /// <summary>Pantalla con la SUNAT falsa; el reenvío usa la misma SUNAT y no espera entre intentos.</summary>
    private static Pantalla Crear(bool reenvia = true)
    {
        var comprobantes = new ComprobanteRepositorioEnMemoria();
        var sunat = new EnvioSunatFalso();
        var registro = new EnvioSunatRepositorioEnMemoria { MasAntiguo = Ahora.AddHours(-3) };
        var reenvio = reenvia
            ? new ReenvioPendientes(sunat, registro, comprobantes, new EmpresaRepositorioEnMemoria(), "PC-1", TimeSpan.Zero)
            : null;
        var vm = new MainViewModel(new ProductoRepositorioEnMemoria(DatosPrueba.Catalogo()),
            new ClienteRepositorioEnMemoria(DatosPrueba.Clientes()), comprobantes)
        {
            Impresion = new ImpresionViewModel(new ImpresoraFalsa(), new EmpresaRepositorioEnMemoria(), comprobantes),
            EnvioSunat = new EnvioSunatViewModel(sunat, new EmpresaRepositorioEnMemoria(), registro),
            ReenvioSunat = new ReenvioSunatViewModel(reenvio, registro, () => Ahora),
        };
        vm.Buscador!.Espera = TimeSpan.Zero;
        vm.IniciarSesion(new Sesion(new Terminal { Id = 1, Nombre = "PC-1" }));
        return new Pantalla(vm, sunat, registro);
    }

    private static async Task<Comprobante> VenderAsync(MainViewModel vm)
    {
        vm.Buscador!.Texto = "clavo";
        await vm.Buscador.UltimaBusqueda;
        vm.Buscador.Elegir(vm.Buscador.Seleccionado);
        Assert.True(vm.Venta.Agregar());
        await vm.Emision!.EmitirCommand.EjecutarAsync();
        await Task.WhenAll(vm.UltimaImpresion, vm.UltimoEnvioSunat);
        return vm.Emision.Ultimo!;
    }

    [Fact]
    public async Task Sin_internet_el_pie_dice_cuantos_quedaron_pendientes()
    {
        var (vm, sunat, _) = Crear();
        sunat.SinInternet = true;

        await VenderAsync(vm);
        await VenderAsync(vm);

        Assert.Equal(2, vm.ReenvioSunat!.Pendientes);
        Assert.Equal("2 pendientes SUNAT", vm.ReenvioSunat.Aviso);
        Assert.False(vm.ReenvioSunat.PorVencer);
    }

    [Fact]
    public async Task Al_volver_internet_la_vuelta_automatica_los_envia_y_el_aviso_desaparece()
    {
        var (vm, sunat, registro) = Crear();
        sunat.SinInternet = true;
        var boleta = await VenderAsync(vm);
        sunat.SinInternet = false;

        var resultado = await vm.ReenvioSunat!.ReenviarAsync();

        Assert.Equal(1, resultado!.Aceptados);
        Assert.Equal(EstadoSunat.Aceptado, registro.Estado(boleta.Id));
        Assert.Null(vm.ReenvioSunat.Aviso);
        Assert.Equal("Reenvío a la SUNAT: 1 aceptado.", vm.Mensaje);
        Assert.StartsWith("Reenvío a la SUNAT: 1 aceptado.", vm.ReenvioSunat.UltimoResultado);
    }

    [Fact]
    public async Task Con_un_pendiente_de_hace_mas_de_dos_dias_el_aviso_dice_que_vence_el_plazo()
    {
        var (vm, sunat, registro) = Crear();
        sunat.SinInternet = true;
        await VenderAsync(vm);
        registro.MasAntiguo = Ahora.AddDays(-2).AddHours(-1);

        await vm.ReenvioSunat!.ActualizarAsync();

        Assert.True(vm.ReenvioSunat.PorVencer);
        Assert.Contains("vence el plazo de 3 días", vm.ReenvioSunat.Aviso);
        Assert.Contains("14/11", vm.ReenvioSunat.Aviso);
    }

    [Fact]
    public async Task Una_PC_sin_la_SUNAT_configurada_cuenta_los_pendientes_pero_no_reenvia()
    {
        var (vm, sunat, registro) = Crear(reenvia: false);
        sunat.SinInternet = true;
        await VenderAsync(vm);
        sunat.SinInternet = false;

        var resultado = await vm.ReenvioSunat!.ReenviarAsync();

        Assert.Null(resultado);
        Assert.False(vm.ReenvioSunat.ReenviarAhoraCommand.CanExecute(null));
        Assert.Equal("1 pendiente SUNAT", vm.ReenvioSunat.Aviso);
        Assert.Contains("la PC que tiene la SUNAT configurada", vm.ReenvioSunat.Detalle);
        Assert.Single(await registro.ListarPendientesAsync());
    }

    [Fact]
    public async Task Sin_la_PC_principal_avisa_la_perdida_de_conexion_y_no_falla()
    {
        var (vm, _, registro) = Crear();
        vm.Conexion = new EstadoConexionViewModel(_ => Task.FromResult(true));
        registro.Error = new SinConexionException(new TimeoutException());

        var resultado = await vm.ReenvioSunat!.ReenviarAsync();

        Assert.Null(resultado);
        Assert.False(vm.Conexion.Conectado);
        Assert.False(vm.ReenvioSunat.Reenviando);
    }

    [Fact]
    public async Task El_Historial_reenvia_los_pendientes_en_el_acto()
    {
        var (vm, sunat, _) = Crear();
        sunat.SinInternet = true;
        await VenderAsync(vm);
        await VenderAsync(vm);
        sunat.SinInternet = false;
        var historial = vm.CrearComprobantesRecientes()!;
        await historial.CargarAsync();
        Assert.True(historial.PuedeReenviar);
        Assert.True(historial.ReenviarCommand.CanExecute(null));

        await historial.ReenviarCommand.EjecutarAsync();

        Assert.Equal("Reenvío a la SUNAT: 2 aceptados.", historial.Mensaje);
        Assert.Equal(0, historial.PendientesSunat);
        Assert.False(historial.ReenviarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Sin_reenvio_el_Historial_no_ofrece_reenviar()
    {
        var (vm, sunat, _) = Crear(reenvia: false);
        sunat.SinInternet = true;
        await VenderAsync(vm);
        var historial = vm.CrearComprobantesRecientes()!;
        await historial.CargarAsync();

        Assert.False(historial.PuedeReenviar);
        Assert.False(historial.ReenviarCommand.CanExecute(null));
    }
}
