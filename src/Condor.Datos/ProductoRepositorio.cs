using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;
using Npgsql;

namespace Condor.Datos;

/// <summary>
/// Catálogo de productos en la base de datos central (HU-01, HU-02).
/// </summary>
public sealed class ProductoRepositorio : IProductoRepositorio
{
    private const string Columnas = "id, nombre, precio, unidad, codigo_barras, activo, creado_en";

    private readonly FabricaConexiones _conexiones;

    public ProductoRepositorio(FabricaConexiones conexiones) => _conexiones = conexiones;

    public async Task<Producto> RegistrarAsync(Producto producto, CancellationToken ct = default)
    {
        ValidadorProducto.Validar(producto);

        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO producto (nombre, precio, unidad, codigo_barras)
            VALUES (@nombre, @precio, @unidad, @codigo)
            RETURNING id, creado_en
            """, conexion);
        cmd.Parameters.AddWithValue("nombre", producto.Nombre);
        cmd.Parameters.AddWithValue("precio", producto.Precio);
        cmd.Parameters.AddWithValue("unidad", producto.Unidad);
        cmd.Parameters.AddWithValue("codigo", (object?)producto.CodigoBarras ?? DBNull.Value);

        try
        {
            await using var lector = await cmd.ExecuteReaderAsync(ct);
            await lector.ReadAsync(ct);
            producto.Id = lector.GetInt64(0);
            producto.CreadoEn = lector.GetFieldValue<DateTimeOffset>(1);
            producto.Activo = true;
            return producto;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation
                                           && ex.ConstraintName == "ux_producto_nombre")
        {
            // El índice único de la BD es la garantía real: dos PC podrían
            // intentar crear el mismo producto en el mismo instante.
            throw new ProductoDuplicadoException(producto.Nombre);
        }
    }

    /// <summary>
    /// Busca por parte del nombre, sin distinguir mayúsculas ni tildes. Usa el
    /// índice GIN de trigramas de la migración V003 (T-15); los productos cuyo
    /// nombre empieza por el texto aparecen primero.
    /// </summary>
    public async Task<IReadOnlyList<Producto>> BuscarPorNombreAsync(string texto, int limite = 20, CancellationToken ct = default)
    {
        texto = texto?.Trim() ?? string.Empty;
        if (texto.Length == 0)
            return Array.Empty<Producto>();

        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand(
            $"""
            SELECT {Columnas}
            FROM producto
            WHERE activo
              AND texto_busqueda(nombre) LIKE '%' || texto_busqueda(@texto) || '%'
            ORDER BY texto_busqueda(nombre) LIKE texto_busqueda(@texto) || '%' DESC, nombre
            LIMIT @limite
            """, conexion);
        cmd.Parameters.AddWithValue("texto", EscaparLike(texto));
        cmd.Parameters.AddWithValue("limite", Math.Clamp(limite, 1, 100));
        return await LeerAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<Producto>> ListarAsync(CancellationToken ct = default)
    {
        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand(
            $"SELECT {Columnas} FROM producto WHERE activo ORDER BY nombre", conexion);
        return await LeerAsync(cmd, ct);
    }

    /// <summary>Evita que % o _ escritos por el usuario actúen como comodines de LIKE.</summary>
    public static string EscaparLike(string texto) =>
        texto.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    private static async Task<IReadOnlyList<Producto>> LeerAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var productos = new List<Producto>();
        await using var lector = await cmd.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
        {
            productos.Add(new Producto
            {
                Id = lector.GetInt64(0),
                Nombre = lector.GetString(1),
                Precio = lector.GetDecimal(2),
                Unidad = lector.GetString(3),
                CodigoBarras = lector.IsDBNull(4) ? null : lector.GetString(4),
                Activo = lector.GetBoolean(5),
                CreadoEn = lector.GetFieldValue<DateTimeOffset>(6),
            });
        }
        return productos;
    }
}
