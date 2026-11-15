namespace Condor.Dominio.Sunat;

/// <summary>Lo que pasó en una vuelta del reenvío automático (HU-17).</summary>
public sealed record ResultadoReenvio(int Enviados, int Aceptados, int Rechazados, int SiguenPendientes, string? Corte = null)
{
    public static ResultadoReenvio Nada { get; } = new(0, 0, 0, 0);

    /// <summary>True si la vuelta se cortó porque no hay internet o la SUNAT no responde.</summary>
    public bool Cortado => Corte is not null;

    /// <summary>Texto corto para la pantalla de venta; null si no hubo nada que reenviar.</summary>
    public string? Resumen
    {
        get
        {
            if (Enviados == 0 && !Cortado)
                return null;
            var partes = new List<string>();
            if (Aceptados > 0)
                partes.Add(Aceptados == 1 ? "1 aceptado" : $"{Aceptados} aceptados");
            if (Rechazados > 0)
                partes.Add(Rechazados == 1 ? "1 rechazado" : $"{Rechazados} rechazados");
            if (SiguenPendientes > 0)
                partes.Add(SiguenPendientes == 1 ? "1 sigue pendiente" : $"{SiguenPendientes} siguen pendientes");
            var texto = Enviados == 0 ? "Reenvío a la SUNAT" : $"Reenvío a la SUNAT: {string.Join(", ", partes)}";
            return Cortado ? $"{texto}. Se detuvo: {Corte}" : $"{texto}.";
        }
    }
}

/// <summary>
/// Reenvío automático de los comprobantes pendientes (HU-17). Lo implementa
/// Condor.Sunat; la pantalla de venta lo llama cada pocos minutos.
/// </summary>
public interface IReenvioPendientes
{
    /// <summary>
    /// Una vuelta: aparta los pendientes que le tocan a esta PC, los envía y
    /// guarda la respuesta. Lanza SinConexionException si no responde la PC
    /// principal.
    /// </summary>
    /// <param name="inmediato">
    /// True cuando el usuario pide reenviar desde el Historial: no espera los
    /// minutos entre intentos, solo unos segundos tras la emisión.
    /// </param>
    Task<ResultadoReenvio> ReenviarAsync(bool inmediato = false, CancellationToken ct = default);
}
