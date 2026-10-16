using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Estado de la conexión con la PC principal (HU-16, T-06). Cuando se pierde,
/// la pantalla muestra un aviso con el botón Reintentar; lo que el usuario
/// tenía en pantalla (detalle de la venta, diálogo abierto) no se borra.
/// </summary>
public sealed class EstadoConexionViewModel : ObservableObject
{
    private readonly Func<CancellationToken, Task<bool>> _probar;
    private bool _conectado = true;
    private DateTime? _caidaDesde;

    /// <param name="probar">Comprueba si la base de datos responde (FabricaConexiones.ProbarAsync).</param>
    public EstadoConexionViewModel(Func<CancellationToken, Task<bool>> probar)
    {
        _probar = probar;
        ReintentarCommand = new AsyncRelayCommand(() => ActualizarAsync());
    }

    /// <summary>Se dispara al volver la conexión, para refrescar lo que haga falta.</summary>
    public event Action? ConexionRecuperada;

    public bool Conectado
    {
        get => _conectado;
        private set
        {
            if (SetProperty(ref _conectado, value))
                OnPropertyChanged(nameof(Aviso));
        }
    }

    public string Aviso => Conectado
        ? string.Empty
        : $"Sin conexión con la PC principal desde las {_caidaDesde:HH:mm:ss}. " +
          "Lo que tiene en pantalla se conserva. Revise el cable de red y pulse Reintentar.";

    public AsyncRelayCommand ReintentarCommand { get; }

    /// <summary>Consulta la base de datos y actualiza el aviso. La llama el temporizador cada pocos segundos.</summary>
    public async Task<bool> ActualizarAsync(CancellationToken ct = default)
    {
        bool responde;
        try
        {
            responde = await _probar(ct);
        }
        catch (Exception)
        {
            responde = false;
        }

        if (responde)
            MarcarConectado();
        else
            MarcarSinConexion();
        return responde;
    }

    /// <summary>Una operación falló por la red: se muestra el aviso sin esperar al temporizador.</summary>
    public void MarcarSinConexion()
    {
        if (Conectado)
            _caidaDesde = DateTime.Now;
        Conectado = false;
    }

    private void MarcarConectado()
    {
        var estabaCaida = !Conectado;
        Conectado = true;
        _caidaDesde = null;
        if (estabaCaida)
            ConexionRecuperada?.Invoke();
    }
}
