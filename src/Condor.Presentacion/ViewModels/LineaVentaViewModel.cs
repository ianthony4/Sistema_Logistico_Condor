using Condor.Dominio.Entidades;
using Condor.Dominio.Ventas;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Línea del detalle de la venta. La descripción y el precio se copian del
/// producto al agregarlo; la cantidad se puede corregir en la tabla.
/// </summary>
public sealed class LineaVentaViewModel : ObservableObject
{
    private int _item;
    private decimal _cantidad;

    public LineaVentaViewModel(Producto producto, decimal cantidad)
    {
        ProductoId = producto.Id;
        Descripcion = producto.Nombre;
        Unidad = producto.Unidad;
        PrecioUnitario = producto.Precio;
        Cantidad = cantidad;
    }

    /// <summary>Se dispara cuando cambia el importe, para recalcular los totales.</summary>
    public event Action? ImporteCambiado;

    public int Item
    {
        get => _item;
        set => SetProperty(ref _item, value);
    }

    public long ProductoId { get; }

    public string Descripcion { get; }

    public string Unidad { get; }

    public decimal PrecioUnitario { get; }

    public decimal Cantidad
    {
        get => _cantidad;
        set
        {
            // Una cantidad no positiva se ignora: la tabla vuelve al valor anterior.
            if (value <= 0 || !SetProperty(ref _cantidad, value))
                return;
            OnPropertyChanged(nameof(Importe));
            ImporteCambiado?.Invoke();
        }
    }

    public decimal Importe => CalculadoraVenta.Importe(Cantidad, PrecioUnitario);

    public DetalleComprobante ADetalle() => new()
    {
        Item = Item,
        ProductoId = ProductoId,
        Descripcion = Descripcion,
        Cantidad = Cantidad,
        PrecioUnitario = PrecioUnitario,
        Importe = Importe,
    };
}
