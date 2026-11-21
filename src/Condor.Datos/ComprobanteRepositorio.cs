using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Ventas;
using Npgsql;

namespace Condor.Datos;

/// <summary>
/// Comprobantes emitidos en la base de datos central (HU-05, HU-12; T-28).
/// </summary>
/// <remarks>
/// EmitirAsync toma el número de la serie, guarda la cabecera y todas las
/// líneas en una sola transacción. La fila de la serie queda bloqueada hasta
/// el COMMIT (T-05), así que dos PC que emiten a la vez reciben números
/// seguidos; y si algo falla (la red, una línea inválida) el ROLLBACK deshace
/// también el número: no quedan boletas a medias ni números saltados.
/// Desde V011 guarda la vigencia de la proforma y de qué proforma salió cada
/// venta; la base central admite una sola conversión por proforma (HU-09, T-70).
/// </remarks>
public sealed class ComprobanteRepositorio : IComprobanteRepositorio
{
    private const string Cabecera = """
        SELECT c.id, s.tipo_comprobante, s.id, s.codigo, c.numero, c.fecha, c.terminal_id, t.nombre,
               c.usuario, c.op_gravada, c.igv, c.total, c.estado_sunat, c.impresiones,
               cl.id, cl.tipo_documento, cl.numero_documento, cl.nombre, cl.direccion,
               coalesce(cs.ultimo_error, cs.descripcion),
               c.valida_hasta, c.proforma_id, sv.codigo, v.numero
        FROM comprobante c
        JOIN serie s ON s.id = c.serie_id
        JOIN terminal t ON t.id = c.terminal_id
        LEFT JOIN cliente cl ON cl.id = c.cliente_id
        LEFT JOIN comprobante_sunat cs ON cs.comprobante_id = c.id
        LEFT JOIN comprobante v ON v.proforma_id = c.id
        LEFT JOIN serie sv ON sv.id = v.serie_id
        """;

    private readonly FabricaConexiones _conexiones;

    public ComprobanteRepositorio(FabricaConexiones conexiones) => _conexiones = conexiones;

