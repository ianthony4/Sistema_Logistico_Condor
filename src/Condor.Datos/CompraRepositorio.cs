using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;
using Npgsql;

namespace Condor.Datos;

/// <summary>
/// Compras a los proveedores en la base de datos central (HU-11, T-79).
/// </summary>
/// <remarks>
/// La cabecera y todas las líneas se guardan en una sola transacción: si algo
/// falla no queda una compra a medias. UNIQUE (proveedor, tipo, serie, número)
/// impide registrar dos veces el mismo comprobante del proveedor, aunque dos
/// PC lo intenten en el mismo instante.
/// </remarks>
public sealed class CompraRepositorio : ICompraRepositorio
{
    private const string Cabecera = """
        SELECT c.id, c.proveedor_id, p.ruc, p.razon_social, p.nombre_comercial,
               c.tipo_documento, c.serie, c.numero, c.fecha_emision, c.registrada_en,
               c.terminal_id, t.nombre, c.observacion, c.op_gravada, c.igv, c.total
        FROM compra c
        JOIN proveedor p ON p.id = c.proveedor_id
        JOIN terminal t ON t.id = c.terminal_id
        """;

    private readonly FabricaConexiones _conexiones;

    public CompraRepositorio(FabricaConexiones conexiones) => _conexiones = conexiones;

