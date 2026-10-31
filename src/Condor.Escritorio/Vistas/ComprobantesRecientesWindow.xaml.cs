using System.Windows;
using System.Windows.Input;
using Condor.Presentacion.ViewModels;

namespace Condor.Escritorio.Vistas;

public partial class ComprobantesRecientesWindow : Window
{
    private readonly ComprobantesRecientesViewModel _vm;

    public ComprobantesRecientesWindow(ComprobantesRecientesViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        Loaded += async (_, _) => await vm.CargarAsync();
    }

    // Doble clic en una fila: reimprimir ese comprobante.
    private void Lista_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        _vm.ReimprimirCommand.Execute(null);

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
}
