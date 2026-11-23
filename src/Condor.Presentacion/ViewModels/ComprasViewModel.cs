using System.Collections.ObjectModel;
using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Ventas;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Ventana Compras (F4, HU-11, T-82): compras a los proveedores por rango de
/// fechas del comprobante y por proveedor, con el total de egresos del
/// período. Al elegir una compra se ve su detalle.
/// </summary>
public sealed class ComprasViewModel : ObservableObject
{
    public const int Cantidad = 300;

    private readonly ICompraRepositorio _compras;
    private readonly Func<DateOnly> _hoy;
    private DateTime? _desde;
    private DateTime? _hasta;
    private string _proveedor = string.Empty;
    private Compra? _seleccionada;
    private decimal _total;
    private int _encontradas;
    private string? _resumen;
    private string? _mensaje;

    /// <param name="hoy">Fecha de hoy en Perú; por defecto la del reloj de la PC.</param>
    public ComprasViewModel(ICompraRepositorio compras, Func<DateOnly>? hoy = null)
    {
        _compras = compras;
        _hoy = hoy ?? (() => EmisionComprobante.FechaPeru(DateTimeOffset.Now));

        // Al abrir: las compras del mes en curso.
        var dia = _hoy();
        _desde = new DateOnly(dia.Year, dia.Month, 1).ToDateTime(TimeOnly.MinValue);
        _hasta = dia.ToDateTime(TimeOnly.MinValue);

        BuscarCommand = new AsyncRelayCommand(BuscarAsync);
        MesCommand = new AsyncRelayCommand(() => PeriodoAsync(new DateOnly(_hoy().Year, _hoy().Month, 1), _hoy()));
        LimpiarCommand = new AsyncRelayCommand(LimpiarAsync);
    }

    /// <summary>No se pudo leer porque no responde la PC principal.</summary>
    public event Action? ConexionPerdida;

    public ObservableCollection<Compra> Compras { get; } = new();

    /// <summary>Líneas de la compra elegida.</summary>
    public ObservableCollection<DetalleCompra> Detalle { get; } = new();

    /// <summary>Primer día del período (fecha del comprobante del proveedor); null sin límite.</summary>
    public DateTime? Desde
    {
        get => _desde;
        set => SetProperty(ref _desde, value?.Date);
    }

    /// <summary>Último día del período, incluido; null sin límite.</summary>
    public DateTime? Hasta
    {
        get => _hasta;
        set => SetProperty(ref _hasta, value?.Date);
    }

    /// <summary>RUC o parte de la razón social o del nombre comercial del proveedor.</summary>
    public string Proveedor
    {
        get => _proveedor;
        set => SetProperty(ref _proveedor, value ?? string.Empty);
    }

    public Compra? Seleccionada
    {
        get => _seleccionada;
        set
        {
            if (SetProperty(ref _seleccionada, value))
                UltimoDetalle = CargarDetalleAsync(value);
        }
    }

    /// <summary>Última carga del detalle; las pruebas la esperan con await.</summary>
    public Task UltimoDetalle { get; private set; } = Task.CompletedTask;

    /// <summary>Cuántas compras hay en el filtro.</summary>
    public int Encontradas
    {
        get => _encontradas;
        private set => SetProperty(ref _encontradas, value);
    }

    /// <summary>Egresos del período: suma de todas las compras del filtro (HU-11, criterio 3).</summary>
    public decimal Total
    {
        get => _total;
        private set => SetProperty(ref _total, value);
    }

    /// <summary>"4 compras · Egresos del período: S/ 5386.00", sobre la lista.</summary>
    public string? Resumen
    {
        get => _resumen;
        private set => SetProperty(ref _resumen, value);
    }

    /// <summary>Resultado de la última acción, o el error.</summary>
    public string? Mensaje
    {
        get => _mensaje;
        private set => SetProperty(ref _mensaje, value);
    }

    public AsyncRelayCommand BuscarCommand { get; }

    /// <summary>Compras del mes en curso.</summary>
    public AsyncRelayCommand MesCommand { get; }

    /// <summary>Quita los filtros: todas las compras.</summary>
    public AsyncRelayCommand LimpiarCommand { get; }

    /// <summary>El filtro tal como se envía a la base central; null si las fechas están al revés.</summary>
    public FiltroCompras? Filtro()
    {
        var desde = Desde is { } d ? DateOnly.FromDateTime(d) : (DateOnly?)null;
        var hasta = Hasta is { } h ? DateOnly.FromDateTime(h) : (DateOnly?)null;
        return desde > hasta ? null : new FiltroCompras(desde, hasta, Proveedor, Cantidad);
    }

    public async Task BuscarAsync()
    {
        var filtro = Filtro();
        if (filtro is null)
        {
            Mensaje = "La fecha Desde es posterior a Hasta: corríjala y pulse Buscar.";
            return;
        }

        try
        {
            var resultado = await _compras.BuscarAsync(filtro);
            Compras.Clear();
            foreach (var c in resultado.Compras)
                Compras.Add(c);
            Encontradas = resultado.Cantidad;
            Total = resultado.Total;
            Resumen = resultado.Cantidad switch
            {
                0 => "No hay compras con esos filtros.",
                1 => $"1 compra · Egresos del período: S/ {resultado.Total:0.00}",
                var n => $"{n} compras · Egresos del período: S/ {resultado.Total:0.00}"
                         + (n > resultado.Compras.Count ? $" · se muestran las {resultado.Compras.Count} más recientes" : string.Empty),
            };
            Mensaje = null;
            Seleccionada = Compras.FirstOrDefault();
        }
        catch (SinConexionException)
        {
            Mensaje = "Sin conexión con la PC principal: no se pueden listar las compras.";
            ConexionPerdida?.Invoke();
        }
    }

    private async Task PeriodoAsync(DateOnly desde, DateOnly hasta)
    {
        Desde = desde.ToDateTime(TimeOnly.MinValue);
        Hasta = hasta.ToDateTime(TimeOnly.MinValue);
        await BuscarAsync();
    }

    private async Task LimpiarAsync()
    {
        Desde = null;
        Hasta = null;
        Proveedor = string.Empty;
        await BuscarAsync();
    }

    private async Task CargarDetalleAsync(Compra? compra)
    {
        Detalle.Clear();
        if (compra is null)
            return;

        try
        {
            var completa = compra.Detalle.Count > 0 ? compra : await _compras.ObtenerAsync(compra.Id);
            // Si mientras tanto se eligió otra, no se mezclan sus líneas.
            if (completa is null || !ReferenceEquals(Seleccionada, compra))
                return;
            foreach (var linea in completa.Detalle)
                Detalle.Add(linea);
        }
        catch (SinConexionException)
        {
            Mensaje = "Sin conexión con la PC principal: no se puede leer el detalle de la compra.";
            ConexionPerdida?.Invoke();
        }
    }
}
