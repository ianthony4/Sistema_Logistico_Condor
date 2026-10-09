using System.Windows;
using Condor.Presentacion.ViewModels;
using Condor.Escritorio.Vistas;

namespace Condor.Escritorio;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var ventana = new MainWindow { DataContext = new MainViewModel() };
        MainWindow = ventana;
        ventana.Show();
    }
}
