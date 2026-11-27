using Condor.Datos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;
using Npgsql;

namespace Condor.Pruebas.Aceptacion;

/// <summary>
/// HU-10: emitir guías de remisión electrónicas.
/// Criterio 1: si hay mercadería que trasladar a una obra (con o sin factura),
/// al completar la guía y pulsar Emitir el sistema la guarda con la serie
/// T001 y el siguiente correlativo, la imprime y la envía a la SUNAT.
/// Criterio 2: si faltan datos obligatorios (transportista, vehículo y
/// conductor, ubigeo, peso...), el sistema no la emite y dice qué falta.
/// Criterio 3: si la SUNAT aún no responde o no hay internet, la guía queda
/// pendiente y el sistema la reenvía o consulta su ticket hasta guardar el CDR.
/// </summary>
/// <remarks>Usa la base de pruebas, la ticketera falsa y la API de guías falsa.</remarks>
[Collection(BaseDatos.Nombre)]
public class HU10_GuiasRemisionTests
{
    private readonly BaseDatosFixture _bd;

    public HU10_GuiasRemisionTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task CA1_la_guia_de_una_factura_se_guarda_con_T001_se_imprime_y_se_envia()
    {
        var (vm, sunat, ticketera) = await PantallaAsync();
        var factura = await VenderFacturaAsync(vm);
        var antes = await UltimoCorrelativoAsync();
        var ventana = vm.CrearGuias()!;
        var nueva = ventana.CrearNuevaGuia();
        await nueva.InicializarAsync();

        nueva.ComprobanteTexto = factura.NumeroCompleto;
        await nueva.TraerComprobanteCommand.EjecutarAsync();
        Completar(nueva);
        await nueva.EmitirCommand.EjecutarAsync();
        await ventana.GuiaEmitidaAsync(nueva.Emitida!);

        Assert.Null(nueva.Error);
        var guia = (await new GuiaRemisionRepositorio(_bd.Conexiones).ObtenerAsync(nueva.Emitida!.Id))!;
        Assert.Equal(antes + 1, guia.Numero);
        Assert.Equal("T001", guia.SerieCodigo);
        Assert.Equal(factura.NumeroCompleto, guia.ComprobanteNumero);
        Assert.Equal(factura.Cliente!.NumeroDocumento, guia.DestinatarioDocumento);
        Assert.Equal(factura.Detalle.Select(l => l.Cantidad), guia.Detalle.Select(l => l.Cantidad));
        Assert.Equal(EstadoSunat.Aceptado, guia.EstadoSunat);
        Assert.Equal($"ticket-{guia.Numero}", guia.Ticket);
        Assert.Equal(1, guia.Impresiones);
        Assert.Single(sunat.Enviadas);
        var ticket = Assert.Single(ticketera.Impresos, t => t.ComoTexto().Contains("GUÍA DE REMISIÓN")).ComoTexto();
        Assert.Contains(guia.NumeroCompleto, ticket);
        Assert.Contains($"Comprobante: Factura {factura.NumeroCompleto}", ticket);
    }

    [FactConBD]
    public async Task CA1_tambien_se_emite_a_mano_en_transporte_publico_sin_factura()
    {
        var (vm, _, _) = await PantallaAsync();
        var ventana = vm.CrearGuias()!;
        var nueva = ventana.CrearNuevaGuia();
        await nueva.InicializarAsync();
        nueva.DocumentoDestinatario = "45879632";
        nueva.NombreDestinatario = "Juan Pérez Quispe";
        nueva.Motivo = NuevaGuiaViewModel.Motivos.Single(m => m.Codigo == MotivoTraslado.Otros);
        nueva.DescripcionMotivo = "Préstamo de andamios a la obra";
        nueva.EsPublico = true;
        nueva.TransportistaRuc = "20100137390";
        nueva.TransportistaRazonSocial = "Transportes Arequipa S.A.";
        nueva.LlegadaUbigeo = "040126";
        nueva.LlegadaDireccion = "Calle Los Pinos 214, Cerro Colorado";
        nueva.PesoTexto = "120";
        await AgregarAsync(nueva, "Clavo", "5");

        await nueva.EmitirCommand.EjecutarAsync();
        await ventana.GuiaEmitidaAsync(nueva.Emitida!);

        var guia = (await new GuiaRemisionRepositorio(_bd.Conexiones).ObtenerAsync(nueva.Emitida!.Id))!;
        Assert.Equal(ModalidadTraslado.Publico, guia.Modalidad);
        Assert.Equal("20100137390", guia.TransportistaRuc);
        Assert.Null(guia.VehiculoPlaca);
        Assert.Equal(TipoDocumento.Dni, guia.DestinatarioTipo);
        Assert.Equal("Préstamo de andamios a la obra", guia.DescripcionMotivo);
        Assert.Null(guia.ComprobanteId);
    }

