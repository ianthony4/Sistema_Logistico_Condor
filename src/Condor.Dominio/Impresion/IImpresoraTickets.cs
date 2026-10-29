namespace Condor.Dominio.Impresion;

/// <summary>
/// Ticketera donde sale el comprobante (HU-12). La implementa Condor.Impresion
/// para la ticketera térmica de Windows; las pruebas usan una falsa.
/// </summary>
public interface IImpresoraTickets
{
    /// <summary>Nombre para los mensajes: el de la impresora en Windows o "vista previa".</summary>
    string Nombre { get; }

    /// <summary>
    /// Imprime el ticket. Lanza ImpresionException con un mensaje para el
    /// vendedor si la ticketera no existe, está apagada o sin papel.
    /// </summary>
    Task ImprimirAsync(Ticket ticket, CancellationToken ct = default);
}

/// <summary>El ticket no salió impreso; el mensaje explica qué revisar.</summary>
public sealed class ImpresionException : Exception
{
    public ImpresionException(string mensaje, Exception? causa = null)
        : base(mensaje, causa)
    {
    }
}
