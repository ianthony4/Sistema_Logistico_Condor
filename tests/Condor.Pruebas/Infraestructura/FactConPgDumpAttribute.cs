namespace Condor.Pruebas.Infraestructura;

/// <summary>
/// Prueba que ejecuta el pg_dump real (HU-15). Se omite si no se definen la
/// base de pruebas (CONDOR_TEST_CONN) y la ruta de pg_dump (CONDOR_PG_DUMP),
/// que debe ser de la misma versión del servidor (PostgreSQL 16).
/// </summary>
/// <example>
/// $env:CONDOR_PG_DUMP = "C:\Program Files\PostgreSQL\16\bin\pg_dump.exe"
/// dotnet test --filter "FullyQualifiedName~HU15"
/// </example>
public sealed class FactConPgDumpAttribute : FactAttribute
{
    public const string Variable = "CONDOR_PG_DUMP";

    public FactConPgDumpAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FactConBDAttribute.Variable)))
            Skip = $"Prueba con pg_dump omitida: defina {FactConBDAttribute.Variable}.";
        else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Prueba con pg_dump omitida: defina {Variable} con la ruta de pg_dump (PostgreSQL 16).";
    }
}
