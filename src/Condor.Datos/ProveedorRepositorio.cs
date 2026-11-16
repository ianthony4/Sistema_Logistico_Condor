using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;
using Npgsql;

namespace Condor.Datos;

/// <summary>
/// Proveedores en la base de datos central (HU-04, T-37). Un proveedor
/// registrado en una PC aparece al instante en las otras dos.
/// </summary>
public sealed class ProveedorRepositorio : IProveedorRepositorio
{
    private const string Columnas =
        "id, ruc, razon_social, nombre_comercial, direccion, telefono, correo, contacto, activo, creado_en";

    private readonly FabricaConexiones _conexiones;

    public ProveedorRepositorio(FabricaConexiones conexiones) => _conexiones = conexiones;

    public async Task<Proveedor> RegistrarAsync(Proveedor proveedor, CancellationToken ct = default)
    {
        ValidadorProveedor.Validar(proveedor);

        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO proveedor (ruc, razon_social, nombre_comercial, direccion, telefono, correo, contacto)
            VALUES (@ruc, @razon, @comercial, @direccion, @telefono, @correo, @contacto)
            RETURNING id, creado_en
            """, conexion);
        cmd.Parameters.AddWithValue("ruc", proveedor.Ruc);
        cmd.Parameters.AddWithValue("razon", proveedor.RazonSocial);
        cmd.Parameters.AddWithValue("comercial", (object?)proveedor.NombreComercial ?? DBNull.Value);
        cmd.Parameters.AddWithValue("direccion", (object?)proveedor.Direccion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("telefono", (object?)proveedor.Telefono ?? DBNull.Value);
        cmd.Parameters.AddWithValue("correo", (object?)proveedor.Correo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("contacto", (object?)proveedor.Contacto ?? DBNull.Value);

        try
        {
            await using var lector = await cmd.ExecuteReaderAsync(ct);
            await lector.ReadAsync(ct);
            proveedor.Id = lector.GetInt64(0);
            proveedor.CreadoEn = lector.GetFieldValue<DateTimeOffset>(1);
            proveedor.Activo = true;
            return proveedor;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // UNIQUE (ruc): dos PC pudieron registrar al mismo proveedor a la vez.
            throw new ProveedorDuplicadoException(proveedor.Ruc);
        }
    }

    public async Task<Proveedor?> BuscarPorRucAsync(string ruc, CancellationToken ct = default)
    {
        ruc = ValidadorDocumento.Limpiar(ruc);
        if (ruc.Length != ValidadorDocumento.LargoRuc)
            return null;

        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand($"SELECT {Columnas} FROM proveedor WHERE ruc = @ruc", conexion);
        cmd.Parameters.AddWithValue("ruc", ruc);
        var encontrados = await LeerAsync(cmd, ct);
        return encontrados.Count > 0 ? encontrados[0] : null;
    }

    /// <summary>
    /// Proveedores activos cuyo RUC empieza por el texto o cuya razón social o
    /// nombre comercial lo contienen (sin distinguir mayúsculas ni tildes). Con
    /// el texto vacío, todos. Ordenados por razón social.
    /// </summary>
    public async Task<IReadOnlyList<Proveedor>> BuscarAsync(string? texto, int limite = 200, CancellationToken ct = default)
    {
        texto = texto?.Trim() ?? string.Empty;

        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand(
            $"""
            SELECT {Columnas}
            FROM proveedor
            WHERE activo
              AND (@texto = ''
                   OR ruc LIKE @texto || '%'
                   OR texto_busqueda(razon_social || ' ' || coalesce(nombre_comercial, ''))
                      LIKE '%' || texto_busqueda(@texto) || '%')
            ORDER BY razon_social
            LIMIT @limite
            """, conexion);
        cmd.Parameters.AddWithValue("texto", ProductoRepositorio.EscaparLike(texto));
        cmd.Parameters.AddWithValue("limite", Math.Clamp(limite, 1, 500));
        return await LeerAsync(cmd, ct);
    }

    private static async Task<IReadOnlyList<Proveedor>> LeerAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var proveedores = new List<Proveedor>();
        await using var lector = await cmd.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
        {
            proveedores.Add(new Proveedor
            {
                Id = lector.GetInt64(0),
                Ruc = lector.GetString(1),
                RazonSocial = lector.GetString(2),
                NombreComercial = Texto(lector, 3),
                Direccion = Texto(lector, 4),
                Telefono = Texto(lector, 5),
                Correo = Texto(lector, 6),
                Contacto = Texto(lector, 7),
                Activo = lector.GetBoolean(8),
                CreadoEn = lector.GetFieldValue<DateTimeOffset>(9),
            });
        }
        return proveedores;
    }

    private static string? Texto(NpgsqlDataReader lector, int columna) =>
        lector.IsDBNull(columna) ? null : lector.GetString(columna);
}
