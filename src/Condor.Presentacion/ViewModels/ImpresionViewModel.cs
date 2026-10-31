using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Impresion;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Impresión del ticket al emitir y reimpresión (HU-12, T-33). Si la
/// ticketera falla, el comprobante ya está guardado: se avisa y se puede
/// reimprimir con F11 o desde el Historial (F5).
/// </summary>
public sealed class ImpresionViewModel : ObservableObject
{
    private readonly IImpresoraTickets _impresora;
    private readonly IEmpresaRepositorio _empresas;
    private readonly IComprobanteRepositorio _comprobantes;
    private readonly int _columnas;
    private Empresa? _empresa;
    private Comprobante? _ultimo;
    private string? _error;

    public ImpresionViewModel(IImpresoraTickets impresora, IEmpresaRepositorio empresas,
        IComprobanteRepositorio comprobantes, int columnas = FormatoTicket.ColumnasPorDefecto)
    {
        _impresora = impresora;
        _empresas = empresas;
        _comprobantes = comprobantes;
        _columnas = columnas;
        ReimprimirCommand = new AsyncRelayCommand(() => ImprimirAsync(Ultimo!), () => Ultimo is not null);
    }

    /// <summary>No se pudo leer la empresa o el comprobante porque no responde la PC principal.</summary>
    public event Action? ConexionPerdida;

    public string Impresora => _impresora.Nombre;

    /// <summary>Último comprobante que se mandó a imprimir; F11 lo reimprime.</summary>
    public Comprobante? Ultimo
    {
        get => _ultimo;
        private set
        {
            if (SetProperty(ref _ultimo, value))
                ReimprimirCommand.NotificarCambio();
        }
    }

    /// <summary>Por qué no salió el último ticket; null si salió bien.</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public AsyncRelayCommand ReimprimirCommand { get; }

    /// <summary>
    /// Imprime el comprobante y anota la impresión en la base central.
    /// Devuelve false si no salió; el motivo queda en Error.
    /// </summary>
    public async Task<bool> ImprimirAsync(Comprobante comprobante)
    {
        Ultimo = comprobante;
        Error = null;

        try
        {
            // Lo que viene del historial no trae el detalle: se lee completo.
            if (comprobante.Detalle.Count == 0)
            {
                comprobante = await _comprobantes.ObtenerAsync(comprobante.Id)
                              ?? throw new InvalidOperationException($"No se encontró {comprobante.NumeroCompleto}.");
                Ultimo = comprobante;
            }

            _empresa ??= await _empresas.ObtenerAsync();
            await _impresora.ImprimirAsync(FormatoTicket.Generar(_empresa, comprobante, _columnas));
        }
        catch (ImpresionException ex)
        {
            // Criterio 2 de HU-12: se avisa, pero el comprobante sigue registrado.
            Error = $"{comprobante.NumeroCompleto} quedó registrada, pero no se imprimió. {ex.Message} Para reimprimir pulse F11.";
            return false;
        }
        catch (SinConexionException)
        {
            Error = $"Sin conexión con la PC principal: no se pudo preparar el ticket de {comprobante.NumeroCompleto}. Pulse F11 al volver la conexión.";
            ConexionPerdida?.Invoke();
            return false;
        }
        catch (Exception ex)
        {
            Error = $"No se pudo imprimir {comprobante.NumeroCompleto}: {ex.Message}";
            return false;
        }

        try
        {
            await _comprobantes.RegistrarImpresionAsync(comprobante.Id);
            comprobante.Impresiones++;
        }
        catch (Exception)
        {
            // El ticket ya salió; si no se pudo anotar, no se molesta al vendedor.
        }
        return true;
    }
}
