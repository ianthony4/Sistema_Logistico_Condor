using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

public class EnvioSunatViewModelTests
{
    private sealed record Pantalla(MainViewModel Vm, EnvioSunatFalso Sunat, EnvioSunatRepositorioEnMemoria Registro, ImpresoraFalsa Ticketera);

    private static Pantalla Crear(bool conSunat = true)
    {
        var comprobantes = new ComprobanteRepositorioEnMemoria();
        var ticketera = new ImpresoraFalsa();
        var sunat = new EnvioSunatFalso();
        var registro = new EnvioSunatRepositorioEnMemoria();
        var vm = new MainViewModel(new ProductoRepositorioEnMemoria(DatosPrueba.Catalogo()),
            new ClienteRepositorioEnMemoria(DatosPrueba.Clientes()), comprobantes)
        {
            Impresion = new ImpresionViewModel(ticketera, new EmpresaRepositorioEnMemoria(), comprobantes),
            EnvioSunat = new EnvioSunatViewModel(conSunat ? sunat : null, new EmpresaRepositorioEnMemoria(), registro),
        };
        vm.Buscador!.Espera = TimeSpan.Zero;
        vm.IniciarSesion(new Sesion(new Terminal { Id = 1, Nombre = "PC-1" }));
        return new Pantalla(vm, sunat, registro, ticketera);
    }

    /// <summary>Vende una bolsa de cemento y emite; no espera a la SUNAT.</summary>
    private static async Task<Comprobante> EmitirAsync(MainViewModel vm)
    {
        vm.Buscador!.Texto = "cemento";
        await vm.Buscador.UltimaBusqueda;
        vm.Buscador.Elegir(vm.Buscador.Seleccionado);
        Assert.True(vm.Venta.Agregar());
        await vm.Emision!.EmitirCommand.EjecutarAsync();
        Assert.Null(vm.Emision.Error);
        await vm.UltimaImpresion;
        return vm.Emision.Ultimo!;
    }

    [Fact]
    public async Task Al_emitir_se_envia_a_la_SUNAT_y_se_muestra_aceptada()
    {
        var (vm, sunat, registro, ticketera) = Crear();

        var boleta = await EmitirAsync(vm);
        await vm.UltimoEnvioSunat;

        Assert.Same(boleta, Assert.Single(sunat.Enviados));
        Assert.Equal(EstadoSunat.Aceptado, vm.EnvioSunat!.Estado);
        Assert.Equal($"{boleta.NumeroCompleto}: Aceptada por la SUNAT.", vm.EnvioSunat.Mensaje);
        Assert.Equal(EstadoSunat.Aceptado, registro.Estado(boleta.Id));
        Assert.Equal(EstadoSunat.Aceptado, boleta.EstadoSunat);
        Assert.Single(ticketera.Impresos);
    }

    [Fact]
    public async Task Un_rechazo_se_muestra_con_su_codigo_y_motivo()
    {
        var (vm, sunat, registro, _) = Crear();
        sunat.Respuesta = EnvioSunatFalso.Rechazada();

        var boleta = await EmitirAsync(vm);
        await vm.UltimoEnvioSunat;

        Assert.Equal(EstadoSunat.Rechazado, vm.EnvioSunat!.Estado);
        Assert.Contains("Rechazada por la SUNAT (2017)", vm.EnvioSunat.Mensaje);
        Assert.Equal(EstadoSunat.Rechazado, registro.Estado(boleta.Id));
    }

    [Fact]
    public async Task Sin_internet_imprime_igual_y_queda_pendiente_con_el_motivo()
    {
        var (vm, sunat, registro, ticketera) = Crear();
        sunat.SinInternet = true;

        var boleta = await EmitirAsync(vm);
        await vm.UltimoEnvioSunat;

        Assert.Single(ticketera.Impresos);
        Assert.Equal(EstadoSunat.Pendiente, vm.EnvioSunat!.Estado);
        Assert.Contains("pendiente de envío", vm.EnvioSunat.Mensaje);
        Assert.Contains("No hay conexión a internet.", vm.EnvioSunat.Mensaje);
        Assert.Equal("No hay conexión a internet.", registro.UltimoError[boleta.Id]);
        Assert.Contains(boleta.Id, await registro.ListarPendientesAsync());
    }

    [Fact]
    public async Task Mientras_la_SUNAT_no_responde_se_puede_emitir_la_venta_siguiente()
    {
        var (vm, sunat, _, ticketera) = Crear();
        sunat.Demora = new TaskCompletionSource();

        var primera = await EmitirAsync(vm);
        Assert.True(vm.EnvioSunat!.Enviando);
        Assert.False(vm.UltimoEnvioSunat.IsCompleted);

        var segunda = await EmitirAsync(vm); // no espera a la SUNAT

        Assert.Equal(primera.Numero + 1, segunda.Numero);
        Assert.Equal(2, ticketera.Impresos.Count);
        sunat.Demora.SetResult();
        await vm.UltimoEnvioSunat;
        Assert.Equal(2, sunat.Enviados.Count);
    }

    [Fact]
    public async Task Sin_la_SUNAT_configurada_queda_pendiente_y_lo_dice()
    {
        var (vm, _, registro, _) = Crear(conSunat: false);

        var boleta = await EmitirAsync(vm);
        await vm.UltimoEnvioSunat;

        Assert.Equal("SUNAT sin configurar", vm.EnvioSunat!.Ambiente);
        Assert.Equal(EstadoSunat.Pendiente, vm.EnvioSunat.Estado);
        Assert.Contains("no está configurada", vm.EnvioSunat.Mensaje);
        Assert.Equal(1, registro.Intentos[boleta.Id]);
    }

    [Fact]
    public async Task Si_no_se_puede_anotar_la_respuesta_avisa_la_perdida_de_conexion_sin_romper()
    {
        var registro = new EnvioSunatRepositorioEnMemoria { Error = new SinConexionException(new IOException()) };
        var envio = new EnvioSunatViewModel(new EnvioSunatFalso(), new EmpresaRepositorioEnMemoria(), registro);
        var avisos = 0;
        envio.ConexionPerdida += () => avisos++;
        var boleta = new Comprobante { Id = 9, SerieCodigo = "B001", Numero = 9, Total = 10m };

        var respuesta = await envio.EnviarAsync(boleta);

        Assert.True(respuesta!.Aceptado);
        Assert.Equal(1, avisos);
        Assert.Equal(EstadoSunat.Aceptado, envio.Estado);
    }

    [Fact]
    public async Task Un_certificado_vencido_deja_el_comprobante_pendiente_con_el_motivo()
    {
        var registro = new EnvioSunatRepositorioEnMemoria();
        var envio = new EnvioSunatViewModel(new SunatQueFalla(), new EmpresaRepositorioEnMemoria(), registro);
        var boleta = new Comprobante { Id = 3, SerieCodigo = "B001", Numero = 3, Total = 10m };

        Assert.Null(await envio.EnviarAsync(boleta));

        Assert.Equal(EstadoSunat.Pendiente, envio.Estado);
        Assert.Contains("venció", envio.Mensaje);
        Assert.Contains("venció", registro.UltimoError[3]);
        Assert.False(envio.Enviando);
    }

    private sealed class SunatQueFalla : IEnvioSunat
    {
        public string Ambiente => "prueba";

        public Task<RespuestaSunat> EnviarAsync(Empresa empresa, Comprobante comprobante, CancellationToken ct = default) =>
            throw new InvalidOperationException("El certificado digital venció el 01/11/2026.");
    }
}
