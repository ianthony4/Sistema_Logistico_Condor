using System.Collections.ObjectModel;
using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Dominio.Ventas;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>Opción del filtro Tipo del Historial; Codigo null son todas las ventas.</summary>
public sealed record OpcionTipoComprobante(string Nombre, string? Codigo)
{
    public override string ToString() => Nombre;
}

/// <summary>
/// Historial de ventas (F5, HU-18): comprobantes por rango de fechas, tipo y
/// cliente o número, con el total del período, para atender reclamos y
/// reimprimir el que no salió (HU-12, criterio 2; T-33). Muestra el estado de
/// cada comprobante ante la SUNAT (HU-07, T-48) y permite reenviar en el acto
/// los pendientes (HU-17, T-58). Los filtros llegaron en el Sprint 5 (T-76).
/// </summary>
public sealed class ComprobantesRecientesViewModel : ObservableObject
{
    /// <summary>Comprobantes que se muestran como máximo; el total del período es de todos.</summary>
    public const int Cantidad = 200;

    private readonly IComprobanteRepositorio _comprobantes;
    private readonly ImpresionViewModel _impresion;
    private readonly ReenvioSunatViewModel? _reenvio;
    private readonly Func<DateOnly> _hoy;
    private Comprobante? _seleccionado;
    private string? _mensaje;
    private DateTime? _desde;
    private DateTime? _hasta;
    private OpcionTipoComprobante _tipo;
    private string _texto = string.Empty;
    private string? _resumen;
    private string? _errorFiltro;
    private decimal _total;
    private int _encontrados;

    /// <param name="reenvio">Reenvío a la SUNAT de esta PC; null o inactivo si esta PC no reenvía.</param>
    /// <param name="hoy">Fecha de hoy en Perú; por defecto la del reloj de la PC.</param>
    public ComprobantesRecientesViewModel(IComprobanteRepositorio comprobantes, ImpresionViewModel impresion,
        ReenvioSunatViewModel? reenvio = null, Func<DateOnly>? hoy = null)
    {
        _comprobantes = comprobantes;
        _impresion = impresion;
        _reenvio = reenvio;
        _hoy = hoy ?? (() => EmisionComprobante.FechaPeru(DateTimeOffset.Now));
        _tipo = Tipos[0];

        // Al abrir se ven las ventas de hoy: el cuadre de caja del día.
        var dia = _hoy().ToDateTime(TimeOnly.MinValue);
        _desde = dia;
        _hasta = dia;

        ActualizarCommand = new AsyncRelayCommand(CargarAsync);
        HoyCommand = new AsyncRelayCommand(() => PeriodoAsync(_hoy(), _hoy()));
        MesCommand = new AsyncRelayCommand(() => PeriodoAsync(new DateOnly(_hoy().Year, _hoy().Month, 1), _hoy()));
        LimpiarCommand = new AsyncRelayCommand(LimpiarAsync);
        ReimprimirCommand = new AsyncRelayCommand(ReimprimirAsync, () => Seleccionado is not null);
        ReenviarCommand = new AsyncRelayCommand(ReenviarAsync, () => PuedeReenviar && PendientesSunat > 0);
    }

    /// <summary>Esta PC tiene la SUNAT configurada y puede reenviar los pendientes (HU-17).</summary>
    public bool PuedeReenviar => _reenvio is { Activo: true };

    public ObservableCollection<Comprobante> Comprobantes { get; } = new();

    /// <summary>Opciones del filtro Tipo: todas las ventas o un tipo; las proformas aparte porque no son ventas.</summary>
    public static IReadOnlyList<OpcionTipoComprobante> Tipos { get; } = new OpcionTipoComprobante[]
    {
        new("Todas las ventas", null),
        new("Boletas", TipoComprobante.Boleta),
        new("Facturas", TipoComprobante.Factura),
        new("Notas de venta", TipoComprobante.NotaVenta),
        new("Proformas", TipoComprobante.Proforma),
    };

