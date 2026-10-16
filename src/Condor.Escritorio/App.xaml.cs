using System.IO;
using System.Windows;
using Condor.Datos;
using Condor.Dominio.Entidades;
using Condor.Escritorio.Vistas;
using Condor.Presentacion.ViewModels;

namespace Condor.Escritorio;

public partial class App : Application
{
    /// <summary>Conexiones a la base de datos central, compartidas por toda la aplicación.</summary>
    public static FabricaConexiones? Conexiones { get; private set; }

    private AvisoCatalogo? _avisoCatalogo;

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

        var nombreEquipo = Environment.MachineName;
        Conexiones = new FabricaConexiones(config, nombreEquipo);

        // HU-14: se abre directamente la pantalla Nueva venta con el perfil
        // MAESTRO, sin pedir usuario ni contraseña.
        var vm = new MainViewModel(new ProductoRepositorio(Conexiones));
        vm.IniciarSesion(Sesion.Local(nombreEquipo));
        var ventana = new MainWindow { DataContext = vm };
        MainWindow = ventana;
        ventana.Show();

        if (!await Conexiones.ProbarAsync())
        {
            vm.Mensaje = $"Sin conexión con la PC principal ({config}). Revise la red.";
            return;
        }

        try
        {
            var aplicadas = await new Migrador(Conexiones).AplicarAsync();
            var terminal = await new TerminalRepositorio(Conexiones).RegistrarAccesoAsync(nombreEquipo);
            vm.IniciarSesion(new Sesion(terminal));

            // T-13: los productos registrados en otra PC aparecen al instante.
            _avisoCatalogo = new AvisoCatalogo(Conexiones.CadenaConexion);
            _avisoCatalogo.CatalogoCambiado += () => Dispatcher.InvokeAsync(vm.AlCambiarCatalogo);
            _avisoCatalogo.Iniciar();

            vm.Mensaje = aplicadas == 0
                ? $"Conectado a la base de datos central ({config})."
                : $"Conectado a {config}. Base de datos actualizada ({aplicadas} migraciones).";
        }
        catch (SinConexionException)
        {
            vm.Mensaje = $"Se perdió la conexión con la PC principal ({config}).";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo preparar la base de datos:\n{ex.Message}",
                "Base de datos", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_avisoCatalogo is not null)
            await _avisoCatalogo.DisposeAsync();
        if (Conexiones is not null)
            await Conexiones.DisposeAsync();
        base.OnExit(e);
    }
}
