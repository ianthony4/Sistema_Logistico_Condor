using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

/// <summary>Ventana Guías (F8) y diálogo Nueva guía (HU-10, T-94).</summary>
public class GuiasViewModelTests
{
    private static readonly DateOnly Hoy = DatosPrueba.Hoy();

    private sealed record Pantalla(
        MainViewModel Vm, GuiaRemisionRepositorioEnMemoria Guias, ComprobanteRepositorioEnMemoria Comprobantes,
        EnvioGuiaFalso Sunat, ImpresoraFalsa Ticketera);

    private static Pantalla Crear(bool conSunat = true)
    {
        var comprobantes = new ComprobanteRepositorioEnMemoria();
        var guias = new GuiaRemisionRepositorioEnMemoria();
        var ticketera = new ImpresoraFalsa();
        var sunat = new EnvioGuiaFalso();
        var vm = new MainViewModel(new ProductoRepositorioEnMemoria(DatosPrueba.Catalogo()),
            new ClienteRepositorioEnMemoria(DatosPrueba.Clientes()), comprobantes)
        {
            Impresion = new ImpresionViewModel(ticketera, new EmpresaRepositorioEnMemoria(), comprobantes),
        };
        vm.Buscador!.Espera = TimeSpan.Zero;
        vm.IniciarSesion(new Sesion(new Terminal { Id = 1, Nombre = "PC-1" }));
        vm.ConfigurarGuias(guias, new EmpresaRepositorioEnMemoria(), conSunat ? sunat : null);
        return new Pantalla(vm, guias, comprobantes, sunat, ticketera);
    }

    /// <summary>Llena el diálogo a mano: constructora, obra en Yanahuara, camioneta del local y 20 bolsas de cemento.</summary>
    private static async Task LlenarAsync(NuevaGuiaViewModel nueva)
    {
        await nueva.InicializarAsync();
        nueva.DocumentoDestinatario = "20131312955";
        await nueva.BuscarDestinatarioCommand.EjecutarAsync();
        nueva.LlegadaUbigeo = "040129";
        nueva.LlegadaDireccion = "Obra Av. Ejército 101, Yanahuara";
        nueva.PesoTexto = "850,5";
        nueva.Placa = "v1a-952";
        nueva.ConductorDni = "45879632";
        nueva.ConductorNombres = "Juan";
        nueva.ConductorApellidos = "Pérez Quispe";
        nueva.ConductorLicencia = "q45879632";
        nueva.Buscador.Texto = "cemento";
        await nueva.Buscador.UltimaBusqueda;
        nueva.Buscador.Elegir(nueva.Buscador.Seleccionado);
        nueva.CantidadTexto = "20";
        Assert.True(nueva.Agregar());
    }

    [Fact]
    public async Task Emitir_guarda_la_guia_T001_con_lo_escrito()
    {
        var (vm, guias, _, _, _) = Crear();
        var nueva = vm.CrearGuias()!.CrearNuevaGuia();
        await LlenarAsync(nueva);
        bool? cerrado = null;
        nueva.Cerrar += e => cerrado = e;

        await nueva.EmitirCommand.EjecutarAsync();

        Assert.Null(nueva.Error);
        Assert.True(cerrado);
        var guia = Assert.Single(guias.Guias);
        Assert.Same(guia, nueva.Emitida);
        Assert.Equal("T001-00000001", guia.NumeroCompleto);
        Assert.Equal("Constructora Valle de Majes S.A.C.", guia.DestinatarioNombre); // lo trajo del cliente
        Assert.Equal("040520", guia.PartidaUbigeo); // del local
        Assert.Equal("Majes, Caylloma - Arequipa", guia.PartidaDireccion);
        Assert.Equal(850.5m, guia.PesoBrutoKg);
        Assert.Equal("V1A952", guia.VehiculoPlaca);
        Assert.Equal(1, guia.TerminalId);
        Assert.Equal(20, Assert.Single(guia.Detalle).Cantidad);
    }

