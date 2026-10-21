using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Condor.Dominio.Entidades;
using Condor.Dominio.Ventas;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Detalle de la venta en curso y sus totales (HU-02, T-17). Vive solo en
/// memoria: si se cae la red, el detalle sigue en pantalla (T-06).
/// </summary>
public sealed class VentaViewModel : ObservableObject
{
    private Producto? _productoPorAgregar;
    private string _cantidadTexto = "1";
    private TotalesVenta _totales = TotalesVenta.Cero;
    private string? _error;

    public VentaViewModel()
    {
        Detalle.CollectionChanged += AlCambiarDetalle;
        AgregarCommand = new RelayCommand(_ => Agregar(), _ => ProductoPorAgregar is not null);
        QuitarCommand = new RelayCommand(p => Quitar(p as LineaVentaViewModel));
        CancelarCommand = new RelayCommand(Cancelar, () => Detalle.Count > 0);
    }

    /// <summary>Se dispara tras agregar una línea: la vista vuelve al campo Producto.</summary>
    public event Action<LineaVentaViewModel>? LineaAgregada;

    public ObservableCollection<LineaVentaViewModel> Detalle { get; } = new();

    /// <summary>Producto elegido en el buscador que espera su cantidad.</summary>
    public Producto? ProductoPorAgregar
    {
        get => _productoPorAgregar;
        set
        {
            if (SetProperty(ref _productoPorAgregar, value))
                AgregarCommand.NotificarCambio();
        }
    }

    public string CantidadTexto
    {
        get => _cantidadTexto;
        set
        {
            if (SetProperty(ref _cantidadTexto, value))
                Error = null;
        }
    }

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public decimal OpGravada => _totales.OpGravada;

    public decimal Igv => _totales.Igv;

    public decimal Total => _totales.Total;

    public RelayCommand AgregarCommand { get; }

    public RelayCommand QuitarCommand { get; }

    public RelayCommand CancelarCommand { get; }

    /// <summary>
    /// Agrega el producto elegido con la cantidad escrita. Si el producto ya
    /// está en el detalle, suma la cantidad a esa línea en lugar de repetirla.
    /// </summary>
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

        var linea = Detalle.FirstOrDefault(l => l.ProductoId == producto.Id && producto.Id != 0);
        if (linea is null)
        {
            linea = new LineaVentaViewModel(producto, cantidad);
            linea.ImporteCambiado += Recalcular;
            Detalle.Add(linea);
        }
        else
        {
            linea.Cantidad += cantidad;
        }

        ProductoPorAgregar = null;
        CantidadTexto = "1";
        LineaAgregada?.Invoke(linea);
        return true;
    }

    public void Quitar(LineaVentaViewModel? linea)
    {
        if (linea is null)
            return;
        linea.ImporteCambiado -= Recalcular;
        Detalle.Remove(linea);
    }

    /// <summary>Descarta la venta en curso (tecla Esc).</summary>
    public void Cancelar()
    {
        foreach (var linea in Detalle)
            linea.ImporteCambiado -= Recalcular;
        Detalle.Clear();
        ProductoPorAgregar = null;
        CantidadTexto = "1";
    }

    private void AlCambiarDetalle(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Renumera las líneas 1, 2, 3... tras agregar o quitar.
        for (var i = 0; i < Detalle.Count; i++)
            Detalle[i].Item = i + 1;
        CancelarCommand.NotificarCambio();
        Recalcular();
    }

    private void Recalcular()
    {
        _totales = CalculadoraVenta.Totales(Detalle.Select(l => l.Importe));
        OnPropertyChanged(nameof(OpGravada));
        OnPropertyChanged(nameof(Igv));
        OnPropertyChanged(nameof(Total));
    }
}