    /// <summary>Las mismas opciones, para el ComboBox (WPF no enlaza propiedades estáticas por instancia).</summary>
    public IReadOnlyList<OpcionTipoComprobante> OpcionesTipo => Tipos;

    /// <summary>Primer día del período (fecha de emisión); null sin límite.</summary>
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

    public OpcionTipoComprobante Tipo
    {
        get => _tipo;
        set => SetProperty(ref _tipo, value ?? Tipos[0]);
    }

    /// <summary>Número (B001-12 o 12), DNI o RUC del cliente, o parte de su nombre.</summary>
    public string Texto
    {
        get => _texto;
        set => SetProperty(ref _texto, value ?? string.Empty);
    }

    /// <summary>Cuántos comprobantes hay en el filtro.</summary>
    public int Encontrados
    {
        get => _encontrados;
        private set => SetProperty(ref _encontrados, value);
    }

    /// <summary>Ventas del período: sin proformas ni comprobantes rechazados por la SUNAT.</summary>
    public decimal Total
    {
        get => _total;
        private set => SetProperty(ref _total, value);
    }

    /// <summary>"12 comprobantes · Ventas del período: S/ 1520.00", sobre la lista.</summary>
    public string? Resumen
    {
        get => _resumen;
        private set => SetProperty(ref _resumen, value);
    }

    /// <summary>Por qué no se pudo buscar (fechas al revés); null si el filtro está bien.</summary>
    public string? ErrorFiltro
    {
        get => _errorFiltro;
        private set => SetProperty(ref _errorFiltro, value);
    }

    /// <summary>Boletas y facturas de la lista que aún no tienen respuesta de la SUNAT.</summary>
    public int PendientesSunat => Comprobantes.Count(c => EsElectronico(c) && c.EstadoSunat == EstadoSunat.Pendiente);

    /// <summary>Comprobantes de la lista rechazados por la SUNAT: hay que corregirlos.</summary>
    public int RechazadosSunat => Comprobantes.Count(c => c.EstadoSunat == EstadoSunat.Rechazado);

    public Comprobante? Seleccionado
    {
        get => _seleccionado;
        set
        {
            if (SetProperty(ref _seleccionado, value))
                ReimprimirCommand.NotificarCambio();
        }
    }

    /// <summary>Resultado de la última acción, o el error.</summary>
    public string? Mensaje
    {
        get => _mensaje;
        private set => SetProperty(ref _mensaje, value);
    }

    /// <summary>Buscar con los filtros (Enter o F5).</summary>
    public AsyncRelayCommand ActualizarCommand { get; }

    /// <summary>Ventas de hoy.</summary>
    public AsyncRelayCommand HoyCommand { get; }

    /// <summary>Ventas del mes en curso.</summary>
    public AsyncRelayCommand MesCommand { get; }

    /// <summary>Quita los filtros: todo el historial, del más reciente al más antiguo.</summary>
    public AsyncRelayCommand LimpiarCommand { get; }

    public AsyncRelayCommand ReimprimirCommand { get; }

    /// <summary>Reenviar a la SUNAT los pendientes, sin esperar al reenvío automático (HU-17).</summary>
    public AsyncRelayCommand ReenviarCommand { get; }

    /// <summary>El filtro tal como se envía a la base central; null si las fechas están al revés.</summary>
    public FiltroComprobantes? Filtro()
    {
        var desde = Desde is { } d ? DateOnly.FromDateTime(d) : (DateOnly?)null;
        var hasta = Hasta is { } h ? DateOnly.FromDateTime(h) : (DateOnly?)null;
        if (desde > hasta)
            return null;
        return new FiltroComprobantes(desde, hasta, Tipo.Codigo, Texto, Cantidad);
    }