    public async Task<Comprobante> EmitirAsync(Comprobante comprobante, CancellationToken ct = default)
    {
        if (comprobante.Detalle.Count == 0)
            throw new InvalidOperationException("El comprobante no tiene productos.");
        if (comprobante.TerminalId <= 0)
            throw new InvalidOperationException("Esta PC aún no está registrada en la base de datos central; espere a que se conecte.");

        await using var conexion = await _conexiones.AbrirAsync(ct);
        try
        {
            await using var tx = await conexion.BeginTransactionAsync(ct);
            var numero = await SerieRepositorio.SiguienteCorrelativoAsync(conexion, tx, comprobante.SerieCodigo, ct);

            await using (var cmd = new NpgsqlCommand(
                """
                INSERT INTO comprobante (serie_id, numero, cliente_id, terminal_id, usuario, op_gravada, igv, total,
                                         valida_hasta, proforma_id)
                SELECT s.id, @numero, @cliente, @terminal, @usuario, @gravada, @igv, @total,
                       CASE WHEN s.tipo_comprobante = 'PR' THEN (now() AT TIME ZONE 'America/Lima')::date + @vigencia - 1 END,
                       @proforma
                FROM serie s
                WHERE s.codigo = @serie AND s.tipo_comprobante = @tipo
                RETURNING id, serie_id, fecha, estado_sunat, valida_hasta
                """, conexion, tx))
            {
                cmd.Parameters.AddWithValue("numero", numero);
                cmd.Parameters.AddWithValue("cliente", (object?)comprobante.ClienteId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("terminal", comprobante.TerminalId);
                cmd.Parameters.AddWithValue("usuario", comprobante.Usuario);
                cmd.Parameters.AddWithValue("gravada", comprobante.OpGravada);
                cmd.Parameters.AddWithValue("igv", comprobante.Igv);
                cmd.Parameters.AddWithValue("total", comprobante.Total);
                cmd.Parameters.AddWithValue("serie", comprobante.SerieCodigo.Trim().ToUpperInvariant());
                cmd.Parameters.AddWithValue("tipo", comprobante.TipoComprobante);
                cmd.Parameters.AddWithValue("vigencia", EmisionComprobante.DiasVigenciaProforma);
                cmd.Parameters.AddWithValue("proforma", (object?)comprobante.ProformaId ?? DBNull.Value);

                await using var lector = await cmd.ExecuteReaderAsync(ct);
                if (!await lector.ReadAsync(ct))
                    throw new InvalidOperationException(
                        $"La serie {comprobante.SerieCodigo} no corresponde a {comprobante.NombreTipo.ToLowerInvariant()}.");

                comprobante.Id = lector.GetInt64(0);
                comprobante.SerieId = lector.GetInt32(1);
                comprobante.Fecha = lector.GetFieldValue<DateTimeOffset>(2);
                comprobante.EstadoSunat = lector.GetString(3);
                comprobante.ValidaHasta = lector.IsDBNull(4) ? null : lector.GetFieldValue<DateOnly>(4);
            }

            // Todas las líneas en un solo viaje a la PC principal.
            await using (var lote = new NpgsqlBatch(conexion, tx))
            {
                foreach (var linea in comprobante.Detalle)
                {
                    var cmd = new NpgsqlBatchCommand(
                        """
                        INSERT INTO comprobante_detalle
                            (comprobante_id, item, producto_id, descripcion, cantidad, precio_unitario, importe)
                        VALUES (@comprobante, @item, @producto, @descripcion, @cantidad, @precio, @importe)
                        """);
                    cmd.Parameters.AddWithValue("comprobante", comprobante.Id);
                    cmd.Parameters.AddWithValue("item", linea.Item);
                    cmd.Parameters.AddWithValue("producto", linea.ProductoId);
                    cmd.Parameters.AddWithValue("descripcion", linea.Descripcion);
                    cmd.Parameters.AddWithValue("cantidad", linea.Cantidad);
                    cmd.Parameters.AddWithValue("precio", linea.PrecioUnitario);
                    cmd.Parameters.AddWithValue("importe", linea.Importe);
                    lote.BatchCommands.Add(cmd);
                }
                await lote.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            comprobante.Numero = numero;
            return comprobante;
        }
        catch (Exception ex) when (ex is not SinConexionException && FabricaConexiones.EsFallaDeConexion(ex))
        {
            // La red se cayó a mitad de la emisión: la transacción no se
            // confirmó y la venta sigue en pantalla para reintentar (T-06).
            comprobante.Id = 0;
            throw new SinConexionException(ex);
        }
        catch (PostgresException ex) when (ex.ConstraintName == "ck_factura_con_ruc")
        {
            // V008: la base central no acepta una factura sin cliente con RUC (HU-06).
            comprobante.Id = 0;
            throw new InvalidOperationException("La factura requiere un cliente con RUC registrado: búsquelo por su RUC antes de emitir.", ex);
        }
        catch (PostgresException ex) when (ex.ConstraintName == "ux_comprobante_proforma")
        {
            // V011: otra PC convirtió la misma proforma un instante antes (HU-09).
            comprobante.Id = 0;
            throw new ProformaYaConvertidaException(await NumeroAsync(comprobante.ProformaId), ex);
        }
        catch (PostgresException ex) when (ex.ConstraintName == "ck_proforma_vencida")
        {
            comprobante.Id = 0;
            throw new InvalidOperationException(
                $"La proforma {await NumeroAsync(comprobante.ProformaId)} está vencida: emita una proforma nueva con los precios de hoy.", ex);
        }
        catch
        {
            comprobante.Id = 0;
            throw;
        }
    }

    public async Task<Comprobante?> ObtenerAsync(long id, CancellationToken ct = default)
    {
        await using var conexion = await _conexiones.AbrirAsync(ct);

        Comprobante? comprobante;
        await using (var cmd = new NpgsqlCommand($"{Cabecera} WHERE c.id = @id", conexion))
        {
            cmd.Parameters.AddWithValue("id", id);
            await using var lector = await cmd.ExecuteReaderAsync(ct);
            comprobante = await lector.ReadAsync(ct) ? LeerCabecera(lector) : null;
        }
        if (comprobante is null)
            return null;

        await using (var cmd = new NpgsqlCommand(
            """
            SELECT d.item, d.producto_id, d.descripcion, p.unidad, d.cantidad, d.precio_unitario, d.importe
            FROM comprobante_detalle d
            JOIN producto p ON p.id = d.producto_id
            WHERE d.comprobante_id = @id
            ORDER BY d.item
            """, conexion))
        {
            cmd.Parameters.AddWithValue("id", id);
            await using var lector = await cmd.ExecuteReaderAsync(ct);
            while (await lector.ReadAsync(ct))
            {
                comprobante.Detalle.Add(new DetalleComprobante
                {
                    Item = lector.GetInt32(0),
                    ProductoId = lector.GetInt64(1),
                    Descripcion = lector.GetString(2),
                    Unidad = lector.GetString(3),
                    Cantidad = lector.GetDecimal(4),
                    PrecioUnitario = lector.GetDecimal(5),
                    Importe = lector.GetDecimal(6),
                });
            }
        }

        return comprobante;
    }

    public async Task<IReadOnlyList<Comprobante>> ListarRecientesAsync(int limite = 30, CancellationToken ct = default)
    {
        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand($"{Cabecera} ORDER BY c.id DESC LIMIT @limite", conexion);
        cmd.Parameters.AddWithValue("limite", Math.Clamp(limite, 1, 200));

        var comprobantes = new List<Comprobante>();
        await using var lector = await cmd.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
            comprobantes.Add(LeerCabecera(lector));
        return comprobantes;
    }

    public async Task RegistrarImpresionAsync(long id, CancellationToken ct = default)
    {
        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE comprobante SET impresiones = impresiones + 1, impreso_en = now() WHERE id = @id", conexion);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Número completo (PR01-00000004) de un comprobante, para los mensajes de error.</summary>
    private async Task<string?> NumeroAsync(long? id)
    {
        if (id is null)
            return null;
        try
        {
            return (await ObtenerAsync(id.Value))?.NumeroCompleto;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Comprobante LeerCabecera(NpgsqlDataReader lector)
    {
        var comprobante = new Comprobante
        {
            Id = lector.GetInt64(0),
            TipoComprobante = lector.GetString(1).Trim(),
            SerieId = lector.GetInt32(2),
            SerieCodigo = lector.GetString(3).Trim(),
            Numero = lector.GetInt64(4),
            Fecha = lector.GetFieldValue<DateTimeOffset>(5),
            TerminalId = lector.GetInt32(6),
            TerminalNombre = lector.GetString(7),
            Usuario = lector.GetString(8),
            OpGravada = lector.GetDecimal(9),
            Igv = lector.GetDecimal(10),
            Total = lector.GetDecimal(11),
            EstadoSunat = lector.GetString(12),
            Impresiones = lector.GetInt32(13),
            MensajeSunat = lector.IsDBNull(19) ? null : lector.GetString(19),
            ValidaHasta = lector.IsDBNull(20) ? null : lector.GetFieldValue<DateOnly>(20),
            ProformaId = lector.IsDBNull(21) ? null : lector.GetInt64(21),
            ConvertidaEn = lector.IsDBNull(22) ? null : Serie.Formatear(lector.GetString(22).Trim(), lector.GetInt64(23)),
        };

        if (!lector.IsDBNull(14))
        {
            comprobante.Cliente = ClienteRepositorio.Leer(lector, desde: 14);
            comprobante.ClienteId = comprobante.Cliente.Id;
        }
        return comprobante;
    }
}
