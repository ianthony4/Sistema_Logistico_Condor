using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Ventas;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// ViewModel de la ventana principal: la pantalla Nueva venta y, desde el
/// Sprint 5, los módulos del menú (F2 a F9) en el panel derecho (T-73).
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly IProductoRepositorio? _productos;
    private readonly IClienteRepositorio? _clientes;
    private readonly IComprobanteRepositorio? _comprobantes;
    private readonly IProveedorRepositorio? _proveedores;
    private ImpresionViewModel? _impresion;
    private EnvioSunatViewModel? _envioSunat;
    private ReenvioSunatViewModel? _reenvioSunat;
    private IGeneradorRespaldo? _generadorRespaldo;
    private IRespaldoRepositorio? _respaldos;
    private string? _carpetaRespaldo;
    private AvisoRespaldoViewModel? _avisoRespaldo;
    private string _mensaje = "Listo.";
    private Sesion? _sesion;
    private EstadoConexionViewModel? _conexion;
    private Modulo _moduloActual = Modulo.Venta;
    private ObservableObject? _contenidoModulo;

    /// <param name="productos">Catálogo; si es null no se muestra el buscador.</param>
    /// <param name="clientes">Clientes; si es null no se muestra el panel Cliente (HU-03).</param>
    /// <param name="comprobantes">Comprobantes; si es null el botón Emitir no está disponible (HU-05).</param>
    /// <param name="proveedores">Proveedores; si es null el módulo Proveedores no está disponible (HU-04).</param>
    public MainViewModel(IProductoRepositorio? productos = null, IClienteRepositorio? clientes = null,
        IComprobanteRepositorio? comprobantes = null, IProveedorRepositorio? proveedores = null)
    {
        _proveedores = proveedores;

        ModuloPendienteCommand = new RelayCommand(
            p => Mensaje = $"El módulo {p} se implementará en un próximo sprint.");
        IrACommand = new RelayCommand(p => IrA(p is Modulo m ? m : Enum.Parse<Modulo>(p?.ToString() ?? nameof(Modulo.Venta))));

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

    /// <summary>Envío a la SUNAT al emitir (HU-07). Lo asigna el arranque.</summary>
    public EnvioSunatViewModel? EnvioSunat
    {
        get => _envioSunat;
        set
        {
            if (SetProperty(ref _envioSunat, value) && value is not null)
                value.ConexionPerdida += () => Conexion?.MarcarSinConexion();
        }
    }

    /// <summary>Reenvío automático de los pendientes y su aviso en el pie (HU-17). Lo asigna el arranque.</summary>
    public ReenvioSunatViewModel? ReenvioSunat
    {
        get => _reenvioSunat;
        set
        {
            if (!SetProperty(ref _reenvioSunat, value) || value is null)
                return;
            value.ConexionPerdida += () => Conexion?.MarcarSinConexion();
            value.Reenviado += resumen => Mensaje = resumen;
        }
    }

    /// <summary>Última impresión lanzada al emitir; las pruebas la esperan con await.</summary>
    public Task UltimaImpresion { get; private set; } = Task.CompletedTask;

    /// <summary>Último envío a la SUNAT lanzado al emitir; las pruebas lo esperan con await.</summary>
    public Task UltimoEnvioSunat { get; private set; } = Task.CompletedTask;

    /// <summary>Atiende los botones F2 a F9 de módulos que aún no existen.</summary>
    public RelayCommand ModuloPendienteCommand { get; }

    /// <summary>Módulo que se ve en el panel derecho; Venta es la pantalla Nueva venta.</summary>
    public Modulo ModuloActual
    {
        get => _moduloActual;
        private set
        {
            if (SetProperty(ref _moduloActual, value))
                OnPropertyChanged(nameof(EnVenta));
        }
    }

    public bool EnVenta => ModuloActual == Modulo.Venta;

    /// <summary>ViewModel del módulo abierto (la vista lo muestra con su plantilla); null en Nueva venta.</summary>
    public ObservableObject? ContenidoModulo
    {
        get => _contenidoModulo;
        private set => SetProperty(ref _contenidoModulo, value);
    }

    /// <summary>Botones del menú: el parámetro es el nombre del módulo (Historial, Proveedores...).</summary>
    public RelayCommand IrACommand { get; }

    /// <summary>Se está generando un respaldo: no se cambia de módulo ni se cierra la ventana.</summary>
    public bool RespaldoEnCurso => ContenidoModulo is RespaldoViewModel { Generando: true };

    /// <summary>Se cambió de módulo: la vista pone el cursor donde corresponde.</summary>
    public event Action? ModuloCambiado;

    /// <summary>
    /// Muestra el módulo en el panel derecho, en lugar de abrir otra ventana
    /// (T-73). Cada vez se crea de nuevo, así se ven los datos al día. Devuelve
    /// false si el módulo no está disponible en esta PC o si un respaldo en
    /// curso no deja salir.
    /// </summary>
    public bool IrA(Modulo modulo)
    {
        if (RespaldoEnCurso && modulo != Modulo.Respaldo)
        {
            Mensaje = "Espere a que termine el respaldo antes de cambiar de módulo: no desconecte el disco.";
            return false;
        }

        ObservableObject? contenido = null;
        if (modulo != Modulo.Venta)
        {
            contenido = CrearModulo(modulo);
            if (contenido is null)
            {
                ModuloPendienteCommand.Execute(modulo.Nombre());
                return false;
            }
        }

        ContenidoModulo = contenido;
        ModuloActual = modulo;
        // Al volver a la venta se refresca el buscador: pudo cambiar el catálogo.
        if (modulo == Modulo.Venta)
            Buscador?.Refrescar();
        ModuloCambiado?.Invoke();
        return true;
    }

    /// <summary>ViewModel de cada módulo; null si esta PC no lo tiene (o aún no existe).</summary>
    private ObservableObject? CrearModulo(Modulo modulo) => modulo switch
    {
        Modulo.Historial => CrearComprobantesRecientes(),
        Modulo.Proveedores => CrearProveedores(),
        Modulo.Respaldo => CrearRespaldo(),
        Modulo.Proformas => CrearProformas(),
        _ => null,
    };

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

    /// <summary>Ventana Proveedores (F6, HU-04, T-39); null si no hay repositorio.</summary>
    public ProveedoresViewModel? CrearProveedores()
    {
        if (_proveedores is null)
            return null;

        var vm = new ProveedoresViewModel(_proveedores);
        vm.ConexionPerdida += () => Conexion?.MarcarSinConexion();
        return vm;
    }

    /// <summary>
    /// En el módulo Proformas se eligió una proforma para venderla (HU-09): la
    /// vista confirma si reemplaza la venta en curso y la convierte.
    /// </summary>
    public event Action<Comprobante>? ProformaPorConvertir;

    /// <summary>Módulo Proformas (F7, HU-09, T-73); null si no hay repositorio de comprobantes.</summary>
    public ProformasViewModel? CrearProformas()
    {
        if (_comprobantes is null)
            return null;

        var vm = new ProformasViewModel(_comprobantes, Impresion);
        vm.ConexionPerdida += () => Conexion?.MarcarSinConexion();
        vm.ProformaElegida += p => ProformaPorConvertir?.Invoke(p);
        return vm;
    }

    /// <summary>
    /// Convierte la proforma en venta (HU-09, criterio 2): su detalle y su
    /// cliente pasan a la pantalla tal como se cotizaron, y se propone el
    /// comprobante según el documento del cliente (RUC: factura; DNI: boleta
    /// con DNI). La venta en curso, si había, se reemplaza.
    /// </summary>
    public void ConvertirProforma(Comprobante proforma)
    {
        if (Emision is null)
            return;

        Buscador?.Limpiar();
        Cliente?.Limpiar();
        Venta.Cargar(proforma.Detalle);
        if (proforma.Cliente is { } cliente)
            Cliente?.Seleccionar(cliente);
        Emision.Tipo = proforma.Cliente?.TipoDocumento switch
        {
            TipoDocumento.Ruc => TipoEmision.Factura,
            TipoDocumento.Dni => TipoEmision.BoletaConDni,
            _ => TipoEmision.Boleta,
        };
        Emision.ProformaOrigen = proforma;
        Mensaje = $"Proforma {proforma.NumeroCompleto} cargada: {Venta.Detalle.Count} productos, total S/ {Venta.Total:0.00}. Revise y pulse Emitir (F10).";
    }

    /// <summary>
    /// Últimos comprobantes para reimprimir (F5, HU-12) y reenviar los
    /// pendientes a la SUNAT (HU-17); null si no hay ticketera ni comprobantes.
    /// </summary>
    public ComprobantesRecientesViewModel? CrearComprobantesRecientes() =>
        _comprobantes is null || Impresion is null ? null : new ComprobantesRecientesViewModel(_comprobantes, Impresion, ReenvioSunat);

    /// <summary>
    /// Respaldo manual (F9, HU-15). Lo llama el arranque: el generador solo
    /// existe en la PC principal, que tiene el disco externo. En todas las PC
    /// se avisa si el último respaldo tiene más de <paramref name="avisoHoras"/> horas.
    /// </summary>
    public void ConfigurarRespaldo(IGeneradorRespaldo? generador, IRespaldoRepositorio registro, string? carpeta,
        int avisoHoras = 24, Func<DateTimeOffset>? ahora = null)
    {
        _generadorRespaldo = generador;
        _respaldos = registro;
        _carpetaRespaldo = carpeta;
        AvisoRespaldo = new AvisoRespaldoViewModel(registro, avisoHoras, ahora);
    }

    /// <summary>Aviso de respaldo vencido al abrir el sistema (HU-15, criterio 2).</summary>
    public AvisoRespaldoViewModel? AvisoRespaldo
    {
        get => _avisoRespaldo;
        private set => SetProperty(ref _avisoRespaldo, value);
    }

    /// <summary>Ventana Respaldo (F9, T-62); null si el arranque no configuró los respaldos.</summary>
    public RespaldoViewModel? CrearRespaldo()
    {
        if (_respaldos is null)
            return null;

        var vm = new RespaldoViewModel(_generadorRespaldo, _respaldos, () => Sesion, _carpetaRespaldo);
        vm.ConexionPerdida += () => Conexion?.MarcarSinConexion();
        vm.RespaldoGenerado += r =>
        {
            Mensaje = $"Respaldo generado: {r.Archivo} ({r.Tamano}) en {r.Carpeta}.";
            AvisoRespaldo?.RespaldoGenerado(r);
        };
        return vm;
    }

    /// <summary>Otra PC cambió el catálogo (aviso LISTEN/NOTIFY, T-13).</summary>
    public void AlCambiarCatalogo() => Buscador?.Refrescar();

    /// <summary>
    /// Comprobante guardado: la pantalla queda lista para la siguiente venta
    /// mientras sale el ticket (HU-12) y se envía a la SUNAT (HU-07). Las dos
    /// cosas van en paralelo: sin internet el ticket sale igual. La nota de
    /// venta es interna: se imprime pero no se envía (HU-08).
    /// </summary>
    private void AlEmitir(Comprobante comprobante)
    {
        Venta.Cancelar();
        Buscador?.Limpiar();
        Cliente?.Limpiar();
        Mensaje = $"{comprobante.NombreTipo} {comprobante.NumeroCompleto} emitida. Total S/ {comprobante.Total:0.00}.";
        if (comprobante.ProformaId is not null)
            Mensaje = $"{comprobante.NombreTipo} {comprobante.NumeroCompleto} emitida desde la proforma. Total S/ {comprobante.Total:0.00}.";
        UltimaImpresion = ImprimirAsync(comprobante);
        if (EnvioSunat is not null && comprobante.EsElectronico)
            UltimoEnvioSunat = EnviarSunatAsync(comprobante);
    }

    /// <summary>Envía al emitir (HU-07) y vuelve a contar los pendientes del pie (HU-17).</summary>
    private async Task EnviarSunatAsync(Comprobante comprobante)
    {
        await EnvioSunat!.EnviarAsync(comprobante);
        if (ReenvioSunat is not null)
            await ReenvioSunat.ActualizarAsync();
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
