using System.Windows;
using System.Windows.Input;
using Condor.Presentacion.ViewModels;

namespace Condor.Escritorio.Vistas;

public partial class NuevaGuiaWindow : Window
{
    private readonly NuevaGuiaViewModel _vm;

    public NuevaGuiaWindow(NuevaGuiaViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        // Asignar DialogResult cierra la ventana abierta con ShowDialog.
        vm.Cerrar += emitida => DialogResult = emitida;
        vm.Buscador.ProductoElegido += _ => CampoCantidad.Focus();
        vm.LineaAgregada += () => CampoProducto.Focus();
        Loaded += async (_, _) => await vm.InicializarAsync();
    }

    // Producto: Enter elige la primera sugerencia y pasa a la cantidad.
    private void CampoProducto_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm.Buscador.MostrarSugerencias)
        {
            _vm.Buscador.Elegir(_vm.Buscador.Seleccionado);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _vm.Buscador.MostrarSugerencias)
        {
            _vm.Buscador.CerrarCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Sugerencias_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        _vm.Buscador.Elegir(_vm.Buscador.Seleccionado);
}
