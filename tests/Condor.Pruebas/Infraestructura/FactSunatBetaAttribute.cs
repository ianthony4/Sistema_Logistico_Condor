namespace Condor.Pruebas.Infraestructura;

/// <summary>
/// Prueba contra el ambiente beta real de la SUNAT (necesita internet). Se
/// omite si CONDOR_SUNAT_BETA no vale 1, para que las pruebas corran sin red.
/// </summary>
/// <example>
/// $env:CONDOR_SUNAT_BETA = "1"
/// dotnet test --filter "FullyQualifiedName~HU07"
/// </example>
public sealed class FactSunatBetaAttribute : FactAttribute
{
    public const string Variable = "CONDOR_SUNAT_BETA";

    public FactSunatBetaAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
            Skip = $"Prueba con la SUNAT beta omitida: defina {Variable}=1 (requiere internet).";
    }
}
