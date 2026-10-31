using System.Windows;
using Condor.Dominio.Impresion;
using Condor.Escritorio.Vistas;

namespace Condor.Escritorio.Impresion;

/// <summary>
/// "Impresora" de las PC sin ticketera configurada en condor.json: muestra el
/// ticket en pantalla tal como saldría en el papel (HU-12, T-33).
/// </summary>
public sealed class ImpresoraVistaPrevia : IImpresoraTickets
{
    public string Nombre => "vista previa";

    public Task ImprimirAsync(Ticket ticket, CancellationToken ct = default)
    {
        var app = Application.Current;
        return app.Dispatcher.InvokeAsync(() =>
        {
            var ventana = new VistaPreviaTicketWindow(ticket.ComoTexto()) { Owner = app.MainWindow };
            ventana.ShowDialog();
        }).Task;
    }
}
