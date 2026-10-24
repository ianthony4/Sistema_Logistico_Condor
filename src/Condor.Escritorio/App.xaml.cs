using System.IO;
using System.Windows;
using System.Windows.Threading;
using Condor.Datos;
using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Escritorio.Vistas;
using Condor.Presentacion.ViewModels;

namespace Condor.Escritorio;

public partial class App : Application
{
    /// <summary>Conexiones a la base de datos central, compartidas por toda la aplicación.</summary>
    public static FabricaConexiones? Conexiones { get; private set; }

    private ConfiguracionConexion? _config;
    private MainViewModel? _vm;
    private AvisoCatalogo? _avisoCatalogo;
    private DispatcherTimer? _monitorConexion;
    private bool _baseDatosLista;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _config = ConfiguracionConexion.Cargar();
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            MessageBox.Show(ex.Message, "Configuración de la base de datos",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var nombreEquipo = Environment.MachineName;
        Conexiones = new FabricaConexiones(_config, nombreEquipo);

        // HU-14: se abre directamente la pantalla Nueva venta con el perfil
        // MAESTRO, sin pedir usuario ni contraseña.
        _vm = new MainViewModel(new ProductoRepositorio(Conexiones), new ClienteRepositorio(Conexiones));
        _vm.IniciarSesion(Sesion.Local(nombreEquipo));

        // T-06: cada 5 s se comprueba la PC principal; si no responde se
        // muestra el aviso con Reintentar y no se borra nada de la pantalla.
        _vm.Conexion = new EstadoConexionViewModel(Conexiones.ProbarAsync);
        _vm.Conexion.ConexionRecuperada += async () => await PrepararBaseDatosAsync();
        _monitorConexion = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _monitorConexion.Tick += async (_, _) => await _vm.Conexion.ActualizarAsync();
        _monitorConexion.Start();

        var ventana = new MainWindow { DataContext = _vm };
        MainWindow = ventana;
        ventana.Show();

        if (await _vm.Conexion.ActualizarAsync())
            await PrepararBaseDatosAsync();
        else
            _vm.Mensaje = $"Sin conexión con la PC principal ({_config}). Revise la red.";
    }

    /// <summary>
    /// Migraciones, registro de la terminal y aviso del catálogo. Se hace una
    /// vez: al arrancar o, si arrancó sin red, cuando vuelve la conexión.
    /// </summary>
    private async Task PrepararBaseDatosAsync()
    {
        if (_baseDatosLista || Conexiones is null || _vm is null)
            return;

        try
        {
            var aplicadas = await new Migrador(Conexiones).AplicarAsync();
            var terminal = await new TerminalRepositorio(Conexiones).RegistrarAccesoAsync(Environment.MachineName);
            _vm.IniciarSesion(new Sesion(terminal));

            // T-13: los productos registrados en otra PC aparecen al instante.
            _avisoCatalogo = new AvisoCatalogo(Conexiones.CadenaConexion);
            _avisoCatalogo.CatalogoCambiado += () => Dispatcher.InvokeAsync(_vm.AlCambiarCatalogo);
            _avisoCatalogo.Iniciar();

            _baseDatosLista = true;
            _vm.Mensaje = aplicadas == 0
                ? $"Conectado a la base de datos central ({_config})."
                : $"Conectado a {_config}. Base de datos actualizada ({aplicadas} migraciones).";
        }
        catch (SinConexionException)
        {
            _vm.Conexion?.MarcarSinConexion();
            _vm.Mensaje = $"Se perdió la conexión con la PC principal ({_config}).";
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
        _monitorConexion?.Stop();
        if (_avisoCatalogo is not null)
            await _avisoCatalogo.DisposeAsync();
        if (Conexiones is not null)
            await Conexiones.DisposeAsync();
        base.OnExit(e);
    }
}
