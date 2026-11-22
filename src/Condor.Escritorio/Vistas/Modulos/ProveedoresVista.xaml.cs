using System.Windows;
using System.Windows.Controls;
using Condor.Presentacion.ViewModels;

namespace Condor.Escritorio.Vistas.Modulos;

public partial class ProveedoresVista : UserControl
{
    public ProveedoresVista()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            CampoBuscar.Focus();
            if (DataContext is ProveedoresViewModel vm)
                await vm.BuscarAsync();
        };
    }

    // + Nuevo proveedor (Alt+N): al guardar aparece en la lista, seleccionado.
    private async void NuevoProveedor_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProveedoresViewModel lista)
            return;
        var vm = lista.CrearNuevoProveedor();
        var dialogo = new NuevoProveedorWindow(vm) { Owner = Window.GetWindow(this) };
        if (dialogo.ShowDialog() == true && vm.Guardado is not null)
        {
            await lista.ProveedorRegistradoAsync(vm.Guardado);
            Lista.ScrollIntoView(lista.Seleccionado);
        }
        CampoBuscar.Focus();
    }
}
