using System.Windows;
using Condor.Presentacion.ViewModels;

namespace Condor.Escritorio.Vistas;

public partial class NuevoProductoWindow : Window
{
    public NuevoProductoWindow(NuevoProductoViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        // Asignar DialogResult cierra la ventana abierta con ShowDialog.
        vm.Cerrar += guardado => DialogResult = guardado;
    }
}
