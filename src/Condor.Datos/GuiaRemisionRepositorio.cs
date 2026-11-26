using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Npgsql;

namespace Condor.Datos;

/// <summary>
/// Guías de remisión en la base de datos central (HU-10, T-90).
/// </summary>
/// <remarks>
/// Como la emisión de comprobantes (T-28): el número de la serie T001, la
/// cabecera y el detalle se guardan en una sola transacción; si algo falla no
/// queda la guía a medias ni se pierde el número.
/// </remarks>
public sealed class GuiaRemisionRepositorio : IGuiaRemisionRepositorio
{
    private const string Cabecera = """
        SELECT g.id, s.id, s.codigo, g.numero, g.fecha, g.fecha_traslado, g.motivo, g.descripcion_motivo, g.modalidad,
               g.destinatario_tipo, g.destinatario_documento, g.destinatario_nombre,
               g.partida_ubigeo, g.partida_direccion, g.llegada_ubigeo, g.llegada_direccion, g.peso_bruto_kg,
               g.transportista_ruc, g.transportista_razon_social, g.vehiculo_placa,
               g.conductor_dni, g.conductor_nombres, g.conductor_apellidos, g.conductor_licencia,
               g.comprobante_id, sc.tipo_comprobante, sc.codigo, c.numero,
               g.observacion, g.terminal_id, t.nombre, g.impresiones,
               g.estado_sunat, coalesce(g.ultimo_error, g.descripcion_sunat), g.ticket
        FROM guia_remision g
        JOIN serie s ON s.id = g.serie_id
        JOIN terminal t ON t.id = g.terminal_id
        LEFT JOIN comprobante c ON c.id = g.comprobante_id
        LEFT JOIN serie sc ON sc.id = c.serie_id
        """;

    private readonly FabricaConexiones _conexiones;

    public GuiaRemisionRepositorio(FabricaConexiones conexiones) => _conexiones = conexiones;

