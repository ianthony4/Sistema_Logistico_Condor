using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;
using Condor.Dominio.Ventas;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>Línea del diálogo Nueva compra: producto del catálogo, cantidad y costo con IGV.</summary>
public sealed class LineaCompraViewModel : ObservableObject
{
    private int _item;

    public LineaCompraViewModel(Producto producto, decimal cantidad, decimal costo)
    {
        ProductoId = producto.Id;
        Descripcion = producto.Nombre;
        Unidad = producto.Unidad;
        Cantidad = cantidad;
        CostoUnitario = costo;
    }

    public int Item
    {
        get => _item;
        set => SetProperty(ref _item, value);
    }

    public long ProductoId { get; }

    public string Descripcion { get; }

    public string Unidad { get; }

    public decimal Cantidad { get; }

    public decimal CostoUnitario { get; }

    public decimal Importe => CalculadoraVenta.Importe(Cantidad, CostoUnitario);

    public DetalleCompra ADetalle() => new()
    {
        ProductoId = ProductoId,
        Descripcion = Descripcion,
        Unidad = Unidad,
        Cantidad = Cantidad,
        CostoUnitario = CostoUnitario,
    };
}

/// <summary>
/// Diálogo Nueva compra (HU-11, T-81): el proveedor (por su RUC), el
/// comprobante que entregó (factura o boleta, serie, número y fecha) y los
/// productos con lo que costaron. Al guardar, la compra queda registrada en la
/// base central y aparece en Compras (F4) de las tres PC.
/// </summary>
public sealed class NuevaCompraViewModel : ObservableObject
{
    private readonly ICompraRepositorio _compras;
    private readonly IProveedorRepositorio _proveedores;
    private readonly Func<Sesion?> _sesion;
    private readonly Func<DateOnly> _hoy;
    private string _documentoProveedor = string.Empty;
    private Proveedor? _proveedor;
    private string _tipoDocumento = TipoDocumentoCompra.Factura;
    private string _serie = string.Empty;
    private string _numeroTexto = string.Empty;
    private DateTime? _fecha;
    private string _observacion = string.Empty;
    private Producto? _productoPorAgregar;
    private string _cantidadTexto = "1";
    private string _costoTexto = string.Empty;
    private TotalesVenta _totales = TotalesVenta.Cero;
    private string? _error;

    /// <param name="sesion">Sesión de la PC: da la terminal que registra (HU-14).</param>
    /// <param name="hoy">Fecha de hoy en Perú; por defecto la del reloj de la PC.</param>
    public NuevaCompraViewModel(ICompraRepositorio compras, IProveedorRepositorio proveedores, IProductoRepositorio productos,
        Func<Sesion?> sesion, Func<DateOnly>? hoy = null)
    {
        _compras = compras;
        _proveedores = proveedores;
        _sesion = sesion;
        _hoy = hoy ?? (() => EmisionComprobante.FechaPeru(DateTimeOffset.Now));
        _fecha = _hoy().ToDateTime(TimeOnly.MinValue);

        Buscador = new BuscadorProductoViewModel(productos);
        Buscador.ProductoElegido += p => ProductoPorAgregar = p;
        Buscador.ConexionPerdida += () => ConexionPerdida?.Invoke();

        Lineas.CollectionChanged += AlCambiarLineas;
        BuscarProveedorCommand = new AsyncRelayCommand(BuscarProveedorAsync, () => !string.IsNullOrWhiteSpace(DocumentoProveedor));
        AgregarCommand = new RelayCommand(_ => Agregar(), _ => ProductoPorAgregar is not null);
        QuitarCommand = new RelayCommand(p => Lineas.Remove((p as LineaCompraViewModel)!));
        GuardarCommand = new AsyncRelayCommand(GuardarAsync, () => Lineas.Count > 0);
        CancelarCommand = new RelayCommand(() => Cerrar?.Invoke(false));
    }

    /// <summary>Lo escucha la ventana para cerrarse: true si se guardó.</summary>
    public event Action<bool>? Cerrar;

    /// <summary>Guardar o buscar falló porque no responde la PC principal.</summary>
    public event Action? ConexionPerdida;

    /// <summary>Se agregó una línea: la vista vuelve al campo Producto.</summary>
    public event Action? LineaAgregada;

