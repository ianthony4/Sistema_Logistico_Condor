using System.Globalization;
using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Diálogo Nuevo producto (HU-01): nombre, precio con IGV y unidad.
/// </summary>
public sealed class NuevoProductoViewModel : ObservableObject
{
    private readonly IProductoRepositorio _productos;
    private string _nombre = string.Empty;
    private string _precioTexto = string.Empty;
    private string _unidad = "UND";
    private string? _error;

    public NuevoProductoViewModel(IProductoRepositorio productos)
    {
        _productos = productos;
        GuardarCommand = new AsyncRelayCommand(GuardarAsync, PuedeGuardar);
        CancelarCommand = new RelayCommand(() => Cerrar?.Invoke(false));
    }

    /// <summary>Lo escucha la ventana para cerrarse: true si se guardó.</summary>
    public event Action<bool>? Cerrar;

    public static IReadOnlyList<string> Unidades { get; } =
        new[] { "UND", "KG", "M", "GAL", "BLS", "VAR", "CJA", "ROL", "PZA", "LT" };

    public string Nombre
    {
        get => _nombre;
        set
        {
            if (SetProperty(ref _nombre, value))
                AlCambiarDatos();
        }
    }

    /// <summary>Texto tal como lo escribe el usuario: acepta 58.00 o 58,00.</summary>
    public string PrecioTexto
    {
        get => _precioTexto;
        set
        {
            if (SetProperty(ref _precioTexto, value))
                AlCambiarDatos();
        }
    }

    public string Unidad
    {
        get => _unidad;
        set => SetProperty(ref _unidad, value);
    }

    /// <summary>Mensaje en rojo bajo el formulario; null si no hay error.</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>Producto guardado, con su Id; null mientras no se guarde.</summary>
    public Producto? Guardado { get; private set; }

    public AsyncRelayCommand GuardarCommand { get; }

    public RelayCommand CancelarCommand { get; }

    public static bool IntentarLeerPrecio(string texto, out decimal precio) =>
        decimal.TryParse(texto.Trim().Replace(',', '.'), NumberStyles.Number,
            CultureInfo.InvariantCulture, out precio);

    private bool PuedeGuardar() => !string.IsNullOrWhiteSpace(Nombre) && !string.IsNullOrWhiteSpace(PrecioTexto);

    private void AlCambiarDatos()
    {
        Error = null;
        GuardarCommand.NotificarCambio();
    }

    private async Task GuardarAsync()
    {
        if (!IntentarLeerPrecio(PrecioTexto, out var precio))
        {
            Error = "El precio debe ser un número, por ejemplo 58.00.";
            return;
        }

        if (precio <= 0)
        {
            Error = "El precio debe ser mayor que cero.";
            return;
        }

        var producto = new Producto { Nombre = Nombre.Trim(), Precio = precio, Unidad = Unidad };
        try
        {
            Guardado = await _productos.RegistrarAsync(producto);
            Cerrar?.Invoke(true);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }
}
