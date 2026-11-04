using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;
using Npgsql;

namespace Condor.Datos;

/// <summary>
/// Clientes en la base de datos central (HU-03, T-22). Un cliente registrado
/// en una PC se encuentra al instante desde las otras dos.
/// </summary>
public sealed class ClienteRepositorio : IClienteRepositorio
{
    private const string Columnas = "id, tipo_documento, numero_documento, nombre, direccion";

    private readonly FabricaConexiones _conexiones;

    public ClienteRepositorio(FabricaConexiones conexiones) => _conexiones = conexiones;

    public async Task<Cliente?> BuscarPorDocumentoAsync(TipoDocumento tipo, string numero, CancellationToken ct = default)
    {
        numero = ValidadorDocumento.Limpiar(numero);
        if (numero.Length == 0)
            return null;

        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand(
            $"""
            SELECT {Columnas}
            FROM cliente
            WHERE tipo_documento = @tipo AND numero_documento = @numero
            """, conexion);
        cmd.Parameters.AddWithValue("tipo", CodigoTipo(tipo));
        cmd.Parameters.AddWithValue("numero", numero);

        await using var lector = await cmd.ExecuteReaderAsync(ct);
        return await lector.ReadAsync(ct) ? Leer(lector) : null;
    }

    public async Task<Cliente> RegistrarAsync(Cliente cliente, CancellationToken ct = default)
    {
        ValidadorCliente.Validar(cliente);

        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO cliente (tipo_documento, numero_documento, nombre, direccion)
            VALUES (@tipo, @numero, @nombre, @direccion)
            RETURNING id
            """, conexion);
        cmd.Parameters.AddWithValue("tipo", CodigoTipo(cliente.TipoDocumento));
        cmd.Parameters.AddWithValue("numero", cliente.NumeroDocumento);
        cmd.Parameters.AddWithValue("nombre", cliente.Nombre);
        cmd.Parameters.AddWithValue("direccion", (object?)cliente.Direccion ?? DBNull.Value);

        try
        {
            cliente.Id = (long)(await cmd.ExecuteScalarAsync(ct))!;
            return cliente;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // UNIQUE (tipo_documento, numero_documento): dos PC pudieron
            // registrar al mismo cliente en el mismo instante.
            throw new ClienteDuplicadoException(cliente.TipoDocumento, cliente.NumeroDocumento);
        }
    }

    /// <summary>Código del catálogo 06 de la SUNAT tal como se guarda en tipo_documento (char(1)).</summary>
    public static string CodigoTipo(TipoDocumento tipo) => ((int)tipo).ToString();

    /// <summary>Lee un cliente de las columnas de <see cref="Columnas"/> a partir de la posición indicada.</summary>
    internal static Cliente Leer(NpgsqlDataReader lector, int desde = 0) => new()
    {
        Id = lector.GetInt64(desde),
        TipoDocumento = (TipoDocumento)int.Parse(lector.GetString(desde + 1)),
        NumeroDocumento = lector.GetString(desde + 2),
        Nombre = lector.GetString(desde + 3),
        Direccion = lector.IsDBNull(desde + 4) ? null : lector.GetString(desde + 4),
    };
}
