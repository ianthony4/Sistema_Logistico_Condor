using System.IO;
using System.Windows;
using Condor.Datos;
using Condor.Presentacion.ViewModels;
using Condor.Escritorio.Vistas;

namespace Condor.Escritorio;

public partial class App : Application
{
    /// <summary>Conexiones a la base de datos central, compartidas por toda la aplicación.</summary>
    public static FabricaConexiones? Conexiones { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ConfiguracionConexion config;
        try
        {
            config = ConfiguracionConexion.Cargar();
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            MessageBox.Show(ex.Message, "Configuración de la base de datos",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        Conexiones = new FabricaConexiones(config, Environment.MachineName);

        var vm = new MainViewModel();
        var ventana = new MainWindow { DataContext = vm };
        MainWindow = ventana;
        ventana.Show();

        vm.Mensaje = await Conexiones.ProbarAsync()
            ? $"Conectado a la base de datos central ({config})."
            : $"Sin conexión con la PC principal ({config}). Revise la red.";
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (Conexiones is not null)
            await Conexiones.DisposeAsync();
        base.OnExit(e);
    }
}
