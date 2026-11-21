using System.IO;
using System.Windows;
using System.Windows.Threading;
using Condor.Datos;
using Condor.Datos.Respaldos;
using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Impresion;
using Condor.Dominio.Sunat;
using Condor.Escritorio.Impresion;
using Condor.Escritorio.Vistas;
using Condor.Impresion;
using Condor.Presentacion.ViewModels;
using Condor.Sunat;

namespace Condor.Escritorio;

public partial class App : Application
{
    /// <summary>Conexiones a la base de datos central, compartidas por toda la aplicación.</summary>
    public static FabricaConexiones? Conexiones { get; private set; }

    private ConfiguracionConexion? _config;
    private MainViewModel? _vm;
    private AvisoCatalogo? _avisoCatalogo;
    private DispatcherTimer? _monitorConexion;
    private DispatcherTimer? _temporizadorReenvio;
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
        var comprobantes = new ComprobanteRepositorio(Conexiones);
        _vm = new MainViewModel(new ProductoRepositorio(Conexiones), new ClienteRepositorio(Conexiones), comprobantes,
            new ProveedorRepositorio(Conexiones));
        _vm.IniciarSesion(Sesion.Local(nombreEquipo));

        // T-06: cada 5 s se comprueba la PC principal; si no responde se
        // muestra el aviso con Reintentar y no se borra nada de la pantalla.
        _vm.Conexion = new EstadoConexionViewModel(Conexiones.ProbarAsync);
        _vm.Conexion.ConexionRecuperada += async () => await PrepararBaseDatosAsync();
        _monitorConexion = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _monitorConexion.Tick += async (_, _) => await _vm.Conexion.ActualizarAsync();
        _monitorConexion.Start();

        // HU-12: ticketera de condor.json o, si no hay, vista previa en pantalla.
        var ticketera = CargarTicketera();
        IImpresoraTickets impresora = ticketera.Configurada
            ? new ImpresoraWindows(ticketera.Nombre!, TimeSpan.FromSeconds(ticketera.EsperaSegundos))
            : new ImpresoraVistaPrevia();
        _vm.Impresion = new ImpresionViewModel(impresora, new EmpresaRepositorio(Conexiones), comprobantes, ticketera.Columnas);

        // HU-07: cada boleta se envía a la SUNAT al emitirla. Sin la sección
        // Sunat en condor.json se emite igual y queda pendiente de envío.
        var sunat = CargarSunat();
        var servicioSunat = sunat.Configurada ? new ServicioEnvioSunat(sunat) : null;
        var registroSunat = new EnvioSunatRepositorio(Conexiones);
        _vm.EnvioSunat = new EnvioSunatViewModel(servicioSunat, new EmpresaRepositorio(Conexiones), registroSunat);

        // HU-17: los pendientes se reenvían solos cada ReenvioMinutos. Las PC
        // sin la SUNAT configurada solo muestran cuántos quedan.
        IReenvioPendientes? reenvio = servicioSunat is not null && sunat.ReenviaPendientes
            ? new ReenvioPendientes(servicioSunat, registroSunat, comprobantes, new EmpresaRepositorio(Conexiones), nombreEquipo)
            : null;
        _vm.ReenvioSunat = new ReenvioSunatViewModel(reenvio, registroSunat);
        if (reenvio is not null)
        {
            _temporizadorReenvio = new DispatcherTimer { Interval = TimeSpan.FromMinutes(sunat.ReenvioMinutos) };
            _temporizadorReenvio.Tick += async (_, _) => await _vm.ReenvioSunat.ReenviarAsync();
        }

        // HU-15: el respaldo se genera en la PC principal, donde se conecta el
        // disco externo (sección Respaldo de condor.json); las demás solo ven el último.
        var respaldo = CargarRespaldo();
        _vm.ConfigurarRespaldo(respaldo.Configurada ? new GeneradorRespaldo(Conexiones, respaldo.PgDump) : null,
            new RespaldoRepositorio(Conexiones), respaldo.Carpeta, respaldo.AvisoHoras);

        var ventana = new MainWindow { DataContext = _vm };
        MainWindow = ventana;
        ventana.Show();

        if (await _vm.Conexion.ActualizarAsync())
            await PrepararBaseDatosAsync();
        else
            _vm.Mensaje = $"Sin conexión con la PC principal ({_config}). Revise la red.";
    }

    /// <summary>Sección Ticketera de condor.json; si está mal escrita se avisa y se usa la vista previa.</summary>
    private static ConfiguracionTicketera CargarTicketera()
    {
        try
        {
            return ConfiguracionTicketera.Cargar();
        }
        catch (InvalidDataException ex)
        {
            MessageBox.Show($"{ex.Message}\nLos tickets se mostrarán en pantalla hasta corregir condor.json.",
                "Configuración de la ticketera", MessageBoxButton.OK, MessageBoxImage.Warning);
            return new ConfiguracionTicketera();
        }
    }

    /// <summary>Sección Sunat de condor.json; si está mal escrita se avisa y los comprobantes quedan pendientes.</summary>
    private static ConfiguracionSunat CargarSunat()
    {
        try
        {
            return ConfiguracionSunat.Cargar();
        }
        catch (InvalidDataException ex)
        {
            MessageBox.Show($"{ex.Message}\nLos comprobantes quedarán pendientes de envío a la SUNAT hasta corregir condor.json.",
                "Configuración de la SUNAT", MessageBoxButton.OK, MessageBoxImage.Warning);
            return new ConfiguracionSunat();
        }
    }

    /// <summary>Sección Respaldo de condor.json; si está mal escrita se avisa y esta PC no genera respaldos.</summary>
    private static ConfiguracionRespaldo CargarRespaldo()
    {
        try
        {
            return ConfiguracionRespaldo.Cargar();
        }
        catch (InvalidDataException ex)
        {
            MessageBox.Show($"{ex.Message}\nNo se podrán generar respaldos desde esta PC hasta corregir condor.json.",
                "Configuración del respaldo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return new ConfiguracionRespaldo();
        }
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

            // HU-15, criterio 2: si el último respaldo tiene más de 24 horas, se avisa.
            if (_vm.AvisoRespaldo is { } avisoRespaldo)
                await avisoRespaldo.RevisarAsync();

            // HU-17: primera vuelta del reenvío al abrir y luego cada ReenvioMinutos.
            if (_vm.ReenvioSunat is { } reenvio)
            {
                await reenvio.ActualizarAsync();
                _temporizadorReenvio?.Start();
                _ = reenvio.ReenviarAsync();
            }
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
        _temporizadorReenvio?.Stop();
        if (_avisoCatalogo is not null)
            await _avisoCatalogo.DisposeAsync();
        if (Conexiones is not null)
            await Conexiones.DisposeAsync();
        base.OnExit(e);
    }
}
