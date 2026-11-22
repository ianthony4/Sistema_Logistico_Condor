using System.IO;
using System.Windows;
using System.Windows.Controls;
using Condor.Presentacion.ViewModels;
using Microsoft.Win32;

namespace Condor.Escritorio.Vistas.Modulos;

public partial class RespaldoVista : UserControl
{
    public RespaldoVista()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            BotonGenerar.Focus();
            if (DataContext is RespaldoViewModel vm)
                await vm.CargarAsync();
        };
    }

    // Elegir la carpeta del disco externo si Windows le dio otra letra.
    private void Examinar_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RespaldoViewModel vm)
            return;
        var dialogo = new OpenFolderDialog
        {
            Title = "Carpeta del disco externo para el respaldo",
            InitialDirectory = Directory.Exists(vm.Carpeta) ? vm.Carpeta : string.Empty,
        };
        if (dialogo.ShowDialog(Window.GetWindow(this)) == true)
            vm.Carpeta = dialogo.FolderName;
    }
}
