using Condor.Datos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Ventas;
using Condor.Pruebas.Infraestructura;
using Npgsql;

namespace Condor.Pruebas.Datos;

[Collection(BaseDatos.Nombre)]
public class ComprobanteRepositorioTests
{
    private readonly BaseDatosFixture _bd;

    public ComprobanteRepositorioTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task Emitir_guarda_cabecera_y_detalle_con_el_siguiente_numero()
    {
        var serie = await CrearSerieBoletaAsync();
        var repo = new ComprobanteRepositorio(_bd.Conexiones);

        var primera = await repo.EmitirAsync(await BoletaAsync(serie));
        var segunda = await repo.EmitirAsync(await BoletaAsync(serie));

        Assert.Equal(1, primera.Numero);
        Assert.Equal(2, segunda.Numero);
        Assert.Equal($"{serie}-00000002", segunda.NumeroCompleto);
        Assert.Equal("PENDIENTE", segunda.EstadoSunat);

        var guardada = await repo.ObtenerAsync(segunda.Id);
        Assert.NotNull(guardada);
        Assert.Equal(TipoComprobante.Boleta, guardada.TipoComprobante);
        Assert.Equal(2, guardada.Detalle.Count);
        Assert.Equal("BLS", guardada.Detalle[0].Unidad);
        Assert.Equal(segunda.Total, guardada.Total);
        Assert.Equal(guardada.Detalle.Sum(l => l.Importe), guardada.Total);
        Assert.Equal(0, guardada.Impresiones);
    }

    [FactConBD]
    public async Task Si_una_linea_falla_no_queda_la_boleta_ni_se_pierde_el_numero()
    {
        var serie = await CrearSerieBoletaAsync();
        var repo = new ComprobanteRepositorio(_bd.Conexiones);
        var conError = await BoletaAsync(serie);
        conError.Detalle[1].ProductoId = -1; // producto inexistente: viola la llave foránea

        await Assert.ThrowsAsync<PostgresException>(() => repo.EmitirAsync(conError));
        var siguiente = await repo.EmitirAsync(await BoletaAsync(serie));

        Assert.Equal(0, conError.Id);
        Assert.Equal(1, siguiente.Numero);
        Assert.Equal(1L, await ContarAsync(serie));
    }

    [FactConBD]
    public async Task La_boleta_con_cliente_lo_trae_al_reimprimir()
    {
        var serie = await CrearSerieBoletaAsync();
        var cliente = await new ClienteRepositorio(_bd.Conexiones).RegistrarAsync(DatosPrueba.ClienteDni(DatosPrueba.DniNuevo()));
        var repo = new ComprobanteRepositorio(_bd.Conexiones);

        var emitida = await repo.EmitirAsync(await BoletaAsync(serie, cliente));
        var guardada = await repo.ObtenerAsync(emitida.Id);

        Assert.Equal(cliente.Id, guardada!.ClienteId);
        Assert.Equal(cliente.NumeroDocumento, guardada.Cliente!.NumeroDocumento);
        Assert.Equal("Juan Pérez Quispe", guardada.Cliente.Nombre);
    }

