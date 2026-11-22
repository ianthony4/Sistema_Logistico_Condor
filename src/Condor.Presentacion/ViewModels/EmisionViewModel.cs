using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Ventas;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Tipo de comprobante y botón Emitir e imprimir (F10) del resumen de venta
/// (HU-05, T-29). Valida con las reglas del dominio, guarda en la base central
/// con el siguiente correlativo y avisa para imprimir el ticket. Desde el
/// Sprint 4 también emite la factura con RUC en la serie F001 (HU-06, T-51)
/// y desde el Sprint 5 la nota de venta NV01 (HU-08, T-68), la proforma PR01
/// y la venta que sale de una proforma (HU-09, T-73).
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
    private Comprobante? _proformaOrigen;

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
            // Si se vacía el detalle (Esc o tras emitir), la venta ya no sale de la proforma.
            if (_venta.Detalle.Count == 0)
                ProformaOrigen = null;
        };
    }

    /// <summary>El comprobante quedó guardado en la base central: falta imprimirlo.</summary>
    public event Action<Comprobante>? Emitido;

    /// <summary>La emisión falló porque no responde la PC principal (T-06).</summary>
    public event Action? ConexionPerdida;

    /// <summary>Se eligió Factura sin un cliente con RUC: la pantalla lleva el foco al campo DNI o RUC (HU-06).</summary>
    public event Action? RucRequerido;

    public TipoEmision Tipo
    {
        get => _tipo;
        set
        {
            if (!SetProperty(ref _tipo, value))
                return;
            OnPropertyChanged(nameof(EsBoleta));
            OnPropertyChanged(nameof(EsBoletaConDni));
            OnPropertyChanged(nameof(EsFactura));
            OnPropertyChanged(nameof(EsNotaVenta));
            OnPropertyChanged(nameof(EsProforma));
            Error = null;

            // HU-06: con la factura, el panel Cliente pide el RUC y recibe el foco.
            _cliente?.ExigirRuc(value == TipoEmision.Factura);
            if (value == TipoEmision.Factura && _cliente?.Seleccionado is not { TipoDocumento: TipoDocumento.Ruc })
                RucRequerido?.Invoke();
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

    /// <summary>Factura con RUC, serie F001 (HU-06, T-51).</summary>
    public bool EsFactura
    {
        get => Tipo == TipoEmision.Factura;
        set
        {
            if (value)
                Tipo = TipoEmision.Factura;
        }
    }

    /// <summary>Nota de venta para el control interno, serie NV01; no va a la SUNAT (HU-08, T-68).</summary>
    public bool EsNotaVenta
    {
        get => Tipo == TipoEmision.NotaVenta;
        set
        {
            if (value)
                Tipo = TipoEmision.NotaVenta;
        }
    }

    /// <summary>Proforma PR01 para cotizar a los clientes de obra; no va a la SUNAT (HU-09, T-73).</summary>
    public bool EsProforma
    {
        get => Tipo == TipoEmision.Proforma;
        set
        {
            if (value)
                Tipo = TipoEmision.Proforma;
        }
    }

    /// <summary>
    /// Proforma de la que sale la venta en curso (HU-09); null si el detalle
    /// se armó en la pantalla. Al emitir, el comprobante queda enlazado a ella.
    /// </summary>
    public Comprobante? ProformaOrigen
    {
        get => _proformaOrigen;
        set
        {
            if (SetProperty(ref _proformaOrigen, value))
                OnPropertyChanged(nameof(AvisoProforma));
        }
    }

    /// <summary>Texto del resumen cuando la venta sale de una proforma.</summary>
    public string? AvisoProforma => ProformaOrigen is { } p
        ? $"Venta desde la proforma {p.NumeroCompleto}{(p.Cliente is { } c ? $" ({c.Nombre})" : string.Empty)}. Elija el comprobante y pulse Emitir."
        : null;

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
        var solicitud = new SolicitudEmision(Tipo, _venta.Detalle.Select(l => l.ADetalle()).ToList(), _cliente?.Seleccionado,
            ProformaOrigen);

        var errores = EmisionComprobante.Errores(solicitud);
        if (errores.Count > 0)
        {
            // Criterio 2 de HU-05: se señala el campo DNI o RUC del panel Cliente.
            foreach (var error in errores.Where(e => e.Campo == CampoEmision.Documento))
                _cliente?.SenalarError(error.Mensaje);
            Error = string.Join(" ", errores.Select(e => e.Mensaje));
            // Criterio 2 de HU-06: la factura no se emite y se pide el RUC.
            if (Tipo == TipoEmision.Factura && errores.Any(e => e.Campo == CampoEmision.Documento))
                RucRequerido?.Invoke();
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
