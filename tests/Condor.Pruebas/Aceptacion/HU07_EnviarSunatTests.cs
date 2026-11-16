using Condor.Datos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;
using Condor.Sunat;
using Npgsql;

namespace Condor.Pruebas.Aceptacion;

/// <summary>
/// HU-07: enviar a la SUNAT y guardar el CDR.
/// Criterio 1: si se emitió una boleta o factura y hay internet, el sistema
/// genera el XML UBL 2.1 firmado, lo envía, recibe el CDR, guarda su estado
/// (aceptado o rechazado) y lo muestra en la pantalla de venta.
/// Criterio 2: si no hay internet o la SUNAT no responde, el comprobante se
/// imprime igual y queda pendiente de envío, sin bloquear la venta siguiente.
/// </summary>
/// <remarks>
/// Las pruebas [FactConBD] usan la SUNAT falsa; las [FactSunatBeta] envían
/// de verdad al ambiente beta de la SUNAT (usuario MODDATOS).
/// </remarks>
[Collection(BaseDatos.Nombre)]
public class HU07_EnviarSunatTests
{
    private readonly BaseDatosFixture _bd;

    public HU07_EnviarSunatTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task CA1_la_boleta_aceptada_queda_ACEPTADO_con_su_CDR_y_se_ve_en_pantalla()
    {
        var (vm, sunat, _) = await PantallaAsync();

        var boleta = await VenderAsync(vm);
        await vm.UltimoEnvioSunat;

        Assert.Same(boleta, Assert.Single(sunat.Enviados));
        Assert.Equal(EstadoSunat.Aceptado, vm.EnvioSunat!.Estado);
        Assert.Contains("Aceptada por la SUNAT", vm.EnvioSunat.Mensaje);
        var fila = await LeerAsync(boleta.Id);
        Assert.Equal(EstadoSunat.Aceptado, fila.Estado);
        Assert.True(fila.TieneCdr);
        Assert.Equal("0", fila.Codigo);
    }

    [FactConBD]
    public async Task CA1_la_boleta_rechazada_queda_RECHAZADO_y_el_historial_muestra_el_motivo()
    {
        var (vm, sunat, _) = await PantallaAsync();
        sunat.Respuesta = EnvioSunatFalso.Rechazada("2800", "El dato ingresado en el tipo de documento de identidad del receptor no esta permitido.");

        var boleta = await VenderAsync(vm);
        await vm.UltimoEnvioSunat;

        Assert.Equal(EstadoSunat.Rechazado, vm.EnvioSunat!.Estado);
        Assert.Contains("2800", vm.EnvioSunat.Mensaje);
        var historial = vm.CrearComprobantesRecientes()!;
        await historial.CargarAsync();
        var fila = historial.Comprobantes.First(c => c.Id == boleta.Id);
        Assert.Equal(EstadoSunat.Rechazado, fila.EstadoSunat);
        Assert.StartsWith("El dato ingresado", fila.MensajeSunat);
    }

    [FactConBD]
    public async Task CA2_sin_internet_imprime_igual_y_queda_pendiente_de_envio()
    {
        var (vm, sunat, ticketera) = await PantallaAsync();
        sunat.SinInternet = true;

        var boleta = await VenderAsync(vm);
        await vm.UltimoEnvioSunat;

        Assert.Single(ticketera.Impresos);
        Assert.Equal(EstadoSunat.Pendiente, vm.EnvioSunat!.Estado);
        Assert.Contains("pendiente de envío", vm.EnvioSunat.Mensaje);
        var fila = await LeerAsync(boleta.Id);
        Assert.Equal(EstadoSunat.Pendiente, fila.Estado);
        Assert.Equal("No hay conexión a internet.", fila.UltimoError);
    }

    [FactConBD]
    public async Task CA2_mientras_la_SUNAT_no_responde_la_venta_siguiente_no_se_bloquea()
    {
        var (vm, sunat, ticketera) = await PantallaAsync();
        sunat.Demora = new TaskCompletionSource();

        var primera = await VenderAsync(vm);
        var segunda = await VenderAsync(vm);

        Assert.True(vm.EnvioSunat!.Enviando);
        Assert.Equal(2, ticketera.Impresos.Count);
        Assert.NotEqual(primera.Numero, segunda.Numero);

        sunat.Demora.SetResult();
        await vm.UltimoEnvioSunat;
        Assert.Equal(EstadoSunat.Aceptado, (await LeerAsync(segunda.Id)).Estado);
    }

    [FactSunatBeta]
    public async Task CA1_beta_la_boleta_sin_DNI_es_aceptada_por_la_SUNAT()
    {
        var respuesta = await EnviarBetaAsync(Boleta(cliente: null));

        Assert.True(respuesta.Aceptado, respuesta.Resumen);
        Assert.Equal("0", respuesta.Codigo);
        Assert.Contains("ha sido aceptada", respuesta.Descripcion);
        Assert.NotNull(respuesta.CdrZip);
        Assert.True(FirmaXml.Verificar(respuesta.XmlFirmado!));
    }

    [FactSunatBeta]
    public async Task CA1_beta_la_boleta_con_DNI_es_aceptada_por_la_SUNAT()
    {
        await Task.Delay(TimeSpan.FromSeconds(2)); // beta responde 401 a envíos muy seguidos
        var respuesta = await EnviarBetaAsync(Boleta(DatosPrueba.ClienteDni()));

        Assert.True(respuesta.Aceptado, respuesta.Resumen);
    }

