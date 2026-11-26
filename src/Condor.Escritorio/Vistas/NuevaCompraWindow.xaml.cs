using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Condor.Presentacion.ViewModels;

namespace Condor.Escritorio.Vistas;

public partial class NuevaCompraWindow : Window
{
    private readonly NuevaCompraViewModel _vm;

    public NuevaCompraWindow(NuevaCompraViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        // Asignar DialogResult cierra la ventana abierta con ShowDialog.
        vm.Cerrar += guardado => DialogResult = guardado;
        vm.Buscador.ProductoElegido += _ => CampoCantidad.Focus();
        vm.LineaAgregada += () => CampoProducto.Focus();
        InputBindings.Add(new KeyBinding(vm.GuardarCommand, Key.G, ModifierKeys.Control));
    }

    // Producto: flecha abajo entra a la lista, Enter elige la primera sugerencia.
    private void CampoProducto_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var buscador = _vm.Buscador;
        if (!buscador.MostrarSugerencias)
            return;
        switch (e.Key)
        {
            case Key.Down when ListaSugerencias.Items.Count > 0:
                ListaSugerencias.SelectedIndex = Math.Max(0, ListaSugerencias.SelectedIndex);
                ListaSugerencias.UpdateLayout();
                (ListaSugerencias.ItemContainerGenerator.ContainerFromIndex(ListaSugerencias.SelectedIndex) as ListBoxItem)?.Focus();
                e.Handled = true;
                break;
            case Key.Enter:
                buscador.Elegir(buscador.Seleccionado);
                e.Handled = true;
                break;
            case Key.Escape:
                buscador.CerrarCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void ListaSugerencias_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _vm.Buscador.Elegir(_vm.Buscador.Seleccionado);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _vm.Buscador.CerrarCommand.Execute(null);
            CampoProducto.Focus();
            e.Handled = true;
        }
    }

    private void ListaSugerencias_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        _vm.Buscador.Elegir(_vm.Buscador.Seleccionado);

    // Cantidad: Enter pasa al costo.
    private void CampoCantidad_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        CampoCosto.Focus();
        CampoCosto.SelectAll();
        e.Handled = true;
    }

    // Costo: Enter agrega la línea.
    private void CampoCosto_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        _vm.Agregar();
        e.Handled = true;
    }

    // El botón Agregar ya ejecuta el comando; aquí solo se devuelve el foco si no se pudo agregar.
    private void Agregar_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.ProductoPorAgregar is not null)
            CampoCosto.Focus();
    }
}
