using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Condor.Presentacion.ViewModels;

namespace Condor.Escritorio.Vistas;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private BuscadorProductoViewModel? Buscador => (DataContext as MainViewModel)?.Buscador;

    // Teclado del campo Producto: flecha abajo entra a la lista, Enter elige
    // la primera sugerencia y Esc cierra la lista. Así se vende sin mouse.
    private void CampoProducto_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var buscador = Buscador;
        if (buscador is null || !buscador.MostrarSugerencias)
            return;

        switch (e.Key)
        {
            case Key.Down:
                EnfocarSugerencia();
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
        var buscador = Buscador;
        if (buscador is null)
            return;

        if (e.Key == Key.Enter)
        {
            buscador.Elegir(buscador.Seleccionado);
            CampoProducto.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            buscador.CerrarCommand.Execute(null);
            CampoProducto.Focus();
            e.Handled = true;
        }
    }

    private void ListaSugerencias_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        Buscador?.Elegir(Buscador.Seleccionado);
        CampoProducto.Focus();
    }

    private void EnfocarSugerencia()
    {
        if (ListaSugerencias.Items.Count == 0)
            return;
        if (ListaSugerencias.SelectedIndex < 0)
            ListaSugerencias.SelectedIndex = 0;
        ListaSugerencias.UpdateLayout();
        if (ListaSugerencias.ItemContainerGenerator.ContainerFromIndex(ListaSugerencias.SelectedIndex) is ListBoxItem item)
            item.Focus();
    }
}
