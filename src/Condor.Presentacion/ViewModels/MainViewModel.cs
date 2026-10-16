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
    private string _mensaje = "Listo.";
    private Sesion? _sesion;

    /// <param name="productos">Catálogo; si es null no se muestra el buscador.</param>
    public MainViewModel(IProductoRepositorio? productos = null)
    {
        ModuloPendienteCommand = new RelayCommand(
            p => Mensaje = $"El módulo {p} se implementará en un próximo sprint.");

        _productos = productos;
        if (productos is not null)
        {
            Buscador = new BuscadorProductoViewModel(productos);
            Buscador.ProductoElegido += AlElegirProducto;
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

    /// <summary>Campo Producto con autocompletado (HU-02).</summary>
    public BuscadorProductoViewModel? Buscador { get; }

    /// <summary>Atiende los botones F2 a F9 de módulos que aún no existen.</summary>
    public RelayCommand ModuloPendienteCommand { get; }

    public void IniciarSesion(Sesion sesion) => Sesion = sesion;

    /// <summary>
    /// ViewModel del diálogo Nuevo producto (HU-01). Si lo escrito en el campo
    /// Producto no encontró nada, se usa como nombre propuesto.
    /// </summary>
    public NuevoProductoViewModel? CrearNuevoProducto()
    {
        if (_productos is null)
            return null;

        var vm = new NuevoProductoViewModel(_productos);
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

    /// <summary>Otra PC cambió el catálogo (aviso LISTEN/NOTIFY, T-13).</summary>
    public void AlCambiarCatalogo() => Buscador?.Refrescar();

    private void AlElegirProducto(Producto producto) =>
        Mensaje = $"Producto elegido: {producto.Nombre} - S/ {producto.Precio:0.00}";
}
