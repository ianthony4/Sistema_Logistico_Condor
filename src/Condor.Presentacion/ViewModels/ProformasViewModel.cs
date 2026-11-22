using System.Collections.ObjectModel;
using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Impresion;
using Condor.Dominio.Ventas;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>Situación de una proforma en la lista (para el color de la columna Estado).</summary>
public enum EstadoProforma
{
    Vigente,
    Vencida,
    Convertida,
}

/// <summary>Una proforma en la ventana Proformas, con lo que se puede hacer con ella.</summary>
public sealed class FilaProforma
{
    public FilaProforma(Comprobante proforma, DateOnly hoy)
    {
        Proforma = proforma;
        Estado = proforma.Convertida ? EstadoProforma.Convertida
            : proforma.Vencida(hoy) ? EstadoProforma.Vencida
            : EstadoProforma.Vigente;
        var hasta = proforma.ValidaHasta is { } fecha ? FormatoTicket.Dia(fecha) : "-";
        EstadoTexto = Estado switch
        {
            EstadoProforma.Convertida => $"Convertida en {proforma.ConvertidaEn}",
            EstadoProforma.Vencida => $"Vencida el {hasta}",
            _ => $"Vigente hasta el {hasta}",
        };
    }

    public Comprobante Proforma { get; }

    public EstadoProforma Estado { get; }

    public string EstadoTexto { get; }

    /// <summary>Solo una proforma vigente que aún no se convirtió pasa a la venta (HU-09, criterio 3).</summary>
    public bool PuedeConvertir => Estado == EstadoProforma.Vigente;
}

/// <summary>
/// Ventana Proformas (F7, HU-09, T-73): lista de las proformas con búsqueda
/// por número o cliente, su vigencia y en qué comprobante terminó cada una.
/// Convertir en venta lleva el detalle y el cliente a la pantalla Nueva venta
/// sin reescribirlos.
/// </summary>
public sealed class ProformasViewModel : ObservableObject
{
    public const int Cantidad = 100;

    private readonly IComprobanteRepositorio _comprobantes;
    private readonly ImpresionViewModel? _impresion;
    private readonly Func<DateOnly> _hoy;
    private string _texto = string.Empty;
    private FilaProforma? _seleccionada;
    private string? _mensaje;

    /// <param name="impresion">Ticketera para reimprimir la proforma; null si no hay.</param>
    /// <param name="hoy">Fecha de hoy en Perú; por defecto la del reloj de la PC.</param>
    public ProformasViewModel(IComprobanteRepositorio comprobantes, ImpresionViewModel? impresion = null, Func<DateOnly>? hoy = null)
    {
        _comprobantes = comprobantes;
        _impresion = impresion;
        _hoy = hoy ?? (() => EmisionComprobante.FechaPeru(DateTimeOffset.Now));
        BuscarCommand = new AsyncRelayCommand(BuscarAsync);
        ConvertirCommand = new AsyncRelayCommand(ConvertirAsync, () => Seleccionada is { PuedeConvertir: true });
        ReimprimirCommand = new AsyncRelayCommand(ReimprimirAsync, () => Seleccionada is not null && _impresion is not null);
    }

    /// <summary>
    /// Se eligió una proforma vigente para venderla: trae su detalle completo y
    /// su cliente. La ventana se cierra y la pantalla de venta la carga.
    /// </summary>
    public event Action<Comprobante>? ProformaElegida;

    /// <summary>No se pudo leer la lista porque no responde la PC principal.</summary>
    public event Action? ConexionPerdida;

    public ObservableCollection<FilaProforma> Proformas { get; } = new();

    /// <summary>Número de la proforma (PR01-4 o 4), DNI o RUC del cliente, o parte de su nombre.</summary>
    public string Texto
    {
        get => _texto;
        set => SetProperty(ref _texto, value ?? string.Empty);
    }

