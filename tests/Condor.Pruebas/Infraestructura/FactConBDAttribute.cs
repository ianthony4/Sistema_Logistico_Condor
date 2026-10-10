namespace Condor.Pruebas.Infraestructura;

/// <summary>
/// Prueba de integración que necesita PostgreSQL. Se omite (no falla) si la
/// variable de entorno CONDOR_TEST_CONN no está definida, para que las pruebas
/// unitarias se puedan correr en cualquier PC sin base de datos.
/// </summary>
/// <example>
/// $env:CONDOR_TEST_CONN = "Host=localhost;Port=5432;Database=condor_pruebas;Username=condor_app;Password=..."
/// dotnet test
/// </example>
public sealed class FactConBDAttribute : FactAttribute
{
    public const string Variable = "CONDOR_TEST_CONN";

    public FactConBDAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Prueba de integración omitida: defina {Variable} con la cadena de conexión de la BD de pruebas.";
    }
}