    [Fact]
    public async Task Con_datos_faltantes_no_emite_y_dice_que_falta()
    {
        var (vm, guias, _, _, _) = Crear();
        var nueva = vm.CrearGuias()!.CrearNuevaGuia();
        await LlenarAsync(nueva);
        nueva.EsPublico = true;          // sin RUC del transportista
        nueva.LlegadaUbigeo = "0401";

        await nueva.EmitirCommand.EjecutarAsync();

        Assert.Empty(guias.Guias);
        Assert.Contains("RUC de la empresa de transportes", nueva.Error);
        Assert.Contains("ubigeo del punto de llegada", nueva.Error);
    }

    [Fact]
    public async Task Trae_el_destinatario_y_los_productos_de_una_factura()
    {
        var (vm, _, comprobantes, _, _) = Crear();
        var factura = await comprobantes.EmitirAsync(DatosPrueba.FacturaObra());
        var nueva = vm.CrearGuias()!.CrearNuevaGuia();
        await nueva.InicializarAsync();

        nueva.ComprobanteTexto = factura.NumeroCompleto;
        await nueva.TraerComprobanteCommand.EjecutarAsync();

        Assert.Null(nueva.Error);
        Assert.Same(factura, nueva.Comprobante);
        Assert.Equal("20131312955", nueva.DocumentoDestinatario);
        Assert.Equal("Av. Principal 450, Majes", nueva.LlegadaDireccion);
        Assert.Equal(new[] { 20m, 10m }, nueva.Lineas.Select(l => l.Cantidad));
        Assert.Equal(MotivoTraslado.Venta, nueva.Motivo.Codigo);
        Assert.Contains("F001-00000001", nueva.Aviso);
        Assert.Equal("F001-00000001", nueva.Armar().ComprobanteNumero);
    }

    [Fact]
    public async Task Un_comprobante_que_no_existe_se_avisa()
    {
        var (vm, _, _, _, _) = Crear();
        var nueva = vm.CrearGuias()!.CrearNuevaGuia();

        nueva.ComprobanteTexto = "F001-999";
        await nueva.TraerComprobanteCommand.EjecutarAsync();

        Assert.Contains("No se encontró", nueva.Error);
        Assert.Empty(nueva.Lineas);
    }

    [Fact]
    public async Task Al_emitir_la_ventana_la_imprime_la_envia_y_la_deja_elegida()
    {
        var (vm, guias, _, sunat, ticketera) = Crear();
        var ventana = vm.CrearGuias()!;
        var nueva = ventana.CrearNuevaGuia();
        await LlenarAsync(nueva);
        await nueva.EmitirCommand.EjecutarAsync();

        await ventana.GuiaEmitidaAsync(nueva.Emitida!);

        Assert.Contains("GUÍA DE REMISIÓN", Assert.Single(ticketera.Impresos).ComoTexto());
        Assert.Single(sunat.Enviadas);
        Assert.Equal(EstadoSunat.Aceptado, guias.Guias[0].EstadoSunat);
        Assert.Same(nueva.Emitida, ventana.Seleccionada);
        Assert.Contains("Aceptada por la SUNAT", ventana.Mensaje);
        Assert.Equal(1, guias.ImpresionesRegistradas[nueva.Emitida!.Id]);
    }

