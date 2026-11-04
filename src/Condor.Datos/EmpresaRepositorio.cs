using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Npgsql;

namespace Condor.Datos;

/// <summary>
/// Datos de la ferretería para la cabecera del ticket (HU-12, T-26).
/// </summary>
public sealed class EmpresaRepositorio : IEmpresaRepositorio
{
    private readonly FabricaConexiones _conexiones;

    public EmpresaRepositorio(FabricaConexiones conexiones) => _conexiones = conexiones;

    public async Task<Empresa> ObtenerAsync(CancellationToken ct = default)
    {
        await using var conexion = await _conexiones.AbrirAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT ruc, razon_social, nombre_comercial, direccion, telefono FROM empresa WHERE id = 1", conexion);

        await using var lector = await cmd.ExecuteReaderAsync(ct);
        if (!await lector.ReadAsync(ct))
            throw new InvalidOperationException("Faltan los datos de la empresa (tabla empresa). Revise la migración V005.");

        return new Empresa
        {
            Ruc = lector.GetString(0),
            RazonSocial = lector.GetString(1),
            NombreComercial = lector.IsDBNull(2) ? null : lector.GetString(2),
            Direccion = lector.GetString(3),
            Telefono = lector.IsDBNull(4) ? null : lector.GetString(4),
        };
    }
}
