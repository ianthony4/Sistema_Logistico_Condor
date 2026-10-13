using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Npgsql;

namespace Condor.Datos;

/// <summary>
/// Registra cada PC que abre el sistema (HU-14). Los comprobantes guardan el Id
/// de la terminal para saber desde qué PC se emitieron.
/// </summary>
public sealed class TerminalRepositorio : ITerminalRepositorio
{
    private readonly FabricaConexiones _conexiones;

    public TerminalRepositorio(FabricaConexiones conexiones) => _conexiones = conexiones;

    public async Task<Terminal> RegistrarAccesoAsync(string nombre, CancellationToken ct = default)
    {
        nombre = nombre.Trim();
        if (nombre.Length is 0 or > 60)
            throw new ArgumentException("El nombre de la terminal debe tener entre 1 y 60 caracteres.", nameof(nombre));

        // Una sola sentencia: crea la terminal la primera vez y después solo
        // actualiza el último acceso. Es atómica aunque dos PC arranquen a la vez.
        const string sql = """
            INSERT INTO terminal (nombre) VALUES (@nombre)
            ON CONFLICT (nombre) DO UPDATE SET ultimo_acceso = now()
            RETURNING id, nombre, registrado_en, ultimo_acceso
            """;

        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conexion);
        cmd.Parameters.AddWithValue("nombre", nombre);

        await using var lector = await cmd.ExecuteReaderAsync(ct);
        await lector.ReadAsync(ct);
        return new Terminal
        {
            Id = lector.GetInt32(0),
            Nombre = lector.GetString(1),
            RegistradoEn = lector.GetFieldValue<DateTimeOffset>(2),
            UltimoAcceso = lector.GetFieldValue<DateTimeOffset>(3),
        };
    }
}
