using System.Windows;
using System.Windows.Controls;
using Condor.Presentacion.ViewModels;

namespace Condor.Escritorio.Vistas.Modulos;

public partial class CatalogoVista : UserControl
{
    public CatalogoVista()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            CampoBuscar.Focus();
            if (DataContext is CatalogoViewModel vm)
                await vm.BuscarAsync();
        };
    }

    // Nuevo producto (Ctrl+N): al guardar aparece en la lista, elegido. Al
    // volver a Nueva venta el buscador se refresca y ya lo encuentra.
    private async void NuevoProducto_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CatalogoViewModel catalogo)
            return;
        var vm = catalogo.CrearNuevoProducto();
        var dialogo = new NuevoProductoWindow(vm) { Owner = Window.GetWindow(this) };
        if (dialogo.ShowDialog() == true && vm.Guardado is not null)
        {
            await catalogo.ProductoRegistradoAsync(vm.Guardado);
            Lista.ScrollIntoView(catalogo.Seleccionado);
        }
        CampoCodigo.Focus();
    }
}
