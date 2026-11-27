using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Condor.Presentacion.ViewModels;

namespace Condor.Escritorio.Vistas.Modulos;

public partial class ClientesVista : UserControl
{
    public ClientesVista()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            CampoBuscar.Focus();
            if (DataContext is ClientesViewModel vm)
                await vm.BuscarAsync();
        };
    }

    // + Nuevo cliente (Alt+N): al guardar aparece en la lista, elegido.
    private async void NuevoCliente_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ClientesViewModel clientes)
            return;
        var vm = clientes.CrearNuevoCliente();
        var dialogo = new NuevoClienteWindow(vm) { Owner = Window.GetWindow(this) };
        if (dialogo.ShowDialog() == true && vm.Guardado is not null)
        {
            await clientes.ClienteRegistradoAsync(vm.Guardado);
            Lista.ScrollIntoView(clientes.Seleccionado);
        }
        CampoBuscar.Focus();
    }

    // Doble clic: usar ese cliente en la venta.
    private void Lista_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        (DataContext as ClientesViewModel)?.UsarCommand.Execute(null);
}
