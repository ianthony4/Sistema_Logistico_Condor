using Condor.Dominio.Contratos;
using Condor.Dominio.Sunat;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Reenvío automático de los comprobantes pendientes (HU-17, T-58). El
/// arranque lo llama cada pocos minutos (ReenvioMinutos de condor.json) sin
/// que el vendedor haga nada; el pie de la pantalla de venta dice cuántos
/// siguen pendientes y avisa si el más antiguo está por vencer el plazo de
/// la SUNAT. Desde el Historial (F5) también se puede reenviar en el acto.
/// </summary>
public sealed class ReenvioSunatViewModel : ObservableObject
{
    private readonly IReenvioPendientes? _reenvio;
    private readonly IEnvioSunatRepositorio _registro;
    private readonly Func<DateTimeOffset> _ahora;
    private ResumenPendientes _resumen = ResumenPendientes.Ninguno;
    private string? _ultimoResultado;
    private bool _reenviando;

    /// <param name="reenvio">Reenvío de esta PC; null si no tiene la SUNAT configurada (solo cuenta los pendientes).</param>
    /// <param name="ahora">Para las pruebas; por defecto la hora de la PC.</param>
    public ReenvioSunatViewModel(IReenvioPendientes? reenvio, IEnvioSunatRepositorio registro, Func<DateTimeOffset>? ahora = null)
    {
        _reenvio = reenvio;
        _registro = registro;
        _ahora = ahora ?? (() => DateTimeOffset.Now);
        ReenviarAhoraCommand = new AsyncRelayCommand(async () => await ReenviarAsync(inmediato: true), () => Activo && !Reenviando);
    }

    /// <summary>No se pudo consultar o guardar porque no responde la PC principal.</summary>
    public event Action? ConexionPerdida;

    /// <summary>Terminó una vuelta que envió algo o se cortó: el texto va a la barra de estado.</summary>
    public event Action<string>? Reenviado;

    /// <summary>True si esta PC reenvía (tiene la SUNAT configurada y ReenvioMinutos mayor que 0).</summary>
    public bool Activo => _reenvio is not null;

    public int Pendientes => _resumen.Cantidad;

    /// <summary>El pendiente más antiguo lleva más de 2 días: la SUNAT da 3 para enviarlo.</summary>
    public bool PorVencer => _resumen.PorVencer(_ahora());

    /// <summary>Texto del pie de la pantalla de venta; null si no hay pendientes.</summary>
    public string? Aviso
    {
        get
        {
            if (_resumen.Cantidad == 0)
                return null;
            var texto = _resumen.Cantidad == 1 ? "1 pendiente SUNAT" : $"{_resumen.Cantidad} pendientes SUNAT";
            if (PorVencer)
                texto += $" · el más antiguo es del {_resumen.MasAntiguo!.Value.ToLocalTime():dd/MM}: vence el plazo de 3 días";
            return texto;
        }
    }

    /// <summary>Detalle para el ToolTip del aviso: qué pasó en la última vuelta.</summary>
    public string Detalle => (_ultimoResultado ?? (Activo
        ? "Se reenvían solos en cuanto vuelva internet."
        : "Esta PC no reenvía: lo hace la PC que tiene la SUNAT configurada.")) + " Ver Historial (F5).";

    public string? UltimoResultado
    {
        get => _ultimoResultado;
        private set
        {
            if (SetProperty(ref _ultimoResultado, value))
                OnPropertyChanged(nameof(Detalle));
        }
    }

    public bool Reenviando
    {
        get => _reenviando;
        private set
        {
            if (SetProperty(ref _reenviando, value))
                ReenviarAhoraCommand.NotificarCambio();
        }
    }

    public AsyncRelayCommand ReenviarAhoraCommand { get; }

    /// <summary>Vuelve a contar los pendientes (al arrancar, tras emitir y tras cada vuelta). No lanza excepciones.</summary>
    public async Task ActualizarAsync()
    {
        try
        {
            _resumen = await _registro.ResumenPendientesAsync();
        }
        catch (SinConexionException)
        {
            ConexionPerdida?.Invoke();
            return;
        }
        catch (Exception)
        {
            return;
        }
        OnPropertyChanged(nameof(Pendientes));
        OnPropertyChanged(nameof(PorVencer));
        OnPropertyChanged(nameof(Aviso));
    }

    /// <summary>
    /// Una vuelta del reenvío. Si ya hay una en curso (el temporizador y el
    /// botón a la vez), no empieza otra. Nunca lanza excepciones: devuelve
    /// null si no se reenvió nada.
    /// </summary>
    /// <param name="inmediato">True desde el botón: no espera los minutos entre intentos.</param>
    public async Task<ResultadoReenvio?> ReenviarAsync(bool inmediato = false)
    {
        if (_reenvio is null || Reenviando)
            return null;

        Reenviando = true;
        ResultadoReenvio? resultado = null;
        try
        {
            resultado = await _reenvio.ReenviarAsync(inmediato);
            if (resultado.Resumen is { } resumen)
            {
                UltimoResultado = $"{resumen} ({_ahora().ToLocalTime():HH:mm})";
                Reenviado?.Invoke(resumen);
            }
        }
        catch (SinConexionException)
        {
            ConexionPerdida?.Invoke();
        }
        catch (Exception ex)
        {
            UltimoResultado = $"El reenvío falló: {ex.Message}";
        }
        finally
        {
            Reenviando = false;
        }

        await ActualizarAsync();
        return resultado;
    }
}
