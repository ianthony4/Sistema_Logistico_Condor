using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Ventas;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Tipo de comprobante y botón Emitir e imprimir (F10) del resumen de venta
/// (HU-05, T-29). Valida con las reglas del dominio, guarda en la base central
/// con el siguiente correlativo y avisa para imprimir el ticket.
/// </summary>
public sealed class EmisionViewModel : ObservableObject
{
    private readonly IComprobanteRepositorio _comprobantes;
    private readonly VentaViewModel _venta;
    private readonly ClienteVentaViewModel? _cliente;
    private readonly Func<Sesion?> _sesion;
    private TipoEmision _tipo = TipoEmision.Boleta;
    private string? _error;
    private Comprobante? _ultimo;

    /// <param name="sesion">Sesión de la PC: da la terminal que emite (HU-14).</param>
    public EmisionViewModel(IComprobanteRepositorio comprobantes, VentaViewModel venta,
        ClienteVentaViewModel? cliente, Func<Sesion?> sesion)
    {
        _comprobantes = comprobantes;
        _venta = venta;
        _cliente = cliente;
        _sesion = sesion;
        EmitirCommand = new AsyncRelayCommand(EmitirAsync, () => _venta.Detalle.Count > 0);
        _venta.Detalle.CollectionChanged += (_, _) =>
        {
            Error = null;
            EmitirCommand.NotificarCambio();
        };
    }

    /// <summary>El comprobante quedó guardado en la base central: falta imprimirlo.</summary>
    public event Action<Comprobante>? Emitido;

    /// <summary>La emisión falló porque no responde la PC principal (T-06).</summary>
    public event Action? ConexionPerdida;

    public TipoEmision Tipo
    {
        get => _tipo;
        set
        {
            if (!SetProperty(ref _tipo, value))
                return;
            OnPropertyChanged(nameof(EsBoleta));
            OnPropertyChanged(nameof(EsBoletaConDni));
            Error = null;
        }
    }

    /// <summary>Para los botones de opción del resumen.</summary>
    public bool EsBoleta
    {
        get => Tipo == TipoEmision.Boleta;
        set
        {
            if (value)
                Tipo = TipoEmision.Boleta;
        }
    }

    public bool EsBoletaConDni
    {
        get => Tipo == TipoEmision.BoletaConDni;
        set
        {
            if (value)
                Tipo = TipoEmision.BoletaConDni;
        }
    }

    /// <summary>Por qué no se emitió; null si no hay error.</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>Último comprobante emitido desde esta PC.</summary>
    public Comprobante? Ultimo
    {
        get => _ultimo;
        private set => SetProperty(ref _ultimo, value);
    }

    public AsyncRelayCommand EmitirCommand { get; }

    private async Task EmitirAsync()
    {
        Error = null;
        var solicitud = new SolicitudEmision(Tipo, _venta.Detalle.Select(l => l.ADetalle()).ToList(), _cliente?.Seleccionado);

        var errores = EmisionComprobante.Errores(solicitud);
        if (errores.Count > 0)
        {
            // Criterio 2 de HU-05: se señala el campo DNI o RUC del panel Cliente.
            foreach (var error in errores.Where(e => e.Campo == CampoEmision.Documento))
                _cliente?.SenalarError(error.Mensaje);
            Error = string.Join(" ", errores.Select(e => e.Mensaje));
            return;
        }

        var sesion = _sesion();
        if (sesion is not { Registrada: true })
        {
            Error = "Esta PC aún no se registró en la base de datos central. Espere a que vuelva la conexión y pulse Emitir otra vez.";
            return;
        }

        var comprobante = EmisionComprobante.Preparar(solicitud);
        comprobante.TerminalId = sesion.Terminal.Id;
        comprobante.TerminalNombre = sesion.Terminal.Nombre;
        comprobante.Usuario = sesion.Usuario;

        try
        {
            Ultimo = await _comprobantes.EmitirAsync(comprobante);
        }
        catch (SinConexionException)
        {
            // T-06: no se emitió nada; el detalle y el cliente siguen en pantalla.
            Error = "Sin conexión con la PC principal: el comprobante NO se emitió. La venta se conserva; pulse Emitir para reintentar.";
            ConexionPerdida?.Invoke();
            return;
        }
        catch (Exception ex)
        {
            Error = $"No se pudo emitir: {ex.Message}";
            return;
        }

        Tipo = TipoEmision.Boleta;
        Emitido?.Invoke(Ultimo);
    }
}
