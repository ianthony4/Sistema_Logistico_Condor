using System.Collections.ObjectModel;
using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Últimos comprobantes emitidos, para reimprimir el que no salió (HU-12,
/// criterio 2; T-33). Es la primera parte del Historial (F5); la búsqueda por
/// fecha y cliente llega con HU-18. Desde el Sprint 3 muestra también el
/// estado de cada comprobante ante la SUNAT (HU-07, T-48).
/// </summary>
public sealed class ComprobantesRecientesViewModel : ObservableObject
{
    public const int Cantidad = 50;

    private readonly IComprobanteRepositorio _comprobantes;
    private readonly ImpresionViewModel _impresion;
    private Comprobante? _seleccionado;
    private string? _mensaje;

    public ComprobantesRecientesViewModel(IComprobanteRepositorio comprobantes, ImpresionViewModel impresion)
    {
        _comprobantes = comprobantes;
        _impresion = impresion;
        ActualizarCommand = new AsyncRelayCommand(CargarAsync);
        ReimprimirCommand = new AsyncRelayCommand(ReimprimirAsync, () => Seleccionado is not null);
    }

    public ObservableCollection<Comprobante> Comprobantes { get; } = new();

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

    public AsyncRelayCommand ActualizarCommand { get; }

    public AsyncRelayCommand ReimprimirCommand { get; }

    public async Task CargarAsync()
    {
        try
        {
            var recientes = await _comprobantes.ListarRecientesAsync(Cantidad);
            Comprobantes.Clear();
            foreach (var c in recientes)
                Comprobantes.Add(c);
            // Primero el más reciente que quedó sin imprimir.
            Seleccionado = Comprobantes.FirstOrDefault(c => c.Impresiones == 0) ?? Comprobantes.FirstOrDefault();
            OnPropertyChanged(nameof(PendientesSunat));
            OnPropertyChanged(nameof(RechazadosSunat));
            Mensaje = Comprobantes.Count == 0 ? "Aún no hay comprobantes emitidos." : ResumenSunat();
        }
        catch (SinConexionException)
        {
            Mensaje = "Sin conexión con la PC principal: no se pueden listar los comprobantes.";
        }
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
