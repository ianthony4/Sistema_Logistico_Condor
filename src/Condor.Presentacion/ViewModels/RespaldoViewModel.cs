using System.Collections.ObjectModel;
using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Ventana Respaldo (F9, HU-15, T-62): el Encargado General conecta el disco
/// externo a la PC principal, pulsa Generar respaldo, ve el avance y al
/// terminar el sistema confirma dónde quedó el archivo (criterio 1). Muestra
/// también los últimos respaldos de cualquier PC.
/// </summary>
public sealed class RespaldoViewModel : ObservableObject
{
    private readonly IGeneradorRespaldo? _generador;
    private readonly IRespaldoRepositorio _registro;
    private readonly Func<Sesion?> _sesion;
    private string _carpeta;
    private int _porcentaje;
    private string? _paso;
    private bool _generando;
    private string? _mensaje;
    private string? _error;
    private Respaldo? _ultimo;

    /// <param name="generador">pg_dump de esta PC; null si no es la PC principal (no tiene la sección Respaldo).</param>
    /// <param name="sesion">Sesión de la PC: da la terminal que genera el respaldo.</param>
    /// <param name="carpeta">Carpeta del disco externo de condor.json.</param>
    public RespaldoViewModel(IGeneradorRespaldo? generador, IRespaldoRepositorio registro, Func<Sesion?> sesion, string? carpeta)
    {
        _generador = generador;
        _registro = registro;
        _sesion = sesion;
        _carpeta = carpeta ?? string.Empty;
        GenerarCommand = new AsyncRelayCommand(GenerarAsync, () => PuedeGenerar && !Generando && !string.IsNullOrWhiteSpace(Carpeta));
        ActualizarCommand = new AsyncRelayCommand(CargarAsync);
    }

    /// <summary>No se pudo leer o anotar porque no responde la PC principal.</summary>
    public event Action? ConexionPerdida;

    /// <summary>El respaldo quedó en el disco y anotado en la base central.</summary>
    public event Action<Respaldo>? RespaldoGenerado;

    /// <summary>True en la PC principal (tiene la sección Respaldo de condor.json).</summary>
    public bool PuedeGenerar => _generador is not null;

    /// <summary>Por qué no se puede generar desde esta PC; null en la PC principal.</summary>
    public string? Indicacion => PuedeGenerar
        ? null
        : "Los respaldos se generan desde la PC principal, que tiene el disco externo (sección Respaldo de condor.json).";

    /// <summary>Carpeta del disco externo; se puede cambiar si Windows le dio otra letra.</summary>
    public string Carpeta
    {
        get => _carpeta;
        set
        {
            if (SetProperty(ref _carpeta, value ?? string.Empty))
                GenerarCommand.NotificarCambio();
        }
    }

    /// <summary>Avance de 0 a 100 para la barra de progreso.</summary>
    public int Porcentaje
    {
        get => _porcentaje;
        private set => SetProperty(ref _porcentaje, value);
    }

    /// <summary>Qué está haciendo: "Copiando producto (5 de 11)…".</summary>
    public string? Paso
    {
        get => _paso;
        private set => SetProperty(ref _paso, value);
    }

    public bool Generando
    {
        get => _generando;
        private set
        {
            if (SetProperty(ref _generando, value))
                GenerarCommand.NotificarCambio();
        }
    }

    /// <summary>Confirmación al terminar (criterio 1); null mientras no haya.</summary>
    public string? Mensaje
    {
        get => _mensaje;
        private set => SetProperty(ref _mensaje, value);
    }

    /// <summary>Por qué no se generó: disco desconectado, sin espacio...</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>El respaldo más reciente de cualquier PC; null si nunca se hizo uno.</summary>
    public Respaldo? Ultimo
    {
        get => _ultimo;
        private set
        {
            if (SetProperty(ref _ultimo, value))
                OnPropertyChanged(nameof(TextoUltimo));
        }
    }

    public string TextoUltimo => Ultimo is { } r
        ? $"Último respaldo: {r.TerminadoEn.ToLocalTime():dd/MM/yyyy HH:mm}, {r.Archivo} ({r.Tamano})."
        : "Aún no se ha generado ningún respaldo.";

    public ObservableCollection<Respaldo> Recientes { get; } = new();

    public AsyncRelayCommand GenerarCommand { get; }

    public AsyncRelayCommand ActualizarCommand { get; }

    /// <summary>Lee los últimos respaldos. No lanza excepciones.</summary>
    public async Task CargarAsync()
    {
        try
        {
            var lista = await _registro.ListarAsync(10);
            Recientes.Clear();
            foreach (var r in lista)
                Recientes.Add(r);
            Ultimo = lista.FirstOrDefault();
        }
        catch (SinConexionException)
        {
            Error = "Sin conexión con la PC principal: no se pueden listar los respaldos.";
            ConexionPerdida?.Invoke();
        }
    }

    private async Task GenerarAsync()
    {
        Error = null;
        Mensaje = null;
        Porcentaje = 0;
        Paso = null;

        var sesion = _sesion();
        if (sesion is not { Registrada: true })
        {
            Error = "Esta PC aún no se registró en la base de datos central. Espere a que vuelva la conexión.";
            return;
        }

        Generando = true;
        Respaldo? respaldo = null;
        try
        {
            respaldo = await _generador!.GenerarAsync(Carpeta.Trim(), new ProgresoDirecto(p =>
            {
                Porcentaje = p.Porcentaje;
                Paso = p.Paso;
            }));

            respaldo.TerminalId = sesion.Terminal.Id;
            respaldo.TerminalNombre = sesion.Terminal.Nombre;
            await _registro.RegistrarAsync(respaldo);
            Mensaje = $"Respaldo terminado: {respaldo.Archivo} ({respaldo.Tamano}) quedó en {respaldo.Carpeta}. " +
                      "Desconecte el disco externo con «Quitar hardware de forma segura» y guárdelo fuera del local.";
            Ultimo = respaldo;
            Recientes.Insert(0, respaldo);
            RespaldoGenerado?.Invoke(respaldo);
        }
        catch (RespaldoException ex)
        {
            Error = ex.Message;
        }
        catch (SinConexionException)
        {
            Error = respaldo is null
                ? "Sin conexión con la PC principal: el respaldo NO se generó. Revise la red y vuelva a intentarlo."
                : $"El archivo {respaldo.Archivo} quedó en el disco, pero no se pudo anotar en la base central (sin conexión). Vuelva a generarlo cuando vuelva la red.";
            ConexionPerdida?.Invoke();
        }
        catch (Exception ex)
        {
            Error = $"No se pudo generar el respaldo: {ex.Message}";
        }
        finally
        {
            Generando = false;
            if (Error is not null)
            {
                Porcentaje = 0;
                Paso = null;
            }
        }
    }

    /// <summary>Informa el avance en el acto (Progress&lt;T&gt; lo encolaría y llegaría tarde al terminar).</summary>
    private sealed class ProgresoDirecto(Action<ProgresoRespaldo> alInformar) : IProgress<ProgresoRespaldo>
    {
        public void Report(ProgresoRespaldo value) => alInformar(value);
    }
}
