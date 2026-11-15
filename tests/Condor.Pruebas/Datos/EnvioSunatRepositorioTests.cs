using Condor.Datos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Dominio.Ventas;
using Condor.Pruebas.Infraestructura;
using Npgsql;

namespace Condor.Pruebas.Datos;

/// <summary>Migraciones V007 y V009: respuestas de la SUNAT (T-45) y reserva de los pendientes (T-55).</summary>
[Collection(BaseDatos.Nombre)]
public class EnvioSunatRepositorioTests
{
    private readonly BaseDatosFixture _bd;

    public EnvioSunatRepositorioTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task Aceptado_guarda_el_estado_el_XML_el_hash_y_el_CDR()
    {
        var boleta = await EmitirAsync();
        var repo = new EnvioSunatRepositorio(_bd.Conexiones);

        await repo.RegistrarRespuestaAsync(boleta.Id, EnvioSunatFalso.Aceptada("20600000005-03-B001-1"));

        var fila = await LeerAsync(boleta.Id);
        Assert.Equal(EstadoSunat.Aceptado, fila.Estado);
        Assert.Equal("0", fila.Codigo);
        Assert.Equal("<Invoice/>", fila.Xml);
        Assert.Equal("hS0bHq3cJ0aZyb0dA1Y4mWm3x1A=", fila.Hash);
        Assert.Equal(new byte[] { 0x50, 0x4B, 0x05, 0x06 }, fila.Cdr);
        Assert.Equal(1, fila.Intentos);
        Assert.True(fila.Respondido);
        Assert.Null(fila.UltimoError);
    }

    [FactConBD]
    public async Task Rechazado_guarda_el_codigo_y_la_descripcion_y_sale_en_el_historial()
    {
        var boleta = await EmitirAsync();
        var repo = new EnvioSunatRepositorio(_bd.Conexiones);

        await repo.RegistrarRespuestaAsync(boleta.Id, EnvioSunatFalso.Rechazada("2800", "El tipo de documento del receptor no es valido"));

        var fila = await LeerAsync(boleta.Id);
        Assert.Equal(EstadoSunat.Rechazado, fila.Estado);
        Assert.Equal("2800", fila.Codigo);
        var historial = await new ComprobanteRepositorio(_bd.Conexiones).ObtenerAsync(boleta.Id);
        Assert.Equal(EstadoSunat.Rechazado, historial!.EstadoSunat);
        Assert.Equal("El tipo de documento del receptor no es valido", historial.MensajeSunat);
    }

    [FactConBD]
    public async Task Sin_internet_suma_intentos_deja_el_motivo_y_sigue_pendiente()
    {
        var boleta = await EmitirAsync();
        var repo = new EnvioSunatRepositorio(_bd.Conexiones);

        await repo.RegistrarFallaAsync(boleta.Id, "No hay conexión a internet.");
        await repo.RegistrarFallaAsync(boleta.Id, "La SUNAT no respondió en 30 segundos.");

        var fila = await LeerAsync(boleta.Id);
        Assert.Equal(EstadoSunat.Pendiente, fila.Estado);
        Assert.Equal(2, fila.Intentos);
        Assert.Equal("La SUNAT no respondió en 30 segundos.", fila.UltimoError);
        Assert.False(fila.Respondido);
    }

    [FactConBD]
    public async Task Listar_pendientes_da_solo_pendientes_de_la_mas_antigua_a_la_mas_reciente()
    {
        var repo = new EnvioSunatRepositorio(_bd.Conexiones);
        await repo.RegistrarFallaAsync((await EmitirAsync()).Id, "Sin internet");

        var pendientes = await repo.ListarPendientesAsync(20);

        Assert.NotEmpty(pendientes);
        Assert.Equal(pendientes.Order(), pendientes);
        foreach (var id in pendientes)
            Assert.Equal(EstadoSunat.Pendiente, (await new ComprobanteRepositorio(_bd.Conexiones).ObtenerAsync(id))!.EstadoSunat);
    }

