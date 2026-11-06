using System.Windows;
using Condor.Presentacion.ViewModels;

namespace Condor.Escritorio.Vistas;

public partial class ProveedoresWindow : Window
{
    private readonly ProveedoresViewModel _vm;

    public ProveedoresWindow(ProveedoresViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        Loaded += async (_, _) => await vm.BuscarAsync();
    }

    // + Nuevo proveedor (Alt+N): al guardar aparece en la lista, seleccionado.
    private async void NuevoProveedor_Click(object sender, RoutedEventArgs e)
    {
        var vm = _vm.CrearNuevoProveedor();
        var dialogo = new NuevoProveedorWindow(vm) { Owner = this };
        if (dialogo.ShowDialog() == true && vm.Guardado is not null)
        {
            await _vm.ProveedorRegistradoAsync(vm.Guardado);
            Lista.ScrollIntoView(_vm.Seleccionado);
        }
        CampoBuscar.Focus();
    }

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
}