    public async Task<GuiaRemision> EmitirAsync(GuiaRemision guia, CancellationToken ct = default)
    {
        if (guia.Detalle.Count == 0)
            throw new InvalidOperationException("La guía no tiene productos.");
        if (guia.TerminalId <= 0)
            throw new InvalidOperationException("Esta PC aún no está registrada en la base de datos central; espere a que se conecte.");

        await using var conexion = await _conexiones.AbrirAsync(ct);
        try
        {
            await using var tx = await conexion.BeginTransactionAsync(ct);
            var numero = await SerieRepositorio.SiguienteCorrelativoAsync(conexion, tx, guia.SerieCodigo, ct);

            await using (var cmd = new NpgsqlCommand(
                """
                INSERT INTO guia_remision (serie_id, numero, fecha_traslado, motivo, descripcion_motivo, modalidad,
                    destinatario_tipo, destinatario_documento, destinatario_nombre,
                    partida_ubigeo, partida_direccion, llegada_ubigeo, llegada_direccion, peso_bruto_kg,
                    transportista_ruc, transportista_razon_social, vehiculo_placa,
                    conductor_dni, conductor_nombres, conductor_apellidos, conductor_licencia,
                    comprobante_id, observacion, terminal_id)
                SELECT s.id, @numero, @traslado, @motivo, @descripcion, @modalidad,
                    @dtipo, @ddoc, @dnombre, @pubigeo, @pdir, @lubigeo, @ldir, @peso,
                    @truc, @trazon, @placa, @cdni, @cnombres, @capellidos, @clicencia,
                    @comprobante, @observacion, @terminal
                FROM serie s
                WHERE s.codigo = @serie AND s.tipo_comprobante = '09'
                RETURNING id, serie_id, fecha
                """, conexion, tx))
            {
                cmd.Parameters.AddWithValue("numero", numero);
                cmd.Parameters.AddWithValue("traslado", guia.FechaTraslado);
                cmd.Parameters.AddWithValue("motivo", guia.Motivo);
                cmd.Parameters.AddWithValue("descripcion", Nulo(guia.DescripcionMotivo));
                cmd.Parameters.AddWithValue("modalidad", guia.Modalidad);
                cmd.Parameters.AddWithValue("dtipo", ClienteRepositorio.CodigoTipo(guia.DestinatarioTipo));
                cmd.Parameters.AddWithValue("ddoc", guia.DestinatarioDocumento);
                cmd.Parameters.AddWithValue("dnombre", guia.DestinatarioNombre);
                cmd.Parameters.AddWithValue("pubigeo", guia.PartidaUbigeo);
                cmd.Parameters.AddWithValue("pdir", guia.PartidaDireccion);
                cmd.Parameters.AddWithValue("lubigeo", guia.LlegadaUbigeo);
                cmd.Parameters.AddWithValue("ldir", guia.LlegadaDireccion);
                cmd.Parameters.AddWithValue("peso", guia.PesoBrutoKg);
                cmd.Parameters.AddWithValue("truc", Nulo(guia.TransportistaRuc));
                cmd.Parameters.AddWithValue("trazon", Nulo(guia.TransportistaRazonSocial));
                cmd.Parameters.AddWithValue("placa", Nulo(guia.VehiculoPlaca));
                cmd.Parameters.AddWithValue("cdni", Nulo(guia.ConductorDni));
                cmd.Parameters.AddWithValue("cnombres", Nulo(guia.ConductorNombres));
                cmd.Parameters.AddWithValue("capellidos", Nulo(guia.ConductorApellidos));
                cmd.Parameters.AddWithValue("clicencia", Nulo(guia.ConductorLicencia));
                cmd.Parameters.AddWithValue("comprobante", (object?)guia.ComprobanteId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("observacion", Nulo(guia.Observacion));
                cmd.Parameters.AddWithValue("terminal", guia.TerminalId);
                cmd.Parameters.AddWithValue("serie", guia.SerieCodigo);

                await using var lector = await cmd.ExecuteReaderAsync(ct);
                if (!await lector.ReadAsync(ct))
                    throw new InvalidOperationException($"La serie {guia.SerieCodigo} no es de guías de remisión.");
                guia.Id = lector.GetInt64(0);
                guia.SerieId = lector.GetInt32(1);
                guia.Fecha = lector.GetFieldValue<DateTimeOffset>(2);
            }

            await using (var lote = new NpgsqlBatch(conexion, tx))
            {
                foreach (var linea in guia.Detalle)
                {
                    var cmd = new NpgsqlBatchCommand(
                        """
                        INSERT INTO guia_detalle (guia_id, item, producto_id, descripcion, cantidad)
                        VALUES (@guia, @item, @producto, @descripcion, @cantidad)
                        """);
                    cmd.Parameters.AddWithValue("guia", guia.Id);
                    cmd.Parameters.AddWithValue("item", linea.Item);
                    cmd.Parameters.AddWithValue("producto", linea.ProductoId);
                    cmd.Parameters.AddWithValue("descripcion", linea.Descripcion);
                    cmd.Parameters.AddWithValue("cantidad", linea.Cantidad);
                    lote.BatchCommands.Add(cmd);
                }
                await lote.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            guia.Numero = numero;
            guia.EstadoSunat = EstadoSunat.Pendiente;
            return guia;
        }
        catch (Exception ex) when (ex is not SinConexionException && FabricaConexiones.EsFallaDeConexion(ex))
        {
            guia.Id = 0;
            throw new SinConexionException(ex);
        }
        catch
        {
            guia.Id = 0;
            throw;
        }
    }

    public async Task<GuiaRemision?> ObtenerAsync(long id, CancellationToken ct = default)
    {
        await using var conexion = await _conexiones.AbrirAsync(ct);

        GuiaRemision? guia;
        await using (var cmd = new NpgsqlCommand($"{Cabecera} WHERE g.id = @id", conexion))
        {
            cmd.Parameters.AddWithValue("id", id);
            await using var lector = await cmd.ExecuteReaderAsync(ct);
            guia = await lector.ReadAsync(ct) ? Leer(lector) : null;
        }
        if (guia is null)
            return null;

        await using (var cmd = new NpgsqlCommand(
            """
            SELECT d.item, d.producto_id, d.descripcion, p.unidad, d.cantidad
            FROM guia_detalle d
            JOIN producto p ON p.id = d.producto_id
            WHERE d.guia_id = @id
            ORDER BY d.item
            """, conexion))
        {
            cmd.Parameters.AddWithValue("id", id);
            await using var lector = await cmd.ExecuteReaderAsync(ct);
            while (await lector.ReadAsync(ct))
            {
                guia.Detalle.Add(new DetalleGuia
                {
                    Item = lector.GetInt32(0),
                    ProductoId = lector.GetInt64(1),
                    Descripcion = lector.GetString(2),
                    Unidad = lector.GetString(3),
                    Cantidad = lector.GetDecimal(4),
                });
            }
        }
        return guia;
    }

    public async Task<IReadOnlyList<GuiaRemision>> BuscarAsync(FiltroGuias filtro, CancellationToken ct = default)
    {
        var texto = filtro.Texto?.Trim() ?? string.Empty;
        var esNumero = Serie.IntentarLeerNumero(texto, out var serie, out var numero);

        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand(
            $"""
            {Cabecera}
            WHERE (@desde::date IS NULL OR g.fecha >= (@desde::date)::timestamp AT TIME ZONE 'America/Lima')
              AND (@hasta::date IS NULL OR g.fecha < (@hasta::date + 1)::timestamp AT TIME ZONE 'America/Lima')
              AND (@texto = ''
                   OR (@numero > 0 AND g.numero = @numero AND (@serie = '' OR s.codigo = @serie))
                   OR g.destinatario_documento LIKE @texto || '%'
                   OR texto_busqueda(g.destinatario_nombre) LIKE '%' || texto_busqueda(@texto) || '%')
            ORDER BY g.id DESC
            LIMIT @limite
            """, conexion);
        cmd.Parameters.Add(new NpgsqlParameter("desde", NpgsqlTypes.NpgsqlDbType.Date) { Value = (object?)filtro.Desde ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("hasta", NpgsqlTypes.NpgsqlDbType.Date) { Value = (object?)filtro.Hasta ?? DBNull.Value });
        cmd.Parameters.AddWithValue("texto", ProductoRepositorio.EscaparLike(texto));
        cmd.Parameters.AddWithValue("numero", esNumero ? numero : 0L);
        cmd.Parameters.AddWithValue("serie", serie ?? string.Empty);
        cmd.Parameters.AddWithValue("limite", Math.Clamp(filtro.Limite, 1, 1000));
        return await LeerVariasAsync(cmd, ct);
    }

    public async Task RegistrarRespuestaAsync(long id, RespuestaSunat respuesta, CancellationToken ct = default)
    {
        var definitiva = EstadoSunat.EsDefinitivo(respuesta.Estado);
        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE guia_remision SET
                estado_sunat      = CASE WHEN estado_sunat = 'PENDIENTE' THEN @estado ELSE estado_sunat END,
                ticket            = coalesce(@ticket, ticket),
                codigo_sunat      = CASE WHEN estado_sunat = 'PENDIENTE' THEN @codigo ELSE codigo_sunat END,
                descripcion_sunat = CASE WHEN estado_sunat = 'PENDIENTE' THEN @descripcion ELSE descripcion_sunat END,
                xml_firmado       = coalesce(@xml, xml_firmado),
                cdr_zip           = CASE WHEN estado_sunat = 'PENDIENTE' THEN coalesce(@cdr, cdr_zip) ELSE cdr_zip END,
                intentos          = intentos + 1,
                ultimo_intento    = now(),
                ultimo_error      = CASE WHEN @definitiva OR estado_sunat <> 'PENDIENTE' THEN NULL ELSE @descripcion END
            WHERE id = @id
            """, conexion);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("estado", respuesta.Estado);
        cmd.Parameters.AddWithValue("ticket", Nulo(respuesta.Ticket));
        cmd.Parameters.AddWithValue("codigo", Nulo(Recortar(respuesta.Codigo, 10)));
        cmd.Parameters.AddWithValue("descripcion", Nulo(Recortar(respuesta.Descripcion, 500)));
        cmd.Parameters.AddWithValue("xml", Nulo(respuesta.XmlFirmado));
        cmd.Parameters.AddWithValue("cdr", (object?)respuesta.CdrZip ?? DBNull.Value);
        cmd.Parameters.AddWithValue("definitiva", definitiva);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RegistrarFallaAsync(long id, string motivo, CancellationToken ct = default)
    {
        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE guia_remision SET intentos = intentos + 1, ultimo_intento = now(),
                ultimo_error = CASE WHEN estado_sunat = 'PENDIENTE' THEN @motivo ELSE ultimo_error END
            WHERE id = @id
            """, conexion);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("motivo", Recortar(motivo, 500) ?? string.Empty);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<GuiaRemision>> ListarPendientesAsync(int limite = 50, CancellationToken ct = default)
    {
        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand(
            $"{Cabecera} WHERE g.estado_sunat = 'PENDIENTE' ORDER BY g.id LIMIT @limite", conexion);
        cmd.Parameters.AddWithValue("limite", Math.Clamp(limite, 1, 500));
        return await LeerVariasAsync(cmd, ct);
    }

    public async Task RegistrarImpresionAsync(long id, CancellationToken ct = default)
    {
        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand("UPDATE guia_remision SET impresiones = impresiones + 1 WHERE id = @id", conexion);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IReadOnlyList<GuiaRemision>> LeerVariasAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var guias = new List<GuiaRemision>();
        await using var lector = await cmd.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
            guias.Add(Leer(lector));
        return guias;
    }

    private static GuiaRemision Leer(NpgsqlDataReader lector) => new()
    {
        Id = lector.GetInt64(0),
        SerieId = lector.GetInt32(1),
        SerieCodigo = lector.GetString(2).Trim(),
        Numero = lector.GetInt64(3),
        Fecha = lector.GetFieldValue<DateTimeOffset>(4),
        FechaTraslado = lector.GetFieldValue<DateOnly>(5),
        Motivo = lector.GetString(6),
        DescripcionMotivo = Texto(lector, 7),
        Modalidad = lector.GetString(8),
        DestinatarioTipo = (TipoDocumento)int.Parse(lector.GetString(9)),
        DestinatarioDocumento = lector.GetString(10),
        DestinatarioNombre = lector.GetString(11),
        PartidaUbigeo = lector.GetString(12),
        PartidaDireccion = lector.GetString(13),
        LlegadaUbigeo = lector.GetString(14),
        LlegadaDireccion = lector.GetString(15),
        PesoBrutoKg = lector.GetDecimal(16),
        TransportistaRuc = Texto(lector, 17),
        TransportistaRazonSocial = Texto(lector, 18),
        VehiculoPlaca = Texto(lector, 19),
        ConductorDni = Texto(lector, 20),
        ConductorNombres = Texto(lector, 21),
        ConductorApellidos = Texto(lector, 22),
        ConductorLicencia = Texto(lector, 23),
        ComprobanteId = lector.IsDBNull(24) ? null : lector.GetInt64(24),
        ComprobanteTipo = Texto(lector, 25)?.Trim(),
        ComprobanteNumero = lector.IsDBNull(26) ? null : Serie.Formatear(lector.GetString(26).Trim(), lector.GetInt64(27)),
        Observacion = Texto(lector, 28),
        TerminalId = lector.GetInt32(29),
        TerminalNombre = lector.GetString(30),
        Impresiones = lector.GetInt32(31),
        EstadoSunat = lector.GetString(32),
        MensajeSunat = Texto(lector, 33),
        Ticket = Texto(lector, 34),
    };

    private static string? Texto(NpgsqlDataReader lector, int columna) =>
        lector.IsDBNull(columna) ? null : lector.GetString(columna);

    private static object Nulo(string? texto) => string.IsNullOrEmpty(texto) ? DBNull.Value : texto;

    private static string? Recortar(string? texto, int largo) =>
        texto is null ? null : texto.Length <= largo ? texto : texto[..largo];
}
