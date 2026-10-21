using Condor.Datos;
using Npgsql;

namespace Condor.Pruebas.Infraestructura;

/// <summary>
/// Simula las tres PC del local: tres pools de conexiones independientes a la
/// misma base de datos, cada uno con el nombre de su terminal.
/// </summary>
public sealed class TresPC : IAsyncDisposable
{
    public TresPC()
    {
        var cadena = Environment.GetEnvironmentVariable(FactConBDAttribute.Variable)
                     ?? throw new InvalidOperationException($"Defina {FactConBDAttribute.Variable}.");
        Pc = Enumerable.Range(1, 3)
            .Select(i => new FabricaConexiones(new NpgsqlConnectionStringBuilder(cadena)
            {
                ApplicationName = $"CondorVentas@PC-{i}",
            }.ConnectionString))
            .ToArray();
    }

    /// <summary>Pc[0] es la PC-1, Pc[1] la PC-2 y Pc[2] la PC-3.</summary>
    public FabricaConexiones[] Pc { get; }

    public async ValueTask DisposeAsync()
    {
        foreach (var pc in Pc)
            await pc.DisposeAsync();
    }
}
