using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// ViewModel de la ventana principal (pantalla Nueva venta).
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly IProductoRepositorio? _productos;
    private readonly IClienteRepositorio? _clientes;
    private readonly IComprobanteRepositorio? _comprobantes;
    private ImpresionViewModel? _impresion;
    private string _mensaje = "Listo.";
    private Sesion? _sesion;
    private EstadoConexionViewModel? _conexion;

    /// <param name="productos">Catálogo; si es null no se muestra el buscador.</param>
    /// <param name="clientes">Clientes; si es null no se muestra el panel Cliente (HU-03).</param>
    /// <param name="comprobantes">Comprobantes; si es null el botón Emitir no está disponible (HU-05).</param>
    public MainViewModel(IProductoRepositorio? productos = null, IClienteRepositorio? clientes = null,
        IComprobanteRepositorio? comprobantes = null)
    {
        ModuloPendienteCommand = new RelayCommand(
            p => Mensaje = $"El módulo {p} se implementará en un próximo sprint.");

        Venta.LineaAgregada += linea =>
        {
            Buscador?.Limpiar();
            Mensaje = $"Agregado: {linea.Cantidad:0.###} x {linea.Descripcion}. Total S/ {Venta.Total:0.00}";
        };

        _productos = productos;
        if (productos is not null)
        {
            Buscador = new BuscadorProductoViewModel(productos);
            Buscador.ProductoElegido += AlElegirProducto;
            Buscador.ConexionPerdida += () => Conexion?.MarcarSinConexion();
        }

        _clientes = clientes;
        if (clientes is not null)
        {
            Cliente = new ClienteVentaViewModel(clientes);
            Cliente.ConexionPerdida += () => Conexion?.MarcarSinConexion();
        }

        _comprobantes = comprobantes;
        if (comprobantes is not null)
        {
            Emision = new EmisionViewModel(comprobantes, Venta, Cliente, () => Sesion);
            Emision.ConexionPerdida += () => Conexion?.MarcarSinConexion();
            Emision.Emitido += AlEmitir;
        }
    }

    public string Titulo => "Ferretería Cóndor Majes - Nueva venta";

    public Sesion? Sesion
    {
        get => _sesion;
        private set
        {
            if (SetProperty(ref _sesion, value))
            {
                OnPropertyChanged(nameof(Terminal));
                OnPropertyChanged(nameof(Usuario));
            }
        }
    }

    public string Terminal => Sesion?.Terminal.Nombre ?? "-";

    /// <summary>Siempre MAESTRO: el sistema no pide usuario ni contraseña (HU-14).</summary>
    public string Usuario => Sesion?.Usuario ?? "-";

    /// <summary>Texto de la barra de estado.</summary>
    public string Mensaje
    {
        get => _mensaje;
        set => SetProperty(ref _mensaje, value);
    }

    /// <summary>Aviso de pérdida de conexión con la PC principal (T-06).</summary>
    public EstadoConexionViewModel? Conexion
    {
        get => _conexion;
        set
        {
            if (!SetProperty(ref _conexion, value) || value is null)
                return;
            value.ConexionRecuperada += () =>
            {
                Mensaje = "Conexión con la PC principal recuperada.";
                Buscador?.Refrescar();
            };
        }
    }

    /// <summary>Detalle y totales de la venta en curso (T-17).</summary>
    public VentaViewModel Venta { get; } = new();

    /// <summary>Campo Producto con autocompletado (HU-02).</summary>
    public BuscadorProductoViewModel? Buscador { get; }

    /// <summary>Cliente de la venta: DNI o RUC con Buscar (HU-03).</summary>
    public ClienteVentaViewModel? Cliente { get; }

    /// <summary>Tipo de comprobante y Emitir e imprimir, F10 (HU-05).</summary>
    public EmisionViewModel? Emision { get; }

    /// <summary>Ticketera: imprime al emitir y reimprime con F11 (HU-12). La asigna el arranque.</summary>
    public ImpresionViewModel? Impresion
    {
        get => _impresion;
        set
        {
            if (SetProperty(ref _impresion, value) && value is not null)
                value.ConexionPerdida += () => Conexion?.MarcarSinConexion();
        }
    }

    /// <summary>Última impresión lanzada al emitir; las pruebas la esperan con await.</summary>
    public Task UltimaImpresion { get; private set; } = Task.CompletedTask;

    /// <summary>Atiende los botones F2 a F9 de módulos que aún no existen.</summary>
    public RelayCommand ModuloPendienteCommand { get; }

    public void IniciarSesion(Sesion sesion) => Sesion = sesion;

    /// <summary>Descarta la venta en curso: detalle, producto a medio elegir y cliente (Esc).</summary>
    public void CancelarVenta()
    {
        Venta.Cancelar();
        Buscador?.Limpiar();
        Cliente?.Limpiar();
        Mensaje = "Venta cancelada.";
    }

    /// <summary>
    /// ViewModel del diálogo Nuevo producto (HU-01). Si lo escrito en el campo
    /// Producto no encontró nada, se usa como nombre propuesto.
    /// </summary>
    public NuevoProductoViewModel? CrearNuevoProducto()
    {
        if (_productos is null)
            return null;

        var vm = new NuevoProductoViewModel(_productos);
        vm.ConexionPerdida += () => Conexion?.MarcarSinConexion();
        if (Buscador is { Sugerencias.Count: 0 } && !string.IsNullOrWhiteSpace(Buscador.Texto))
            vm.Nombre = Buscador.Texto.Trim();
        return vm;
    }

    /// <summary>Tras guardar en el diálogo: avisa y deja el producto elegido para venderlo.</summary>
    public void ProductoRegistrado(Producto producto)
    {
        Buscador?.Elegir(producto);
        Mensaje = $"Producto registrado: {producto.Nombre} - S/ {producto.Precio:0.00}. Ya está disponible en las tres PC.";
    }

    /// <summary>
    /// ViewModel del diálogo Nuevo cliente (HU-03, criterio 2). Propone el DNI
    /// o RUC digitado en el panel Cliente.
    /// </summary>
    public NuevoClienteViewModel? CrearNuevoCliente()
    {
        if (_clientes is null)
            return null;

        var vm = new NuevoClienteViewModel(_clientes);
        vm.ConexionPerdida += () => Conexion?.MarcarSinConexion();
        if (!string.IsNullOrWhiteSpace(Cliente?.Documento))
            vm.Proponer(Cliente.Documento);
        return vm;
    }

    /// <summary>Tras guardar en el diálogo: el cliente queda elegido en la venta en curso.</summary>
    public void ClienteRegistrado(Cliente cliente)
    {
        Cliente?.Seleccionar(cliente);
        Mensaje = $"Cliente registrado: {cliente.Nombre} ({cliente.Documento}).";
    }

    /// <summary>Últimos comprobantes para reimprimir (F5, HU-12); null si no hay ticketera ni comprobantes.</summary>
    public ComprobantesRecientesViewModel? CrearComprobantesRecientes() =>
        _comprobantes is null || Impresion is null ? null : new ComprobantesRecientesViewModel(_comprobantes, Impresion);

    /// <summary>Otra PC cambió el catálogo (aviso LISTEN/NOTIFY, T-13).</summary>
    public void AlCambiarCatalogo() => Buscador?.Refrescar();

    /// <summary>
    /// Comprobante guardado: la pantalla queda lista para la siguiente venta
    /// mientras sale el ticket (HU-12).
    /// </summary>
    private void AlEmitir(Comprobante comprobante)
    {
        Venta.Cancelar();
        Buscador?.Limpiar();
        Cliente?.Limpiar();
        Mensaje = $"{comprobante.NombreTipo} {comprobante.NumeroCompleto} emitida. Total S/ {comprobante.Total:0.00}.";
        UltimaImpresion = ImprimirAsync(comprobante);
    }

    private async Task ImprimirAsync(Comprobante comprobante)
    {
        if (Impresion is null)
            return;

        var emitida = Mensaje;
        Mensaje = $"{emitida} Imprimiendo…";
        Mensaje = await Impresion.ImprimirAsync(comprobante)
            ? $"{emitida} Impresa en {Impresion.Impresora}."
            : $"{emitida} El ticket no salió: pulse F11 para reimprimir.";
    }

    private void AlElegirProducto(Producto producto)
    {
        Venta.ProductoPorAgregar = producto;
        Mensaje = $"{producto.Nombre} - S/ {producto.Precio:0.00}. Escriba la cantidad y presione Enter.";
    }
}
