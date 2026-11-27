using System.Windows;
using System.Windows.Controls;
using Condor.Presentacion.ViewModels;

namespace Condor.Escritorio.Vistas.Modulos;

public partial class GuiasVista : UserControl
{
    public GuiasVista()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            CampoBuscar.Focus();
            if (DataContext is GuiasViewModel vm)
                await vm.BuscarAsync();
        };
    }

    // Nueva guía (Alt+N): al emitirla se imprime, se envía y queda elegida.
    private async void NuevaGuia_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not GuiasViewModel guias)
            return;
        var vm = guias.CrearNuevaGuia();
        var dialogo = new NuevaGuiaWindow(vm) { Owner = Window.GetWindow(this) };
        if (dialogo.ShowDialog() == true && vm.Emitida is not null)
        {
            await guias.GuiaEmitidaAsync(vm.Emitida);
            Lista.ScrollIntoView(guias.Seleccionada);
        }
        CampoBuscar.Focus();
    }
}