    public async Task CargarAsync()
    {
        var filtro = Filtro();
        if (filtro is null)
        {
            ErrorFiltro = "La fecha Desde es posterior a Hasta: corríjala y pulse Buscar.";
            return;
        }
        ErrorFiltro = null;

        try
        {
            var resultado = await _comprobantes.BuscarAsync(filtro);
            Comprobantes.Clear();
            foreach (var c in resultado.Comprobantes)
                Comprobantes.Add(c);
            Encontrados = resultado.Cantidad;
            Total = resultado.Total;
            Resumen = ResumenPeriodo(resultado);
            // Primero el más reciente que quedó sin imprimir.
            Seleccionado = Comprobantes.FirstOrDefault(c => c.Impresiones == 0) ?? Comprobantes.FirstOrDefault();
            OnPropertyChanged(nameof(PendientesSunat));
            OnPropertyChanged(nameof(RechazadosSunat));
            ReenviarCommand.NotificarCambio();
            Mensaje = Comprobantes.Count == 0 ? null : ResumenSunat();
        }
        catch (SinConexionException)
        {
            Mensaje = "Sin conexión con la PC principal: no se pueden listar los comprobantes.";
        }
    }

    private async Task PeriodoAsync(DateOnly desde, DateOnly hasta)
    {
        Desde = desde.ToDateTime(TimeOnly.MinValue);
        Hasta = hasta.ToDateTime(TimeOnly.MinValue);
        await CargarAsync();
    }

    private async Task LimpiarAsync()
    {
        Desde = null;
        Hasta = null;
        Tipo = Tipos[0];
        Texto = string.Empty;
        await CargarAsync();
    }

    private string ResumenPeriodo(ResultadoComprobantes resultado)
    {
        var cantidad = resultado.Cantidad == 1 ? "1 comprobante" : $"{resultado.Cantidad} comprobantes";
        if (resultado.Cantidad == 0)
            return "No hay comprobantes con esos filtros.";
        var partes = new List<string> { cantidad };
        if (Tipo.Codigo != TipoComprobante.Proforma)
            partes.Add($"Ventas del período: S/ {resultado.Total:0.00}");
        if (resultado.Cantidad > resultado.Comprobantes.Count)
            partes.Add($"se muestran los {resultado.Comprobantes.Count} más recientes");
        return string.Join(" · ", partes);
    }

    private async Task ReenviarAsync()
    {
        Mensaje = "Reenviando a la SUNAT…";
        var resultado = await _reenvio!.ReenviarAsync(inmediato: true);
        await CargarAsync();
        Mensaje = resultado?.Resumen
                  ?? "No había pendientes listos para reenviar: los recién emitidos esperan unos segundos. Pulse de nuevo en un momento.";
    }

    private static bool EsElectronico(Comprobante c) =>
        c.TipoComprobante is TipoComprobante.Boleta or TipoComprobante.Factura;

    private string? ResumenSunat()
    {
        var partes = new List<string>();
        if (PendientesSunat > 0)
            partes.Add(PendientesSunat == 1 ? "1 pendiente de envío a la SUNAT" : $"{PendientesSunat} pendientes de envío a la SUNAT");
        if (RechazadosSunat > 0)
            partes.Add(RechazadosSunat == 1 ? "1 rechazada por la SUNAT" : $"{RechazadosSunat} rechazadas por la SUNAT");
        return partes.Count == 0 ? null : string.Join(" · ", partes) + ". Pase el mouse por el estado para ver el motivo.";
    }

    private async Task ReimprimirAsync()
    {
        var comprobante = Seleccionado!;
        Mensaje = await _impresion.ImprimirAsync(comprobante)
            ? $"{comprobante.NumeroCompleto} reimpreso en {_impresion.Impresora}."
            : _impresion.Error;

        // Refresca la fila (columna Impresiones) con el comprobante ya leído completo.
        var posicion = Comprobantes.IndexOf(comprobante);
        if (posicion >= 0 && _impresion.Ultimo is { } actualizado)
        {
            Comprobantes[posicion] = actualizado;
            Seleccionado = actualizado;
        }
    }
}