    /// <summary>RUC del proveedor (o parte de su razón social) tal como se digita.</summary>
    public string DocumentoProveedor
    {
        get => _documentoProveedor;
        set
        {
            if (!SetProperty(ref _documentoProveedor, value ?? string.Empty))
                return;
            BuscarProveedorCommand.NotificarCambio();
            if (Proveedor is not null && Proveedor.Ruc != ValidadorDocumento.Limpiar(_documentoProveedor))
                Proveedor = null;
        }
    }

    /// <summary>Proveedor encontrado; null mientras no se busque.</summary>
    public Proveedor? Proveedor
    {
        get => _proveedor;
        private set
        {
            if (SetProperty(ref _proveedor, value))
                Error = null;
        }
    }

    public bool EsFactura
    {
        get => _tipoDocumento == TipoDocumentoCompra.Factura;
        set
        {
            if (value)
                CambiarTipo(TipoDocumentoCompra.Factura);
        }
    }

    public bool EsBoleta
    {
        get => _tipoDocumento == TipoDocumentoCompra.Boleta;
        set
        {
            if (value)
                CambiarTipo(TipoDocumentoCompra.Boleta);
        }
    }

    /// <summary>Serie del comprobante del proveedor (F001, E001...).</summary>
    public string Serie
    {
        get => _serie;
        set => SetProperty(ref _serie, value ?? string.Empty);
    }

    public string NumeroTexto
    {
        get => _numeroTexto;
        set => SetProperty(ref _numeroTexto, value ?? string.Empty);
    }

    /// <summary>Fecha que figura en el comprobante del proveedor; por defecto hoy.</summary>
    public DateTime? Fecha
    {
        get => _fecha;
        set => SetProperty(ref _fecha, value?.Date);
    }

    public string Observacion
    {
        get => _observacion;
        set => SetProperty(ref _observacion, value ?? string.Empty);
    }

    /// <summary>Campo Producto con autocompletado, el mismo de la venta (HU-02).</summary>
    public BuscadorProductoViewModel Buscador { get; }

    /// <summary>Producto elegido que espera su cantidad y su costo.</summary>
    public Producto? ProductoPorAgregar
    {
        get => _productoPorAgregar;
        private set
        {
            if (SetProperty(ref _productoPorAgregar, value))
                AgregarCommand.NotificarCambio();
        }
    }

    public string CantidadTexto
    {
        get => _cantidadTexto;
        set => SetProperty(ref _cantidadTexto, value ?? string.Empty);
    }

    /// <summary>Costo por unidad con IGV, como figura en el comprobante del proveedor.</summary>
    public string CostoTexto
    {
        get => _costoTexto;
        set => SetProperty(ref _costoTexto, value ?? string.Empty);
    }

    public ObservableCollection<LineaCompraViewModel> Lineas { get; } = new();

    public decimal OpGravada => _totales.OpGravada;

    public decimal Igv => _totales.Igv;

    public decimal Total => _totales.Total;

    /// <summary>Mensaje en rojo bajo el formulario; null si no hay error.</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>Compra guardada, con su Id; null mientras no se guarde.</summary>
    public Compra? Guardado { get; private set; }

    public AsyncRelayCommand BuscarProveedorCommand { get; }

    public RelayCommand AgregarCommand { get; }

    public RelayCommand QuitarCommand { get; }

    public AsyncRelayCommand GuardarCommand { get; }

    public RelayCommand CancelarCommand { get; }

    /// <summary>Agrega el producto elegido con la cantidad y el costo escritos.</summary>
    public bool Agregar()
    {
        var producto = ProductoPorAgregar;
        if (producto is null)
            return false;
        if (!NuevoProductoViewModel.IntentarLeerPrecio(CantidadTexto, out var cantidad) || cantidad <= 0)
        {
            Error = "La cantidad debe ser un número mayor que cero.";
            return false;
        }
        if (!NuevoProductoViewModel.IntentarLeerPrecio(CostoTexto, out var costo) || costo <= 0)
        {
            Error = $"Escriba el costo unitario de {producto.Nombre} (con IGV, como en el comprobante del proveedor).";
            return false;
        }

        Lineas.Add(new LineaCompraViewModel(producto, cantidad, costo));
        Error = null;
        ProductoPorAgregar = null;
        Buscador.Limpiar();
        CantidadTexto = "1";
        CostoTexto = string.Empty;
        LineaAgregada?.Invoke();
        return true;
    }