    [FactConBD]
    public async Task Tras_un_fallo_el_reenvio_aceptado_borra_el_error_y_sale_de_pendientes()
    {
        var boleta = await EmitirAsync();
        var repo = new EnvioSunatRepositorio(_bd.Conexiones);
        await repo.RegistrarFallaAsync(boleta.Id, "No hay conexión a internet.");

        await repo.RegistrarRespuestaAsync(boleta.Id, EnvioSunatFalso.Aceptada());

        var fila = await LeerAsync(boleta.Id);
        Assert.Equal(EstadoSunat.Aceptado, fila.Estado);
        Assert.Equal(2, fila.Intentos);
        Assert.Null(fila.UltimoError);
        Assert.DoesNotContain(boleta.Id, await repo.ListarPendientesAsync(1000));
    }

    [FactConBD]
    public async Task Un_estado_definitivo_no_se_pisa_con_una_respuesta_posterior()
    {
        var boleta = await EmitirAsync();
        var repo = new EnvioSunatRepositorio(_bd.Conexiones);
        await repo.RegistrarRespuestaAsync(boleta.Id, EnvioSunatFalso.Aceptada());

        // Otra PC lo reenvió y la SUNAT contestó "1033: registrado previamente".
        await repo.RegistrarRespuestaAsync(boleta.Id, new RespuestaSunat
        {
            Estado = EstadoSunat.Pendiente, Codigo = "1033", Descripcion = "El comprobante fue registrado previamente con otros datos",
        });
        await repo.RegistrarFallaAsync(boleta.Id, "Sin internet");

        var fila = await LeerAsync(boleta.Id);
        Assert.Equal(EstadoSunat.Aceptado, fila.Estado);
        Assert.Equal("0", fila.Codigo);
        Assert.Null(fila.UltimoError);
        Assert.Equal(3, fila.Intentos);
    }

    [FactConBD]
    public async Task La_empresa_tiene_el_domicilio_fiscal_de_Majes_para_el_XML()
    {
        var empresa = await new EmpresaRepositorio(_bd.Conexiones).ObtenerAsync();

        Assert.Equal("040520", empresa.Ubigeo);
        Assert.Equal("AREQUIPA", empresa.Departamento);
        Assert.Equal("CAYLLOMA", empresa.Provincia);
        Assert.Equal("MAJES", empresa.Distrito);
        Assert.Equal("0000", empresa.CodigoEstablecimiento);
    }

    [FactConBD]
    public async Task Reservar_aparta_los_pendientes_para_una_sola_PC()
    {
        await PendientesDePrueba.ApartarLosDeOtrasPruebasAsync(_bd.Conexiones);
        var emitidas = new[] { await EmitirAsync(), await EmitirAsync(), await EmitirAsync() };
        var repo = new EnvioSunatRepositorio(_bd.Conexiones);

        var pc1 = await repo.ReservarPendientesAsync("PC-1", TimeSpan.Zero);
        var pc2 = await repo.ReservarPendientesAsync("PC-2", TimeSpan.Zero);

        Assert.Equal(emitidas.Select(c => c.Id), pc1);
        Assert.Empty(pc2);
        Assert.Equal("PC-1", await PendientesDePrueba.ReservadoPorAsync(_bd.Conexiones, emitidas[0].Id));
    }

    [FactConBD]
    public async Task Tres_PC_a_la_vez_nunca_apartan_el_mismo_comprobante()
    {
        await PendientesDePrueba.ApartarLosDeOtrasPruebasAsync(_bd.Conexiones);
        var emitidas = new List<long>();
        for (var i = 0; i < 10; i++)
            emitidas.Add((await EmitirAsync()).Id);
        await using var pcs = new TresPC();

        // Cada vuelta, las tres PC apartan a la vez; una PC puede llevarse menos
        // de 4 si las otras los tenían bloqueados, y los que faltan salen en la
        // vuelta siguiente. Lo que nunca puede pasar es que dos PC aparten el mismo.
        var todas = new List<long>();
        for (var vuelta = 0; vuelta < 4 && todas.Count < emitidas.Count; vuelta++)
        {
            var reservas = await Task.WhenAll(pcs.Pc.Select((pc, i) =>
                new EnvioSunatRepositorio(pc).ReservarPendientesAsync($"PC-{i + 1}", TimeSpan.Zero, limite: 4)));
            todas.AddRange(reservas.SelectMany(r => r));
        }

        Assert.Equal(todas.Count, todas.Distinct().Count());
        Assert.Equal(emitidas.Order(), todas.Order());
    }

