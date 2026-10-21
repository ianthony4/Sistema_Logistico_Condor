using System.Reflection;
using System.Text.RegularExpressions;
using Npgsql;

namespace Condor.Datos;

/// <summary>
/// Aplica los scripts de db/migraciones que aún no se ejecutaron (T-03).
/// Los scripts van incrustados en la DLL, así que basta con abrir el programa
/// actualizado en cualquier PC para que la base de datos central se ponga al día.
/// </summary>
public sealed partial class Migrador
{
    /// <summary>Clave del bloqueo: si dos PC abren el programa a la vez, solo una migra.</summary>
    private const long ClaveBloqueo = 20261007;

    private readonly FabricaConexiones _conexiones;

    public Migrador(FabricaConexiones conexiones) => _conexiones = conexiones;

    public sealed record Migracion(int Version, string Nombre, string Sql);

    /// <summary>Migraciones incrustadas, en orden de versión.</summary>
    public static IReadOnlyList<Migracion> Disponibles()
    {
        var ensamblado = typeof(Migrador).Assembly;
        return ensamblado.GetManifestResourceNames()
            .Select(recurso => (recurso, m: PatronNombre().Match(recurso)))
            .Where(x => x.m.Success)
            .Select(x => new Migracion(
                int.Parse(x.m.Groups["version"].Value),
                x.m.Groups["nombre"].Value,
                Leer(ensamblado, x.recurso)))
            .OrderBy(m => m.Version)
            .ToList();
    }

    /// <summary>Aplica las migraciones pendientes y devuelve cuántas aplicó.</summary>
    public async Task<int> AplicarAsync(CancellationToken ct = default)
    {
        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var tx = await conexion.BeginTransactionAsync(ct);

        await Ejecutar(conexion, tx, $"SELECT pg_advisory_xact_lock({ClaveBloqueo})", ct);
        await Ejecutar(conexion, tx, """
            CREATE TABLE IF NOT EXISTS esquema_version (
                version      int          PRIMARY KEY,
                nombre       varchar(100) NOT NULL,
                aplicado_en  timestamptz  NOT NULL DEFAULT now(),
                aplicado_por varchar(100) NOT NULL DEFAULT current_setting('application_name')
            )
            """, ct);

        var aplicadas = new HashSet<int>();
        await using (var cmd = new NpgsqlCommand("SELECT version FROM esquema_version", conexion, tx))
        await using (var lector = await cmd.ExecuteReaderAsync(ct))
        {
            while (await lector.ReadAsync(ct))
                aplicadas.Add(lector.GetInt32(0));
        }

        var pendientes = Disponibles().Where(m => !aplicadas.Contains(m.Version)).ToList();
        foreach (var migracion in pendientes)
        {
            await Ejecutar(conexion, tx, migracion.Sql, ct);
            await using var registro = new NpgsqlCommand(
                "INSERT INTO esquema_version (version, nombre) VALUES (@v, @n)", conexion, tx);
            registro.Parameters.AddWithValue("v", migracion.Version);
            registro.Parameters.AddWithValue("n", migracion.Nombre);
            await registro.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return pendientes.Count;
    }

    private static async Task Ejecutar(NpgsqlConnection conexion, NpgsqlTransaction tx, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conexion, tx);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string Leer(Assembly ensamblado, string recurso)
    {
        using var flujo = ensamblado.GetManifestResourceStream(recurso)!;
        using var lector = new StreamReader(flujo);
        return lector.ReadToEnd();
    }

    // Condor.Datos.Migraciones.V001__esquema_inicial.sql
    [GeneratedRegex(@"\.V(?<version>\d+)__(?<nombre>[\w]+)\.sql$")]
    private static partial Regex PatronNombre();
}