    private void CambiarTipo(string tipo)
    {
        if (_tipoDocumento == tipo)
            return;
        _tipoDocumento = tipo;
        OnPropertyChanged(nameof(EsFactura));
        OnPropertyChanged(nameof(EsBoleta));
    }

    private void AlCambiarLineas(object? sender, NotifyCollectionChangedEventArgs e)
    {
        for (var i = 0; i < Lineas.Count; i++)
            Lineas[i].Item = i + 1;
        _totales = CalculadoraVenta.Totales(Lineas.Select(l => l.Importe));
        OnPropertyChanged(nameof(OpGravada));
        OnPropertyChanged(nameof(Igv));
        OnPropertyChanged(nameof(Total));
        GuardarCommand.NotificarCambio();
    }

    private async Task BuscarProveedorAsync()
    {
        var texto = DocumentoProveedor.Trim();
        Proveedor = null;
        try
        {
            var limpio = ValidadorDocumento.Limpiar(texto);
            if (limpio.Length == ValidadorDocumento.LargoRuc && limpio.All(char.IsAsciiDigit))
            {
                Proveedor = await _proveedores.BuscarPorRucAsync(limpio);
                if (Proveedor is null)
                    Error = $"No hay un proveedor con el RUC {limpio}: regístrelo en Proveedores (F6).";
                return;
            }

            var encontrados = await _proveedores.BuscarAsync(texto, limite: 10);
            switch (encontrados.Count)
            {
                case 1:
                    Proveedor = encontrados[0];
                    break;
                case 0:
                    Error = $"No hay proveedores con «{texto}». Regístrelo en Proveedores (F6).";
                    break;
                default:
                    Error = $"Hay {encontrados.Count} proveedores con «{texto}»: escriba el RUC del que entregó el comprobante.";
                    break;
            }
        }
        catch (SinConexionException)
        {
            Error = "Sin conexión con la PC principal: no se puede buscar el proveedor.";
            ConexionPerdida?.Invoke();
        }
        finally
        {
            if (Proveedor is not null)
            {
                // Se muestra el RUC aunque se haya buscado por la razón social.
                _documentoProveedor = Proveedor.Ruc;
                OnPropertyChanged(nameof(DocumentoProveedor));
            }
        }
    }

    private async Task GuardarAsync()
    {
        var hoy = _hoy();
        var compra = new Compra
        {
            ProveedorId = Proveedor?.Id ?? 0,
            Proveedor = Proveedor,
            TipoDocumento = _tipoDocumento,
            Serie = Serie,
            Numero = long.TryParse(NumeroTexto.Trim(), out var numero) ? numero : 0,
            FechaEmision = Fecha is { } f ? DateOnly.FromDateTime(f) : hoy,
            Observacion = Observacion,
        };
        foreach (var linea in Lineas)
            compra.Detalle.Add(linea.ADetalle());

        var errores = ValidadorCompra.Errores(compra, hoy);
        if (errores.Count > 0)
        {
            Error = string.Join(" ", errores.Select(e => e.Mensaje));
            return;
        }

        var sesion = _sesion();
        if (sesion is not { Registrada: true })
        {
            Error = "Esta PC aún no se registró en la base de datos central. Espere a que vuelva la conexión y pulse Guardar otra vez.";
            return;
        }
        compra.TerminalId = sesion.Terminal.Id;
        compra.TerminalNombre = sesion.Terminal.Nombre;

        try
        {
            Guardado = await _compras.RegistrarAsync(ValidadorCompra.Preparar(compra, hoy));
            Cerrar?.Invoke(true);
        }
        catch (SinConexionException)
        {
            // T-06: el diálogo sigue abierto con lo escrito para reintentar.
            Error = "Sin conexión con la PC principal. La compra NO se guardó y sus datos se conservan: pulse Guardar para reintentar.";
            ConexionPerdida?.Invoke();
        }
        catch (Exception ex)
        {
            // CompraDuplicadaException trae el mensaje para el usuario (HU-11, criterio 2).
            Error = ex.Message;
        }
    }
}
