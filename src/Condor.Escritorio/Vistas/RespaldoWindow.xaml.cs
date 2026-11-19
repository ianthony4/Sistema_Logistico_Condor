using System.ComponentModel;
using System.IO;
using System.Windows;
using Condor.Presentacion.ViewModels;
using Microsoft.Win32;

namespace Condor.Escritorio.Vistas;

public partial class RespaldoWindow : Window
{
    private readonly RespaldoViewModel _vm;

    public RespaldoWindow(RespaldoViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        Loaded += async (_, _) => await vm.CargarAsync();
    }

    // Elegir la carpeta del disco externo si Windows le dio otra letra.
    private void Examinar_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFolderDialog
        {
            Title = "Carpeta del disco externo para el respaldo",
            InitialDirectory = Directory.Exists(_vm.Carpeta) ? _vm.Carpeta : string.Empty,
        };
        if (dialogo.ShowDialog(this) == true)
            _vm.Carpeta = dialogo.FolderName;
    }

    // Mientras pg_dump copia la base no se cierra la ventana: así nadie
    // desconecta el disco creyendo que ya terminó.
    private void Ventana_Closing(object? sender, CancelEventArgs e)
    {
        if (!_vm.Generando)
            return;
        e.Cancel = true;
        MessageBox.Show(this, "Espere a que termine el respaldo antes de cerrar esta ventana.", "Respaldo",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
}