    public async Task<Compra> RegistrarAsync(Compra compra, CancellationToken ct = default)
    {
        if (compra.Detalle.Count == 0)
            throw new InvalidOperationException("La compra no tiene productos.");
        if (compra.TerminalId <= 0)
            throw new InvalidOperationException("Esta PC aún no está registrada en la base de datos central; espere a que se conecte.");

        await using var conexion = await _conexiones.AbrirAsync(ct);
        try
        {
            await using var tx = await conexion.BeginTransactionAsync(ct);

            await using (var cmd = new NpgsqlCommand(
                """
                INSERT INTO compra (proveedor_id, tipo_documento, serie, numero, fecha_emision, terminal_id,
                                    observacion, op_gravada, igv, total)
                VALUES (@proveedor, @tipo, @serie, @numero, @fecha, @terminal, @observacion, @gravada, @igv, @total)
                RETURNING id, registrada_en
                """, conexion, tx))
            {
                cmd.Parameters.AddWithValue("proveedor", compra.ProveedorId);
                cmd.Parameters.AddWithValue("tipo", compra.TipoDocumento);
                cmd.Parameters.AddWithValue("serie", compra.Serie);
                cmd.Parameters.AddWithValue("numero", compra.Numero);
                cmd.Parameters.AddWithValue("fecha", compra.FechaEmision);
                cmd.Parameters.AddWithValue("terminal", compra.TerminalId);
                cmd.Parameters.AddWithValue("observacion", (object?)compra.Observacion ?? DBNull.Value);
                cmd.Parameters.AddWithValue("gravada", compra.OpGravada);
                cmd.Parameters.AddWithValue("igv", compra.Igv);
                cmd.Parameters.AddWithValue("total", compra.Total);

                await using var lector = await cmd.ExecuteReaderAsync(ct);
                await lector.ReadAsync(ct);
                compra.Id = lector.GetInt64(0);
                compra.RegistradaEn = lector.GetFieldValue<DateTimeOffset>(1);
            }

            // Todas las líneas en un solo viaje a la PC principal.
            await using (var lote = new NpgsqlBatch(conexion, tx))
            {
                foreach (var linea in compra.Detalle)
                {
                    var cmd = new NpgsqlBatchCommand(
                        """
                        INSERT INTO compra_detalle (compra_id, item, producto_id, descripcion, cantidad, costo_unitario, importe)
                        VALUES (@compra, @item, @producto, @descripcion, @cantidad, @costo, @importe)
                        """);
                    cmd.Parameters.AddWithValue("compra", compra.Id);
                    cmd.Parameters.AddWithValue("item", linea.Item);
                    cmd.Parameters.AddWithValue("producto", linea.ProductoId);
                    cmd.Parameters.AddWithValue("descripcion", linea.Descripcion);
                    cmd.Parameters.AddWithValue("cantidad", linea.Cantidad);
                    cmd.Parameters.AddWithValue("costo", linea.CostoUnitario);
                    cmd.Parameters.AddWithValue("importe", linea.Importe);
                    lote.BatchCommands.Add(cmd);
                }
                await lote.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return compra;
        }
        catch (Exception ex) when (ex is not SinConexionException && FabricaConexiones.EsFallaDeConexion(ex))
        {
            // La red se cayó a mitad: no se confirmó nada y el diálogo conserva lo escrito (T-06).
            compra.Id = 0;
            throw new SinConexionException(ex);
        }
        catch (PostgresException ex) when (ex.ConstraintName == "ux_compra_documento")
        {
            compra.Id = 0;
            throw new CompraDuplicadaException(compra.DocumentoCompleto, compra.Proveedor?.RazonSocial);
        }
        catch
        {
            compra.Id = 0;
            throw;
        }
    }

    public async Task<Compra?> ObtenerAsync(long id, CancellationToken ct = default)
    {
        await using var conexion = await _conexiones.AbrirAsync(ct);

        Compra? compra;
        await using (var cmd = new NpgsqlCommand($"{Cabecera} WHERE c.id = @id", conexion))
        {
            cmd.Parameters.AddWithValue("id", id);
            await using var lector = await cmd.ExecuteReaderAsync(ct);
            compra = await lector.ReadAsync(ct) ? LeerCabecera(lector) : null;
        }
        if (compra is null)
            return null;

        await using (var cmd = new NpgsqlCommand(
            """
            SELECT d.item, d.producto_id, d.descripcion, p.unidad, d.cantidad, d.costo_unitario, d.importe
            FROM compra_detalle d
            JOIN producto p ON p.id = d.producto_id
            WHERE d.compra_id = @id
            ORDER BY d.item
            """, conexion))
        {
            cmd.Parameters.AddWithValue("id", id);
            await using var lector = await cmd.ExecuteReaderAsync(ct);
            while (await lector.ReadAsync(ct))
            {
                compra.Detalle.Add(new DetalleCompra
                {
                    Item = lector.GetInt32(0),
                    ProductoId = lector.GetInt64(1),
                    Descripcion = lector.GetString(2),
                    Unidad = lector.GetString(3),
                    Cantidad = lector.GetDecimal(4),
                    CostoUnitario = lector.GetDecimal(5),
                    Importe = lector.GetDecimal(6),
                });
            }
        }

        return compra;
    }

    public async Task<ResultadoCompras> BuscarAsync(FiltroCompras filtro, CancellationToken ct = default)
    {
        var proveedor = filtro.Proveedor?.Trim() ?? string.Empty;

        // count(*) y sum() OVER () cuentan todas las compras del filtro, no solo
        // las que entran en el límite: el total de egresos sale completo.
        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand(
            $"""
            SELECT x.*, count(*) OVER (), sum(x.total) OVER ()
            FROM ({Cabecera}
                  WHERE (@desde::date IS NULL OR c.fecha_emision >= @desde)
                    AND (@hasta::date IS NULL OR c.fecha_emision <= @hasta)
                    AND (@proveedor = ''
                         OR p.ruc LIKE @proveedor || '%'
                         OR texto_busqueda(p.razon_social || ' ' || coalesce(p.nombre_comercial, ''))
                            LIKE '%' || texto_busqueda(@proveedor) || '%')) AS x
            ORDER BY x.fecha_emision DESC, x.id DESC
            LIMIT @limite
            """, conexion);
        cmd.Parameters.Add(new NpgsqlParameter("desde", NpgsqlTypes.NpgsqlDbType.Date) { Value = (object?)filtro.Desde ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("hasta", NpgsqlTypes.NpgsqlDbType.Date) { Value = (object?)filtro.Hasta ?? DBNull.Value });
        cmd.Parameters.AddWithValue("proveedor", ProductoRepositorio.EscaparLike(proveedor));
        cmd.Parameters.AddWithValue("limite", Math.Clamp(filtro.Limite, 1, 1000));

        var compras = new List<Compra>();
        var cantidad = 0;
        var total = 0m;
        await using var lector = await cmd.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
        {
            compras.Add(LeerCabecera(lector));
            cantidad = (int)lector.GetInt64(16);
            total = lector.GetDecimal(17);
        }
        return compras.Count == 0 ? ResultadoCompras.Ninguna : new ResultadoCompras(compras, cantidad, total);
    }

    private static Compra LeerCabecera(NpgsqlDataReader lector) => new()
    {
        Id = lector.GetInt64(0),
        ProveedorId = lector.GetInt64(1),
        Proveedor = new Proveedor
        {
            Id = lector.GetInt64(1),
            Ruc = lector.GetString(2),
            RazonSocial = lector.GetString(3),
            NombreComercial = lector.IsDBNull(4) ? null : lector.GetString(4),
        },
        TipoDocumento = lector.GetString(5),
        Serie = lector.GetString(6),
        Numero = lector.GetInt64(7),
        FechaEmision = lector.GetFieldValue<DateOnly>(8),
        RegistradaEn = lector.GetFieldValue<DateTimeOffset>(9),
        TerminalId = lector.GetInt32(10),
        TerminalNombre = lector.GetString(11),
        Observacion = lector.IsDBNull(12) ? null : lector.GetString(12),
        OpGravada = lector.GetDecimal(13),
        Igv = lector.GetDecimal(14),
        Total = lector.GetDecimal(15),
    };
}