    [FactConBD]
    public async Task Una_serie_de_otro_tipo_se_rechaza()
    {
        var repo = new ComprobanteRepositorio(_bd.Conexiones);
        var boleta = await BoletaAsync("F001"); // F001 es de facturas

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.EmitirAsync(boleta));
        Assert.Contains("F001", ex.Message);
    }

    [FactConBD]
    public async Task La_factura_con_RUC_lleva_su_propia_numeracion_y_su_cliente()
    {
        var serieBoleta = await CrearSerieBoletaAsync();
        var serieFactura = await CrearSerieAsync(TipoComprobante.Factura, 'X');
        var cliente = await new ClienteRepositorio(_bd.Conexiones).RegistrarAsync(DatosPrueba.ClienteRuc(DatosPrueba.RucNuevo()));
        var repo = new ComprobanteRepositorio(_bd.Conexiones);

        await repo.EmitirAsync(await BoletaAsync(serieBoleta));
        await repo.EmitirAsync(await BoletaAsync(serieBoleta));
        var factura = await repo.EmitirAsync(await FacturaAsync(serieFactura, cliente));

        Assert.Equal(1, factura.Numero);
        var guardada = await repo.ObtenerAsync(factura.Id);
        Assert.Equal(TipoComprobante.Factura, guardada!.TipoComprobante);
        Assert.Equal("Factura electrónica", guardada.NombreTipo);
        Assert.Equal(TipoDocumento.Ruc, guardada.Cliente!.TipoDocumento);
        Assert.Equal(cliente.NumeroDocumento, guardada.Cliente.NumeroDocumento);
    }

    [FactConBD]
    public async Task La_base_central_rechaza_la_factura_sin_cliente_con_RUC_y_no_gasta_el_numero()
    {
        var serieFactura = await CrearSerieAsync(TipoComprobante.Factura, 'X');
        var conDni = await new ClienteRepositorio(_bd.Conexiones).RegistrarAsync(DatosPrueba.ClienteDni(DatosPrueba.DniNuevo()));
        var conRuc = await new ClienteRepositorio(_bd.Conexiones).RegistrarAsync(DatosPrueba.ClienteRuc(DatosPrueba.RucNuevo()));
        var repo = new ComprobanteRepositorio(_bd.Conexiones);

        // Una PC con una versión vieja del programa podría mandar una factura
        // con DNI o sin cliente: la base central no la deja pasar (V008).
        var conDniFactura = await FacturaAsync(serieFactura, conRuc);
        conDniFactura.Cliente = conDni;
        conDniFactura.ClienteId = conDni.Id;
        var sinCliente = await FacturaAsync(serieFactura, conRuc);
        sinCliente.Cliente = null;
        sinCliente.ClienteId = null;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.EmitirAsync(conDniFactura));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.EmitirAsync(sinCliente));
        var valida = await repo.EmitirAsync(await FacturaAsync(serieFactura, conRuc));

        Assert.Contains("RUC", ex.Message);
        Assert.Equal(0, conDniFactura.Id);
        Assert.Equal(1, valida.Numero);
        Assert.Equal(1L, await ContarAsync(serieFactura));
    }

    [FactConBD]
    public async Task Registrar_impresion_suma_una_y_aparece_en_los_recientes()
    {
        var serie = await CrearSerieBoletaAsync();
        var repo = new ComprobanteRepositorio(_bd.Conexiones);
        var emitida = await repo.EmitirAsync(await BoletaAsync(serie));

        await repo.RegistrarImpresionAsync(emitida.Id);
        await repo.RegistrarImpresionAsync(emitida.Id);

        Assert.Equal(2, (await repo.ObtenerAsync(emitida.Id))!.Impresiones);
        var reciente = (await repo.ListarRecientesAsync(200)).First(c => c.Id == emitida.Id);
        Assert.Equal(emitida.NumeroCompleto, reciente.NumeroCompleto);
        Assert.Empty(reciente.Detalle);
    }

    // HU-09: proformas (V011, T-70).

    [FactConBD]
    public async Task La_proforma_se_guarda_con_su_vigencia_de_7_dias()
    {
        var serie = await CrearSerieAsync(TipoComprobante.Proforma, 'Q');
        var repo = new ComprobanteRepositorio(_bd.Conexiones);

        var proforma = await repo.EmitirAsync(await ProformaAsync(serie));
        var guardada = await repo.ObtenerAsync(proforma.Id);

        var esperada = EmisionComprobante.VigenciaProforma(EmisionComprobante.FechaPeru(proforma.Fecha));
        Assert.Equal(1, proforma.Numero);
        Assert.Equal(esperada, proforma.ValidaHasta);
        Assert.Equal(esperada, guardada!.ValidaHasta);
        Assert.Equal(TipoComprobante.Proforma, guardada.TipoComprobante);
        Assert.Equal(2, guardada.Detalle.Count);
        Assert.False(guardada.Convertida);
    }

    [FactConBD]
    public async Task Convertir_la_proforma_enlaza_la_venta_y_la_marca_como_convertida()
    {
        var serieProforma = await CrearSerieAsync(TipoComprobante.Proforma, 'Q');
        var serieBoleta = await CrearSerieBoletaAsync();
        var repo = new ComprobanteRepositorio(_bd.Conexiones);
        var proforma = await repo.EmitirAsync(await ProformaAsync(serieProforma));

        var boleta = await repo.EmitirAsync(await ConversionAsync(serieBoleta, proforma));

        var convertida = await repo.ObtenerAsync(proforma.Id);
        var venta = await repo.ObtenerAsync(boleta.Id);
        Assert.Equal(boleta.NumeroCompleto, convertida!.ConvertidaEn);
        Assert.True(convertida.Convertida);
        Assert.Equal(proforma.Id, venta!.ProformaId);
        Assert.Null(venta.ValidaHasta);
        Assert.Equal(proforma.Total, venta.Total);
    }

    [FactConBD]
    public async Task Tres_PC_convirtiendo_la_misma_proforma_solo_una_la_emite_y_no_se_gastan_numeros()
    {
        await using var local = new TresPC();
        var serieProforma = await CrearSerieAsync(TipoComprobante.Proforma, 'Q');
        var serieBoleta = await CrearSerieBoletaAsync();
        var proforma = await new ComprobanteRepositorio(_bd.Conexiones).EmitirAsync(await ProformaAsync(serieProforma));
        var conversiones = new List<Comprobante>();
        foreach (var _ in local.Pc)
            conversiones.Add(await ConversionAsync(serieBoleta, proforma));

        using var largada = new Barrier(3);
        var resultados = await Task.WhenAll(local.Pc.Select((pc, i) => Task.Run(async () =>
        {
            largada.SignalAndWait();
            try
            {
                await new ComprobanteRepositorio(pc).EmitirAsync(conversiones[i]);
                return (Exception?)null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        })));

        Assert.Single(resultados, r => r is null);
        var rechazos = resultados.Where(r => r is not null).ToList();
        Assert.Equal(2, rechazos.Count);
        Assert.All(rechazos, r => Assert.IsType<ProformaYaConvertidaException>(r));
        Assert.Contains(proforma.NumeroCompleto, rechazos[0]!.Message);
        // El ROLLBACK devuelve el número: la boleta emitida es la 1 y no hay saltos.
        Assert.Equal(1L, await ContarAsync(serieBoleta));
        Assert.Equal(1, conversiones.Single(c => c.Id != 0).Numero);
    }

    [FactConBD]
    public async Task La_base_central_no_convierte_una_proforma_vencida()
    {
        var serieProforma = await CrearSerieAsync(TipoComprobante.Proforma, 'Q');
        var serieBoleta = await CrearSerieBoletaAsync();
        var repo = new ComprobanteRepositorio(_bd.Conexiones);
        var proforma = await repo.EmitirAsync(await ProformaAsync(serieProforma));
        await EjecutarAsync("UPDATE comprobante SET valida_hasta = current_date - 8 WHERE id = @id", proforma.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await repo.EmitirAsync(await ConversionAsync(serieBoleta, proforma)));

        Assert.Contains("vencida", ex.Message);
        Assert.Contains(proforma.NumeroCompleto, ex.Message);
        Assert.Equal(0L, await ContarAsync(serieBoleta));
    }

    [FactConBD]
    public async Task Solo_una_proforma_se_convierte_aunque_una_PC_vieja_lo_intente()
    {
        var serieBoleta = await CrearSerieBoletaAsync();
        var repo = new ComprobanteRepositorio(_bd.Conexiones);
        var boleta = await repo.EmitirAsync(await BoletaAsync(serieBoleta));
        var otra = await BoletaAsync(serieBoleta);
        otra.ProformaId = boleta.Id; // apunta a una boleta, no a una proforma

        var ex = await Assert.ThrowsAsync<PostgresException>(() => repo.EmitirAsync(otra));

        Assert.Equal("ck_proforma", ex.ConstraintName);
    }

    [FactConBD]
    public async Task La_proforma_sin_vigencia_no_se_guarda()
    {
        var serie = await CrearSerieAsync(TipoComprobante.Proforma, 'Q');
        var proforma = await new ComprobanteRepositorio(_bd.Conexiones).EmitirAsync(await ProformaAsync(serie));

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            EjecutarAsync("UPDATE comprobante SET valida_hasta = NULL WHERE id = @id", proforma.Id));

        Assert.Equal("ck_proforma", ex.ConstraintName);
    }

    /// <summary>Proforma preparada por el dominio con los productos de la boleta de prueba.</summary>
    private async Task<Comprobante> ProformaAsync(string serie)
    {
        var boleta = await BoletaAsync(serie);
        var proforma = EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.Proforma, boleta.Detalle));
        proforma.SerieCodigo = serie;
        proforma.TerminalId = boleta.TerminalId;
        return proforma;
    }

    /// <summary>Boleta que sale de la proforma, con su mismo detalle.</summary>
    private async Task<Comprobante> ConversionAsync(string serieBoleta, Comprobante proforma)
    {
        var terminal = await new TerminalRepositorio(_bd.Conexiones).RegistrarAccesoAsync("PC-PRUEBAS");
        var detalle = proforma.Detalle.Select(l => new DetalleComprobante
        {
            ProductoId = l.ProductoId, Descripcion = l.Descripcion, Unidad = l.Unidad, Cantidad = l.Cantidad, PrecioUnitario = l.PrecioUnitario,
        }).ToList();
        var boleta = EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.Boleta, detalle, Proforma: proforma));
        boleta.SerieCodigo = serieBoleta;
        boleta.TerminalId = terminal.Id;
        return boleta;
    }

    private async Task EjecutarAsync(string sql, long id)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(sql, conexion);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Boleta preparada por el dominio con dos productos reales del catálogo de pruebas.</summary>
    private async Task<Comprobante> BoletaAsync(string serie, Cliente? cliente = null)
    {
        var productos = new ProductoRepositorio(_bd.Conexiones);
        var cemento = await productos.RegistrarAsync(DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Cemento"), 32.50m, "BLS"));
        var clavos = await productos.RegistrarAsync(DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Clavo 3 in"), 7.50m, "KG"));
        var terminal = await new TerminalRepositorio(_bd.Conexiones).RegistrarAccesoAsync("PC-PRUEBAS");

        var detalle = new List<DetalleComprobante>
        {
            new() { ProductoId = cemento.Id, Descripcion = cemento.Nombre, Unidad = "BLS", Cantidad = 2, PrecioUnitario = 32.50m },
            new() { ProductoId = clavos.Id, Descripcion = clavos.Nombre, Unidad = "KG", Cantidad = 1.5m, PrecioUnitario = 7.50m },
        };
        var tipo = cliente is null ? TipoEmision.Boleta : TipoEmision.BoletaConDni;
        var boleta = EmisionComprobante.Preparar(new SolicitudEmision(tipo, detalle, cliente));
        boleta.SerieCodigo = serie;
        boleta.TerminalId = terminal.Id;
        return boleta;
    }

    /// <summary>Factura preparada por el dominio para el cliente con RUC (HU-06).</summary>
    private async Task<Comprobante> FacturaAsync(string serie, Cliente cliente)
    {
        var boleta = await BoletaAsync(serie);
        var factura = EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.Factura, boleta.Detalle, cliente));
        factura.SerieCodigo = serie;
        factura.TerminalId = boleta.TerminalId;
        return factura;
    }

    /// <summary>Serie de boletas de prueba (W + 3 dígitos) para no tocar la B001.</summary>
    private Task<string> CrearSerieBoletaAsync() => CrearSerieAsync(TipoComprobante.Boleta, 'W');

    /// <summary>Serie de prueba de un tipo (letra + 3 dígitos) para no tocar la B001 ni la F001.</summary>
    private async Task<string> CrearSerieAsync(string tipo, char letra)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO serie (tipo_comprobante, codigo)
            SELECT @tipo, @letra || lpad(n::text, 3, '0')
            FROM generate_series(1, 999) AS n
            WHERE NOT EXISTS (SELECT 1 FROM serie WHERE codigo = @letra || lpad(n::text, 3, '0'))
            LIMIT 1
            RETURNING codigo
            """, conexion);
        cmd.Parameters.AddWithValue("tipo", tipo);
        cmd.Parameters.AddWithValue("letra", letra.ToString());
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<long> ContarAsync(string serie)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM comprobante c JOIN serie s ON s.id = c.serie_id WHERE s.codigo = @s", conexion);
        cmd.Parameters.AddWithValue("s", serie);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