    [FactSunatBeta]
    public async Task CA1_beta_la_factura_con_RUC_es_aceptada_por_la_SUNAT()
    {
        await Task.Delay(TimeSpan.FromSeconds(4));
        var factura = Boleta(DatosPrueba.ClienteRuc());
        factura.TipoComprobante = TipoComprobante.Factura;
        factura.SerieCodigo = "F001";

        var respuesta = await EnviarBetaAsync(factura);

        Assert.True(respuesta.Aceptado, respuesta.Resumen);
        Assert.Contains("Factura", respuesta.Descripcion);
    }

    [FactSunatBeta]
    public async Task CA2_beta_sin_llegar_a_la_SUNAT_se_avisa_como_no_disponible()
    {
        var config = new ConfiguracionSunat { UsuarioSol = "MODDATOS", ClaveSol = "moddatos", Url = "https://sunat.invalid/billService" };

        await Assert.ThrowsAsync<SunatNoDisponibleException>(() =>
            new ServicioEnvioSunat(config).EnviarAsync(DatosPrueba.Empresa(), Boleta(cliente: null)));
    }

    /// <summary>Pantalla Nueva venta contra la base de pruebas, con ticketera y SUNAT falsas.</summary>
    private async Task<(MainViewModel Vm, EnvioSunatFalso Sunat, ImpresoraFalsa Ticketera)> PantallaAsync()
    {
        var pc = _bd.Conexiones;
        var comprobantes = new ComprobanteRepositorio(pc);
        var ticketera = new ImpresoraFalsa();
        var sunat = new EnvioSunatFalso();
        var vm = new MainViewModel(new ProductoRepositorio(pc), new ClienteRepositorio(pc), comprobantes)
        {
            Impresion = new ImpresionViewModel(ticketera, new EmpresaRepositorio(pc), comprobantes),
            EnvioSunat = new EnvioSunatViewModel(sunat, new EmpresaRepositorio(pc), new EnvioSunatRepositorio(pc)),
        };
        vm.Buscador!.Espera = TimeSpan.Zero;
        vm.IniciarSesion(new Sesion(await new TerminalRepositorio(pc).RegistrarAccesoAsync("PC-HU07")));
        return (vm, sunat, ticketera);
    }

    private async Task<Comprobante> VenderAsync(MainViewModel vm)
    {
        var producto = await new ProductoRepositorio(_bd.Conexiones)
            .RegistrarAsync(DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Fierro corrugado"), 38.00m, "VAR"));
        vm.Buscador!.Elegir(producto);
        vm.Venta.CantidadTexto = "3";
        Assert.True(vm.Venta.Agregar());

        await vm.Emision!.EmitirCommand.EjecutarAsync();
        Assert.Null(vm.Emision.Error);
        await vm.UltimaImpresion;
        return vm.Emision.Ultimo!;
    }

    private sealed record Fila(string Estado, string? Codigo, bool TieneCdr, string? UltimoError);

    private async Task<Fila> LeerAsync(long id)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT c.estado_sunat, cs.codigo, cs.cdr_zip IS NOT NULL, cs.ultimo_error
            FROM comprobante c LEFT JOIN comprobante_sunat cs ON cs.comprobante_id = c.id
            WHERE c.id = @id
            """, conexion);
        cmd.Parameters.AddWithValue("id", id);
        await using var l = await cmd.ExecuteReaderAsync();
        Assert.True(await l.ReadAsync());
        return new Fila(l.GetString(0), l.IsDBNull(1) ? null : l.GetString(1), l.GetBoolean(2), l.IsDBNull(3) ? null : l.GetString(3));
    }

    private static Task<RespuestaSunat> EnviarBetaAsync(Comprobante comprobante) =>
        new ServicioEnvioSunat(new ConfiguracionSunat { UsuarioSol = "MODDATOS", ClaveSol = "moddatos" })
            .EnviarAsync(DatosPrueba.Empresa(), comprobante);

    /// <summary>Boleta con número al azar (la beta no guarda la numeración): cemento y fierro.</summary>
    private static Comprobante Boleta(Cliente? cliente)
    {
        var boleta = new Comprobante
        {
            Id = 1, TipoComprobante = TipoComprobante.Boleta, SerieCodigo = "B001",
            Numero = Random.Shared.Next(1, 99_999_999), Fecha = DateTimeOffset.Now, Cliente = cliente,
            OpGravada = 151.69m, Igv = 27.31m, Total = 179.00m,
        };
        boleta.Detalle.Add(new DetalleComprobante
        {
            Item = 1, ProductoId = 1, Descripcion = "Cemento Portland tipo I x 42.5 kg", Unidad = "BLS",
            Cantidad = 2, PrecioUnitario = 32.50m, Importe = 65.00m,
        });
        boleta.Detalle.Add(new DetalleComprobante
        {
            Item = 2, ProductoId = 2, Descripcion = "Fierro corrugado 1/2 in x 9 m", Unidad = "VAR",
            Cantidad = 3, PrecioUnitario = 38.00m, Importe = 114.00m,
        });
        return boleta;
    }
}
