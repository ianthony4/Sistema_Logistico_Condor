using System.Windows;
using Condor.Presentacion.ViewModels;

namespace Condor.Escritorio.Vistas;

public partial class NuevoClienteWindow : Window
{
    public NuevoClienteWindow(NuevoClienteViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        // Asignar DialogResult cierra la ventana abierta con ShowDialog.
        vm.Cerrar += guardado => DialogResult = guardado;
    }
}
