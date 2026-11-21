using System.ComponentModel;
using System.Diagnostics;

namespace Condor.Datos.Respaldos;

/// <summary>Ejecuta un programa externo (pg_dump). Se reemplaza en las pruebas.</summary>
public interface IEjecutorProceso
{
    /// <summary>
    /// Ejecuta el programa sin ventana y devuelve su código de salida. Cada
    /// línea que escribe en la salida de errores se entrega a <paramref name="alLeerError"/>
    /// (pg_dump --verbose informa ahí su avance). Si se cancela, lo detiene.
    /// </summary>
    Task<int> EjecutarAsync(string programa, IReadOnlyList<string> argumentos, IReadOnlyDictionary<string, string> entorno,
        Action<string> alLeerError, CancellationToken ct = default);
}

/// <summary>Ejecución real con System.Diagnostics.Process.</summary>
public sealed class EjecutorProceso : IEjecutorProceso
{
    public async Task<int> EjecutarAsync(string programa, IReadOnlyList<string> argumentos,
        IReadOnlyDictionary<string, string> entorno, Action<string> alLeerError, CancellationToken ct = default)
    {
        var inicio = new ProcessStartInfo(programa)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var argumento in argumentos)
            inicio.ArgumentList.Add(argumento);
        foreach (var (clave, valor) in entorno)
            inicio.Environment[clave] = valor;

        using var proceso = new Process { StartInfo = inicio };
        proceso.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                alLeerError(e.Data);
        };

        try
        {
            proceso.Start();
        }
        catch (Win32Exception ex)
        {
            throw new FileNotFoundException($"No se pudo ejecutar {programa}: {ex.Message}", programa, ex);
        }

        proceso.BeginErrorReadLine();
        proceso.BeginOutputReadLine();
        try
        {
            await proceso.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            proceso.Kill(entireProcessTree: true);
            throw;
        }
        return proceso.ExitCode;
    }
}