    public FilaProforma? Seleccionada
    {
        get => _seleccionada;
        set
        {
            if (!SetProperty(ref _seleccionada, value))
                return;
            ConvertirCommand.NotificarCambio();
            ReimprimirCommand.NotificarCambio();
        }
    }

    /// <summary>Cuántas se encontraron, por qué no se puede convertir o el error.</summary>
    public string? Mensaje
    {
        get => _mensaje;
        private set => SetProperty(ref _mensaje, value);
    }

    public bool PuedeReimprimir => _impresion is not null;

    public AsyncRelayCommand BuscarCommand { get; }

    /// <summary>Convertir en venta (Enter): lleva la proforma elegida a la pantalla Nueva venta.</summary>
    public AsyncRelayCommand ConvertirCommand { get; }

    public AsyncRelayCommand ReimprimirCommand { get; }

    public async Task BuscarAsync()
    {
        try
        {
            var hoy = _hoy();
            var encontradas = await _comprobantes.ListarProformasAsync(Texto, Cantidad);
            Proformas.Clear();
            foreach (var p in encontradas)
                Proformas.Add(new FilaProforma(p, hoy));
            // Primero la más reciente que aún se puede convertir.
            Seleccionada = Proformas.FirstOrDefault(f => f.PuedeConvertir) ?? Proformas.FirstOrDefault();
            var vigentes = Proformas.Count(f => f.PuedeConvertir);
            Mensaje = (Proformas.Count, Texto.Trim().Length) switch
            {
                (0, 0) => "Aún no hay proformas. Arme el detalle en Nueva venta, elija Proforma y pulse Emitir (F10).",
                (0, _) => $"No hay proformas con «{Texto.Trim()}».",
                (var n, _) => $"{(n == 1 ? "1 proforma" : $"{n} proformas")}, {(vigentes == 1 ? "1 vigente" : $"{vigentes} vigentes")}. Elija una y pulse Convertir en venta (Enter).",
            };
        }
        catch (SinConexionException)
        {
            Mensaje = "Sin conexión con la PC principal: no se pueden listar las proformas.";
            ConexionPerdida?.Invoke();
        }
    }

    private async Task ConvertirAsync()
    {
        var fila = Seleccionada!;
        try
        {
            // Se vuelve a leer completa: otra PC pudo convertirla hace un momento.
            var proforma = await _comprobantes.ObtenerAsync(fila.Proforma.Id);
            if (proforma is null)
            {
                Mensaje = $"No se encontró la proforma {fila.Proforma.NumeroCompleto}.";
                return;
            }

            var actual = new FilaProforma(proforma, _hoy());
            if (!actual.PuedeConvertir)
            {
                Mensaje = actual.Estado == EstadoProforma.Convertida
                    ? $"La proforma {proforma.NumeroCompleto} ya se convirtió en {proforma.ConvertidaEn}: no se vende dos veces."
                    : $"La proforma {proforma.NumeroCompleto} venció el {FormatoTicket.Dia(proforma.ValidaHasta!.Value)}: emita una proforma nueva con los precios de hoy.";
                Reemplazar(fila, actual);
                return;
            }

            ProformaElegida?.Invoke(proforma);
        }
        catch (SinConexionException)
        {
            Mensaje = "Sin conexión con la PC principal: no se puede leer la proforma.";
            ConexionPerdida?.Invoke();
        }
    }

    private async Task ReimprimirAsync()
    {
        var proforma = Seleccionada!.Proforma;
        Mensaje = await _impresion!.ImprimirAsync(proforma)
            ? $"{proforma.NumeroCompleto} reimpresa en {_impresion.Impresora}."
            : _impresion.Error;
    }

    private void Reemplazar(FilaProforma anterior, FilaProforma nueva)
    {
        var posicion = Proformas.IndexOf(anterior);
        if (posicion < 0)
            return;
        Proformas[posicion] = nueva;
        Seleccionada = nueva;
    }
}