    [FactConBD]
    public async Task CA2_sin_vehiculo_conductor_ni_peso_no_se_emite_y_no_se_gasta_numero()
    {
        var (vm, sunat, _) = await PantallaAsync();
        var factura = await VenderFacturaAsync(vm);
        var antes = await UltimoCorrelativoAsync();
        var nueva = vm.CrearGuias()!.CrearNuevaGuia();
        await nueva.InicializarAsync();
        nueva.ComprobanteTexto = factura.NumeroCompleto;
        await nueva.TraerComprobanteCommand.EjecutarAsync();
        nueva.LlegadaUbigeo = "040129";

        await nueva.EmitirCommand.EjecutarAsync();

        Assert.Null(nueva.Emitida);
        Assert.Contains("placa", nueva.Error);
        Assert.Contains("DNI del conductor", nueva.Error);
        Assert.Contains("licencia", nueva.Error);
        Assert.Contains("peso", nueva.Error);
        Assert.Equal(antes, await UltimoCorrelativoAsync());
        Assert.Empty(sunat.Enviadas);
    }

    [FactConBD]
    public async Task CA3_sin_internet_queda_pendiente_y_al_volver_se_envia_y_guarda_el_CDR()
    {
        var (vm, sunat, ticketera) = await PantallaAsync();
        sunat.SinInternet = true;
        var ventana = vm.CrearGuias()!;
        var guia = await EmitirAManoAsync(ventana);

        var repo = new GuiaRemisionRepositorio(_bd.Conexiones);
        var pendiente = (await repo.ObtenerAsync(guia.Id))!;
        Assert.Equal(EstadoSunat.Pendiente, pendiente.EstadoSunat);
        Assert.Contains("No hay conexión", pendiente.MensajeSunat);
        Assert.Single(ticketera.Impresos); // la guía acompaña a la mercadería aunque no haya internet

        sunat.SinInternet = false;
        await ReenviarHastaAsync(vm, guia.Id);

        Assert.Equal(EstadoSunat.Aceptado, (await repo.ObtenerAsync(guia.Id))!.EstadoSunat);
    }

    [FactConBD]
    public async Task CA3_si_la_SUNAT_esta_procesando_se_guarda_el_ticket_y_luego_se_consulta()
    {
        var (vm, sunat, _) = await PantallaAsync();
        sunat.Demora = true;
        var ventana = vm.CrearGuias()!;
        var guia = await EmitirAManoAsync(ventana);
        var repo = new GuiaRemisionRepositorio(_bd.Conexiones);
        var enProceso = (await repo.ObtenerAsync(guia.Id))!;

        await ReenviarHastaAsync(vm, guia.Id);

        Assert.Equal(EstadoSunat.Pendiente, enProceso.EstadoSunat);
        Assert.Equal($"ticket-{guia.Numero}", enProceso.Ticket);
        Assert.Contains($"ticket-{guia.Numero}", sunat.Consultas);
        Assert.Single(sunat.Enviadas, g => g.Id == guia.Id); // se consultó el ticket, no se reenvió
        Assert.Equal(EstadoSunat.Aceptado, (await repo.ObtenerAsync(guia.Id))!.EstadoSunat);
    }

    /// <summary>Vueltas del reenvío hasta que la guía deja de estar pendiente (otras pruebas dejan pendientes).</summary>
    private async Task ReenviarHastaAsync(MainViewModel vm, long id)
    {
        var repo = new GuiaRemisionRepositorio(_bd.Conexiones);
        for (var vuelta = 0; vuelta < 20 && (await repo.ObtenerAsync(id))!.EstadoSunat == EstadoSunat.Pendiente; vuelta++)
            await vm.EnvioGuias!.ReenviarPendientesAsync();
    }