    [Fact]
    public async Task Sin_internet_la_guia_se_imprime_y_queda_pendiente_y_luego_se_envia()
    {
        var (vm, guias, _, sunat, ticketera) = Crear();
        sunat.SinInternet = true;
        var ventana = vm.CrearGuias()!;
        var nueva = ventana.CrearNuevaGuia();
        await LlenarAsync(nueva);
        await nueva.EmitirCommand.EjecutarAsync();

        await ventana.GuiaEmitidaAsync(nueva.Emitida!);
        Assert.Single(ticketera.Impresos);
        Assert.Equal(EstadoSunat.Pendiente, guias.Guias[0].EstadoSunat);
        Assert.Contains("Pendiente de envío", ventana.Mensaje);
        Assert.Equal(1, ventana.PendientesSunat);

        sunat.SinInternet = false;
        await ventana.EnviarPendientesCommand.EjecutarAsync();

        Assert.Equal(EstadoSunat.Aceptado, guias.Guias[0].EstadoSunat);
        Assert.Equal("Guías: 1 aceptadas, 0 rechazadas, 0 en proceso.", ventana.Mensaje);
        Assert.Equal(0, ventana.PendientesSunat);
    }

    [Fact]
    public async Task Si_la_SUNAT_sigue_procesando_la_proxima_vuelta_consulta_el_ticket()
    {
        var (vm, guias, _, sunat, _) = Crear();
        sunat.Demora = true;
        var ventana = vm.CrearGuias()!;
        var nueva = ventana.CrearNuevaGuia();
        await LlenarAsync(nueva);
        await nueva.EmitirCommand.EjecutarAsync();
        await ventana.GuiaEmitidaAsync(nueva.Emitida!);
        Assert.Equal("ticket-1", guias.Guias[0].Ticket);

        var resultado = await vm.EnvioGuias!.ReenviarPendientesAsync();

        Assert.Equal(1, resultado.Aceptadas);
        Assert.Equal(new[] { "ticket-1" }, sunat.Consultas);
        Assert.Single(sunat.Enviadas); // no se envió dos veces
    }

    [Fact]
    public async Task Sin_la_API_de_guias_configurada_se_emite_e_imprime_y_queda_pendiente()
    {
        var (vm, guias, _, _, ticketera) = Crear(conSunat: false);
        var ventana = vm.CrearGuias()!;
        var nueva = ventana.CrearNuevaGuia();
        await LlenarAsync(nueva);
        await nueva.EmitirCommand.EjecutarAsync();

        await ventana.GuiaEmitidaAsync(nueva.Emitida!);

        Assert.Single(ticketera.Impresos);
        Assert.Equal(EstadoSunat.Pendiente, guias.Guias[0].EstadoSunat);
        Assert.Contains("no está configurada", guias.Guias[0].MensajeSunat);
        Assert.False(ventana.EnvioConfigurado);
    }

    [Fact]
    public async Task Busca_por_destinatario_y_reimprime_como_copia()
    {
        var (vm, guias, _, _, ticketera) = Crear();
        var guia = await guias.EmitirAsync(DatosPrueba.GuiaObra());
        guia.Impresiones = 1;
        var ventana = vm.CrearGuias()!;

        ventana.Texto = "constructora";
        await ventana.BuscarAsync();
        await ventana.ReimprimirCommand.EjecutarAsync();

        Assert.Same(guia, Assert.Single(ventana.Guias));
        Assert.Contains("COPIA", Assert.Single(ticketera.Impresos).ComoTexto());
        Assert.Contains("reimpresa", ventana.Mensaje);
    }

    [Fact]
    public async Task Sin_conexion_al_emitir_conserva_lo_escrito()
    {
        var (vm, guias, _, _, _) = Crear();
        var nueva = vm.CrearGuias()!.CrearNuevaGuia();
        await LlenarAsync(nueva);
        guias.Error = new SinConexionException(new TimeoutException());

        await nueva.EmitirCommand.EjecutarAsync();

        Assert.Contains("NO se emitió", nueva.Error);
        Assert.Single(nueva.Lineas);
    }

    [Fact]
    public void Sin_configurar_las_guias_la_pantalla_principal_no_abre_la_ventana()
    {
        var vm = new MainViewModel(new ProductoRepositorioEnMemoria(), new ClienteRepositorioEnMemoria(), new ComprobanteRepositorioEnMemoria());

        Assert.Null(vm.CrearGuias());
    }
}
