using Condor.Datos;
using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Dominio.Validacion;
using Condor.Pruebas.Infraestructura;
using Npgsql;

namespace Condor.Pruebas.Datos;

/// <summary>Guías de remisión en la base central (V013, HU-10, T-90).</summary>
[Collection(BaseDatos.Nombre)]
public class GuiaRemisionRepositorioTests
{
    private readonly BaseDatosFixture _bd;

    public GuiaRemisionRepositorioTests(BaseDatosFixture bd) => _bd = bd;

    private static DateOnly Hoy => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));

    [FactConBD]
    public async Task Emite_la_guia_con_T001_el_siguiente_numero_y_su_detalle()
    {
        var repo = new GuiaRemisionRepositorio(_bd.Conexiones);
        var antes = await UltimoCorrelativoAsync();

        var guia = await repo.EmitirAsync(await GuiaAsync());
        var leida = await repo.ObtenerAsync(guia.Id);

        Assert.Equal(antes + 1, guia.Numero);
        Assert.Equal($"T001-{guia.Numero:D8}", leida!.NumeroCompleto);
        Assert.Equal(EstadoSunat.Pendiente, leida.EstadoSunat);
        Assert.Equal("V1A952", leida.VehiculoPlaca);
        Assert.Equal(ModalidadTraslado.Privado, leida.Modalidad);
        Assert.Equal(TipoDocumento.Ruc, leida.DestinatarioTipo);
        Assert.Equal(850m, leida.PesoBrutoKg);
        Assert.Equal(2, leida.Detalle.Count);
        Assert.Equal("BLS", leida.Detalle[0].Unidad);
        Assert.Equal("PC-GUIAS", leida.TerminalNombre);
    }

    [FactConBD]
    public async Task La_guia_de_una_factura_la_trae_relacionada()
    {
        var comprobante = await FacturaAsync();
        var guia = await GuiaAsync();
        guia.ComprobanteId = comprobante.Id;
        var repo = new GuiaRemisionRepositorio(_bd.Conexiones);

        var leida = await repo.ObtenerAsync((await repo.EmitirAsync(guia)).Id);

        Assert.Equal(TipoComprobante.Factura, leida!.ComprobanteTipo);
        Assert.Equal(comprobante.NumeroCompleto, leida.ComprobanteNumero);
    }

    [FactConBD]
    public async Task La_base_central_exige_los_datos_del_transporte_segun_la_modalidad()
    {
        var guia = await GuiaAsync();
        guia.Modalidad = ModalidadTraslado.Publico; // sin RUC del transportista
        var repo = new GuiaRemisionRepositorio(_bd.Conexiones);
        var antes = await UltimoCorrelativoAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => repo.EmitirAsync(guia));

        Assert.Equal("ck_guia_transporte", ex.ConstraintName);
        Assert.Equal(0, guia.Id);
        Assert.Equal(antes, await UltimoCorrelativoAsync()); // el número no se perdió
    }

    [FactConBD]
    public async Task Guarda_el_ticket_y_luego_el_CDR_y_un_estado_definitivo_no_cambia()
    {
        var repo = new GuiaRemisionRepositorio(_bd.Conexiones);
        var guia = await repo.EmitirAsync(await GuiaAsync());

        await repo.RegistrarRespuestaAsync(guia.Id, new RespuestaSunat { Estado = EstadoSunat.Pendiente, Ticket = "a1b2c3", Descripcion = "En proceso" });
        var enProceso = await repo.ObtenerAsync(guia.Id);
        Assert.Contains(enProceso!.Id, (await repo.ListarPendientesAsync(500)).Select(g => g.Id));
        await repo.RegistrarRespuestaAsync(guia.Id, new RespuestaSunat { Estado = EstadoSunat.Aceptado, Codigo = "0", Descripcion = "La Guia numero T001 ha sido aceptada", CdrZip = new byte[] { 1, 2 } });
        await repo.RegistrarRespuestaAsync(guia.Id, new RespuestaSunat { Estado = EstadoSunat.Rechazado, Codigo = "2000", Descripcion = "tarde" });
        var aceptada = await repo.ObtenerAsync(guia.Id);

        Assert.Equal("a1b2c3", enProceso.Ticket);
        Assert.Equal("En proceso", enProceso.MensajeSunat);
        Assert.Equal(EstadoSunat.Aceptado, aceptada!.EstadoSunat);
        Assert.Equal("a1b2c3", aceptada.Ticket);
        Assert.Equal("La Guia numero T001 ha sido aceptada", aceptada.MensajeSunat);
        Assert.DoesNotContain(guia.Id, (await repo.ListarPendientesAsync(500)).Select(g => g.Id));
    }

    [FactConBD]
    public async Task Busca_por_destinatario_o_numero_y_anota_las_impresiones()
    {
        var repo = new GuiaRemisionRepositorio(_bd.Conexiones);
        var nombre = DatosPrueba.NombreUnico("Constructora Ñan");
        var guia = await GuiaAsync();
        guia.DestinatarioNombre = nombre;
        guia = await repo.EmitirAsync(guia);
        await repo.RegistrarFallaAsync(guia.Id, "Sin internet");
        await repo.RegistrarImpresionAsync(guia.Id);

        var porNombre = await repo.BuscarAsync(new FiltroGuias(Texto: nombre[..18].Replace("Ñ", "N")));
        var porNumero = await repo.BuscarAsync(new FiltroGuias(Hoy, Hoy, guia.NumeroCompleto));

        var encontrada = Assert.Single(porNombre);
        Assert.Equal(guia.Id, encontrada.Id);
        Assert.Equal("Sin internet", encontrada.MensajeSunat);
        Assert.Equal(1, encontrada.Impresiones);
        Assert.Equal(guia.Id, Assert.Single(porNumero).Id);
        Assert.Empty(encontrada.Detalle);
    }

    /// <summary>Guía en transporte privado con dos productos reales, preparada por el dominio.</summary>
    private async Task<GuiaRemision> GuiaAsync()
    {
        var productos = new ProductoRepositorio(_bd.Conexiones);
        var cemento = await productos.RegistrarAsync(DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Cemento"), 32.50m, "BLS"));
        var fierro = await productos.RegistrarAsync(DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Fierro"), 38.00m, "VAR"));
        var terminal = await new TerminalRepositorio(_bd.Conexiones).RegistrarAccesoAsync("PC-GUIAS");
        var guia = new GuiaRemision
        {
            FechaTraslado = Hoy,
            Modalidad = ModalidadTraslado.Privado,
            DestinatarioTipo = TipoDocumento.Ruc,
            DestinatarioDocumento = "20131312955",
            DestinatarioNombre = "Constructora Valle de Majes S.A.C.",
            PartidaUbigeo = "040520",
            PartidaDireccion = "Majes, Caylloma - Arequipa",
            LlegadaUbigeo = "040520",
            LlegadaDireccion = "Obra Av. Principal 450, El Pedregal",
            PesoBrutoKg = 850m,
            VehiculoPlaca = "V1A-952",
            ConductorDni = "45879632",
            ConductorNombres = "Juan",
            ConductorApellidos = "Pérez Quispe",
            ConductorLicencia = "Q45879632",
            TerminalId = terminal.Id,
            Detalle =
            {
                new() { ProductoId = cemento.Id, Descripcion = cemento.Nombre, Unidad = "BLS", Cantidad = 20 },
                new() { ProductoId = fierro.Id, Descripcion = fierro.Nombre, Unidad = "VAR", Cantidad = 10 },
            },
        };
        return ValidadorGuia.Preparar(guia, Hoy);
    }

    private async Task<Comprobante> FacturaAsync()
    {
        var cliente = await new ClienteRepositorio(_bd.Conexiones).RegistrarAsync(DatosPrueba.ClienteRuc(DatosPrueba.RucNuevo()));
        var producto = await new ProductoRepositorio(_bd.Conexiones).RegistrarAsync(DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Cemento"), 32.50m, "BLS"));
        var terminal = await new TerminalRepositorio(_bd.Conexiones).RegistrarAccesoAsync("PC-GUIAS");
        var detalle = new List<DetalleComprobante>
        {
            new() { ProductoId = producto.Id, Descripcion = producto.Nombre, Unidad = "BLS", Cantidad = 20, PrecioUnitario = 32.50m },
        };
        var factura = Condor.Dominio.Ventas.EmisionComprobante.Preparar(
            new Condor.Dominio.Ventas.SolicitudEmision(Condor.Dominio.Ventas.TipoEmision.Factura, detalle, cliente));
        factura.TerminalId = terminal.Id;
        return await new ComprobanteRepositorio(_bd.Conexiones).EmitirAsync(factura);
    }

    private async Task<long> UltimoCorrelativoAsync()
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand("SELECT ultimo_correlativo FROM serie WHERE codigo = 'T001'", conexion);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
