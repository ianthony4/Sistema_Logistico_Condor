using System.Windows.Controls;
using System.Windows.Input;
using Condor.Presentacion.ViewModels;

namespace Condor.Escritorio.Vistas.Modulos;

public partial class HistorialVista : UserControl
{
    public HistorialVista()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            Lista.Focus();
            if (DataContext is ComprobantesRecientesViewModel vm)
                await vm.CargarAsync();
        };
    }

    // Doble clic en una fila: reimprimir ese comprobante.
    private void Lista_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        (DataContext as ComprobantesRecientesViewModel)?.ReimprimirCommand.Execute(null);
}
