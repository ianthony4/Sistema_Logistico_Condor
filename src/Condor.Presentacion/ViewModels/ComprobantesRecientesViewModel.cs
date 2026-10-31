using System.Collections.ObjectModel;
using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Últimos comprobantes emitidos, para reimprimir el que no salió (HU-12,
/// criterio 2; T-33). Es la primera parte del Historial (F5); la búsqueda por
/// fecha y cliente llega con HU-18.
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
            Mensaje = Comprobantes.Count == 0 ? "Aún no hay comprobantes emitidos." : null;
        }
        catch (SinConexionException)
        {
            Mensaje = "Sin conexión con la PC principal: no se pueden listar los comprobantes.";
        }
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
