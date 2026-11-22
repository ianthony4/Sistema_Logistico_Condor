using System.Windows.Controls;
using System.Windows.Input;
using Condor.Presentacion.ViewModels;

namespace Condor.Escritorio.Vistas.Modulos;

public partial class ProformasVista : UserControl
{
    public ProformasVista()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            CampoBuscar.Focus();
            if (Vm is { } vm)
                await vm.BuscarAsync();
        };
    }

    private ProformasViewModel? Vm => DataContext as ProformasViewModel;

    // Enter o doble clic en una fila: convertir esa proforma en venta.
    private void Lista_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Vm is not { } vm)
            return;
        _ = vm.ConvertirCommand.EjecutarAsync();
        e.Handled = true;
    }

    private void Lista_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        _ = Vm?.ConvertirCommand.EjecutarAsync();
}