    [FactConBD]
    public async Task Registrar_la_falla_libera_la_reserva_y_la_respuesta_lo_saca_de_los_pendientes()
    {
        await PendientesDePrueba.ApartarLosDeOtrasPruebasAsync(_bd.Conexiones);
        var boleta = await EmitirAsync();
        var repo = new EnvioSunatRepositorio(_bd.Conexiones);

        Assert.Equal(new[] { boleta.Id }, await repo.ReservarPendientesAsync("PC-1", TimeSpan.Zero));
        await repo.RegistrarFallaAsync(boleta.Id, "No hay conexión a internet.");
        Assert.Null(await PendientesDePrueba.ReservadoPorAsync(_bd.Conexiones, boleta.Id));

        Assert.Equal(new[] { boleta.Id }, await repo.ReservarPendientesAsync("PC-2", TimeSpan.Zero));
        await repo.RegistrarRespuestaAsync(boleta.Id, EnvioSunatFalso.Aceptada());
        Assert.Empty(await repo.ReservarPendientesAsync("PC-3", TimeSpan.Zero));
        Assert.Equal(EstadoSunat.Aceptado, (await LeerAsync(boleta.Id)).Estado);
    }

    [FactConBD]
    public async Task Liberar_devuelve_solo_los_apartados_por_esa_PC_sin_sumar_intentos()
    {
        await PendientesDePrueba.ApartarLosDeOtrasPruebasAsync(_bd.Conexiones);
        var propia = await EmitirAsync();
        var ajena = await EmitirAsync();
        var repo = new EnvioSunatRepositorio(_bd.Conexiones);
        await repo.ReservarPendientesAsync("PC-1", TimeSpan.Zero, limite: 1);
        await repo.ReservarPendientesAsync("PC-2", TimeSpan.Zero, limite: 1);

        await repo.LiberarAsync("PC-1", new[] { propia.Id, ajena.Id });

        Assert.Null(await PendientesDePrueba.ReservadoPorAsync(_bd.Conexiones, propia.Id));
        Assert.Equal("PC-2", await PendientesDePrueba.ReservadoPorAsync(_bd.Conexiones, ajena.Id));
        Assert.Equal(0, (await LeerAsync(propia.Id)).Intentos);
        Assert.Equal(new[] { propia.Id }, await repo.ReservarPendientesAsync("PC-3", TimeSpan.Zero));
    }

    [FactConBD]
    public async Task La_espera_entre_intentos_crece_con_cada_intento()
    {
        await PendientesDePrueba.ApartarLosDeOtrasPruebasAsync(_bd.Conexiones);
        var boleta = await EmitirAsync();
        var repo = new EnvioSunatRepositorio(_bd.Conexiones);
        var espera = TimeSpan.FromMinutes(5);

        // Recién emitida: aún se está enviando desde la pantalla de venta.
        Assert.Empty(await repo.ReservarPendientesAsync("PC-1", espera));

        // Dos intentos fallidos: la próxima vez toca después de 2 x 5 minutos.
        await repo.RegistrarFallaAsync(boleta.Id, "Sin internet");
        await repo.RegistrarFallaAsync(boleta.Id, "Sin internet");
        await PendientesDePrueba.AtrasarAsync(_bd.Conexiones, boleta.Id, TimeSpan.FromMinutes(6));
        Assert.Empty(await repo.ReservarPendientesAsync("PC-1", espera));

        await PendientesDePrueba.AtrasarAsync(_bd.Conexiones, boleta.Id, TimeSpan.FromMinutes(5));
        Assert.Equal(new[] { boleta.Id }, await repo.ReservarPendientesAsync("PC-1", espera));
    }