    private async Task<GuiaRemision> EmitirAManoAsync(GuiasViewModel ventana)
    {
        var nueva = ventana.CrearNuevaGuia();
        await nueva.InicializarAsync();
        nueva.DocumentoDestinatario = DatosPrueba.ClienteRuc().NumeroDocumento;
        nueva.NombreDestinatario = "Constructora Valle de Majes S.A.C.";
        nueva.LlegadaUbigeo = "040129";
        nueva.LlegadaDireccion = "Obra Av. Ejército 101, Yanahuara";
        Completar(nueva);
        await AgregarAsync(nueva, "Cemento", "20");
        await nueva.EmitirCommand.EjecutarAsync();
        Assert.Null(nueva.Error);
        await ventana.GuiaEmitidaAsync(nueva.Emitida!);
        return nueva.Emitida!;
    }

    /// <summary>La camioneta del local, su conductor, la obra y el peso.</summary>
    private static void Completar(NuevaGuiaViewModel nueva)
    {
        nueva.LlegadaUbigeo = "040129";
        if (string.IsNullOrWhiteSpace(nueva.LlegadaDireccion))
            nueva.LlegadaDireccion = "Obra Av. Ejército 101, Yanahuara";
        nueva.PesoTexto = "1235.5";
        nueva.Placa = "V1A-952";
        nueva.ConductorDni = "45879632";
        nueva.ConductorNombres = "Juan";
        nueva.ConductorApellidos = "Pérez Quispe";
        nueva.ConductorLicencia = "Q45879632";
    }

    /// <summary>Pantalla principal contra la base de pruebas con ticketera y API de guías falsas.</summary>
    private async Task<(MainViewModel Vm, EnvioGuiaFalso Sunat, ImpresoraFalsa Ticketera)> PantallaAsync()
    {
        var pc = _bd.Conexiones;
        var productos = new ProductoRepositorio(pc);
        foreach (var p in DatosPrueba.Catalogo())
        {
            p.Nombre = DatosPrueba.NombreUnico(p.Nombre.Split(' ')[0]);
            await productos.RegistrarAsync(p);
        }

        var comprobantes = new ComprobanteRepositorio(pc);
        var ticketera = new ImpresoraFalsa();
        var sunat = new EnvioGuiaFalso();
        var vm = new MainViewModel(productos, new ClienteRepositorio(pc), comprobantes)
        {
            Impresion = new ImpresionViewModel(ticketera, new EmpresaRepositorio(pc), comprobantes),
        };
        vm.Buscador!.Espera = TimeSpan.Zero;
        vm.IniciarSesion(new Sesion(await new TerminalRepositorio(pc).RegistrarAccesoAsync("PC-HU10")));
        vm.ConfigurarGuias(new GuiaRemisionRepositorio(pc), new EmpresaRepositorio(pc), sunat);
        return (vm, sunat, ticketera);
    }

    /// <summary>Factura a una constructora con RUC nuevo: 20 bolsas de cemento y 10 varillas.</summary>
    private async Task<Comprobante> VenderFacturaAsync(MainViewModel vm)
    {
        var ruc = DatosPrueba.RucNuevo();
        await new ClienteRepositorio(_bd.Conexiones).RegistrarAsync(DatosPrueba.ClienteRuc(ruc));
        await AgregarAsync(vm, "Cemento", "20");
        await AgregarAsync(vm, "Fierro", "10");
        vm.Emision!.EsFactura = true;
        vm.Cliente!.Documento = ruc;
        await vm.Cliente.BuscarCommand.EjecutarAsync();
        await vm.Emision.EmitirCommand.EjecutarAsync();
        await vm.UltimaImpresion;
        Assert.Null(vm.Emision.Error);
        return vm.Emision.Ultimo!;
    }

    private static async Task AgregarAsync(MainViewModel vm, string texto, string cantidad)
    {
        vm.Buscador!.Texto = texto;
        await vm.Buscador.UltimaBusqueda;
        Assert.NotNull(vm.Buscador.Seleccionado);
        vm.Buscador.Elegir(vm.Buscador.Seleccionado);
        vm.Venta.CantidadTexto = cantidad;
        Assert.True(vm.Venta.Agregar());
    }

    private static async Task AgregarAsync(NuevaGuiaViewModel nueva, string texto, string cantidad)
    {
        nueva.Buscador.Texto = texto;
        await nueva.Buscador.UltimaBusqueda;
        Assert.NotNull(nueva.Buscador.Seleccionado);
        nueva.Buscador.Elegir(nueva.Buscador.Seleccionado);
        nueva.CantidadTexto = cantidad;
        Assert.True(nueva.Agregar());
    }

    private async Task<long> UltimoCorrelativoAsync()
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand("SELECT ultimo_correlativo FROM serie WHERE codigo = 'T001'", conexion);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
