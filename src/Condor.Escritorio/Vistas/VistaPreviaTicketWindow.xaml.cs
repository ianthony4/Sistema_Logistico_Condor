using System.Windows;

namespace Condor.Escritorio.Vistas;

public partial class VistaPreviaTicketWindow : Window
{
    public VistaPreviaTicketWindow(string ticket)
    {
        InitializeComponent();
        Texto.Text = ticket.TrimEnd();
    }

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
}
