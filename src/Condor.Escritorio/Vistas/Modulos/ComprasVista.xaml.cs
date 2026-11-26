using System.Windows;
using System.Windows.Controls;
using Condor.Presentacion.ViewModels;

namespace Condor.Escritorio.Vistas.Modulos;

public partial class ComprasVista : UserControl
{
    public ComprasVista()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            CampoProveedor.Focus();
            if (DataContext is ComprasViewModel vm)
                await vm.BuscarAsync();
        };
    }

    // + Nueva compra (Alt+N): al guardar aparece en la lista, seleccionada.
    private async void NuevaCompra_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ComprasViewModel compras || compras.CrearNuevaCompra() is not { } vm)
            return;

        var dialogo = new NuevaCompraWindow(vm) { Owner = Window.GetWindow(this) };
        if (dialogo.ShowDialog() == true && vm.Guardado is not null)
        {
            await compras.CompraRegistradaAsync(vm.Guardado);
            Lista.ScrollIntoView(compras.Seleccionada);
        }
        CampoProveedor.Focus();
    }
}
