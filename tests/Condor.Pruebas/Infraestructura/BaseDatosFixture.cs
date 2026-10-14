using Condor.Datos;

namespace Condor.Pruebas.Infraestructura;

/// <summary>
/// Conexión compartida a la base de datos de pruebas. Aplica las migraciones
/// una sola vez por ejecución. Si CONDOR_TEST_CONN no está definida, no hace
/// nada (las pruebas [FactConBD] ya se habrán omitido).
/// </summary>
public sealed class BaseDatosFixture : IAsyncLifetime
{
    private FabricaConexiones? _conexiones;

    public FabricaConexiones Conexiones =>
        _conexiones ?? throw new InvalidOperationException($"Defina {FactConBDAttribute.Variable}.");

    public async Task InitializeAsync()
    {
        var cadena = Environment.GetEnvironmentVariable(FactConBDAttribute.Variable);
        if (string.IsNullOrWhiteSpace(cadena))
            return;

        _conexiones = new FabricaConexiones(cadena);
        await new Migrador(_conexiones).AplicarAsync();
    }

    public async Task DisposeAsync()
    {
        if (_conexiones is not null)
            await _conexiones.DisposeAsync();
    }
}

/// <summary>Las clases con [Collection(BaseDatos.Nombre)] comparten la misma fixture.</summary>
[CollectionDefinition(Nombre)]
public sealed class BaseDatos : ICollectionFixture<BaseDatosFixture>
{
    public const string Nombre = "Base de datos de pruebas";
}