    [FactConBD]
    public async Task Si_la_PC_que_lo_aparto_se_apaga_la_reserva_vence_y_otra_lo_retoma()
    {
        await PendientesDePrueba.ApartarLosDeOtrasPruebasAsync(_bd.Conexiones);
        var boleta = await EmitirAsync();
        var repo = new EnvioSunatRepositorio(_bd.Conexiones);
        await repo.ReservarPendientesAsync("PC-1", TimeSpan.Zero);

        await using (var conexion = await _bd.Conexiones.AbrirAsync())
        await using (var cmd = new NpgsqlCommand(
            "UPDATE comprobante_sunat SET reservado_hasta = now() - interval '1 second' WHERE comprobante_id = @id", conexion))
        {
            cmd.Parameters.AddWithValue("id", boleta.Id);
            await cmd.ExecuteNonQueryAsync();
        }

        Assert.Equal(new[] { boleta.Id }, await repo.ReservarPendientesAsync("PC-2", TimeSpan.Zero));
        Assert.Equal("PC-2", await PendientesDePrueba.ReservadoPorAsync(_bd.Conexiones, boleta.Id));
    }

    [FactConBD]
    public async Task El_resumen_cuenta_los_pendientes_y_el_mas_antiguo()
    {
        var repo = new EnvioSunatRepositorio(_bd.Conexiones);
        var antes = await repo.ResumenPendientesAsync();

        var primera = await EmitirAsync();
        var segunda = await EmitirAsync();
        await repo.RegistrarRespuestaAsync(segunda.Id, EnvioSunatFalso.Aceptada());
        var despues = await repo.ResumenPendientesAsync();

        Assert.Equal(antes.Cantidad + 1, despues.Cantidad);
        Assert.NotNull(despues.MasAntiguo);
        Assert.True(despues.MasAntiguo <= primera.Fecha);
    }

    private sealed record Fila(string Estado, string? Codigo, string? Xml, string? Hash, byte[]? Cdr, int Intentos, bool Respondido, string? UltimoError);

    private async Task<Fila> LeerAsync(long id)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT c.estado_sunat, cs.codigo, cs.xml_firmado, cs.hash, cs.cdr_zip, cs.intentos,
                   cs.respondido_en IS NOT NULL, cs.ultimo_error
            FROM comprobante c JOIN comprobante_sunat cs ON cs.comprobante_id = c.id
            WHERE c.id = @id
            """, conexion);
        cmd.Parameters.AddWithValue("id", id);
        await using var l = await cmd.ExecuteReaderAsync();
        Assert.True(await l.ReadAsync());
        string? Texto(int i) => l.IsDBNull(i) ? null : l.GetString(i);
        return new Fila(l.GetString(0), Texto(1), Texto(2), Texto(3), l.IsDBNull(4) ? null : (byte[])l[4],
            l.GetInt32(5), l.GetBoolean(6), Texto(7));
    }

    /// <summary>Boleta B001 de la base de pruebas, con un producto nuevo.</summary>
    private async Task<Comprobante> EmitirAsync()
    {
        var producto = await new ProductoRepositorio(_bd.Conexiones)
            .RegistrarAsync(DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Cemento"), 32.50m, "BLS"));
        var terminal = await new TerminalRepositorio(_bd.Conexiones).RegistrarAccesoAsync("PC-SUNAT");
        var boleta = EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.Boleta, new List<DetalleComprobante>
        {
            new() { ProductoId = producto.Id, Descripcion = producto.Nombre, Unidad = "BLS", Cantidad = 2, PrecioUnitario = 32.50m },
        }));
        boleta.TerminalId = terminal.Id;
        return await new ComprobanteRepositorio(_bd.Conexiones).EmitirAsync(boleta);
    }
}
