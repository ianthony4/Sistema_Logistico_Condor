using Condor.Dominio.Entidades;

namespace Condor.Dominio.Contratos;

/// <summary>Avance del respaldo para la barra de progreso (HU-15, criterio 1).</summary>
public sealed record ProgresoRespaldo(int Porcentaje, string Paso);

/// <summary>
/// Genera el archivo de respaldo de la base de datos central (HU-15, T-61).
/// Lo implementa Condor.Datos con pg_dump.
/// </summary>
public interface IGeneradorRespaldo
{
    /// <summary>
    /// Copia toda la base de datos a un archivo nuevo en la carpeta (el disco
    /// externo) cuyo nombre lleva la fecha y la hora, e informa el avance.
    /// Devuelve el respaldo sin registrar (sin Id ni terminal). Lanza
    /// RespaldoException si la carpeta no existe, no hay espacio o pg_dump
    /// falla; en ese caso no queda ningún archivo a medias.
    /// </summary>
    Task<Respaldo> GenerarAsync(string carpeta, IProgress<ProgresoRespaldo>? progreso = null, CancellationToken ct = default);
}

/// <summary>El respaldo no se pudo generar; el mensaje dice qué hacer.</summary>
public sealed class RespaldoException : Exception
{
    public RespaldoException(string mensaje, Exception? causa = null) : base(mensaje, causa)
    {
    }
}
