using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Diálogo Nuevo cliente (HU-03, T-24): tipo de documento, número, nombre o
/// razón social y dirección. Al guardar, el cliente queda elegido en la venta.
/// </summary>
public sealed class NuevoClienteViewModel : ObservableObject
{
    private readonly IClienteRepositorio _clientes;
    private TipoDocumento _tipoDocumento = TipoDocumento.Dni;
    private string _numeroDocumento = string.Empty;
    private string _nombre = string.Empty;
    private string _direccion = string.Empty;
    private string? _error;

    public NuevoClienteViewModel(IClienteRepositorio clientes)
    {
        _clientes = clientes;
        GuardarCommand = new AsyncRelayCommand(GuardarAsync, PuedeGuardar);
        CancelarCommand = new RelayCommand(() => Cerrar?.Invoke(false));
    }

    /// <summary>Lo escucha la ventana para cerrarse: true si se guardó.</summary>
    public event Action<bool>? Cerrar;

    /// <summary>Guardar falló porque no responde la PC principal.</summary>
    public event Action? ConexionPerdida;

    public TipoDocumento TipoDocumento
    {
        get => _tipoDocumento;
        set
        {
            if (!SetProperty(ref _tipoDocumento, value))
                return;
            OnPropertyChanged(nameof(EsDni));
            OnPropertyChanged(nameof(EsRuc));
            OnPropertyChanged(nameof(EtiquetaNombre));
            AlCambiarDatos();
        }
    }

    /// <summary>Para los botones de opción DNI / RUC.</summary>
    public bool EsDni
    {
        get => TipoDocumento == TipoDocumento.Dni;
        set
        {
            if (value)
                TipoDocumento = TipoDocumento.Dni;
        }
    }

    public bool EsRuc
    {
        get => TipoDocumento == TipoDocumento.Ruc;
        set
        {
            if (value)
                TipoDocumento = TipoDocumento.Ruc;
        }
    }

    public string EtiquetaNombre => EsRuc ? "_Razón social" : "_Nombre completo";

    public string NumeroDocumento
    {
        get => _numeroDocumento;
        set
        {
            if (SetProperty(ref _numeroDocumento, value ?? string.Empty))
                AlCambiarDatos();
        }
    }

    public string Nombre
    {
        get => _nombre;
        set
        {
            if (SetProperty(ref _nombre, value ?? string.Empty))
                AlCambiarDatos();
        }
    }

    public string Direccion
    {
        get => _direccion;
        set => SetProperty(ref _direccion, value ?? string.Empty);
    }

    /// <summary>Mensaje en rojo bajo el formulario; null si no hay error.</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>Cliente guardado, con su Id; null mientras no se guarde.</summary>
    public Cliente? Guardado { get; private set; }

    public AsyncRelayCommand GuardarCommand { get; }

    public RelayCommand CancelarCommand { get; }

    /// <summary>
    /// Copia lo digitado en el panel Cliente: si tiene 11 dígitos se propone
    /// RUC y, si no, DNI.
    /// </summary>
    public void Proponer(string documento)
    {
        var numero = ValidadorDocumento.Limpiar(documento);
        TipoDocumento = ValidadorDocumento.TipoSegunLargo(numero) ?? TipoDocumento.Dni;
        NumeroDocumento = numero;
    }

    private bool PuedeGuardar() => !string.IsNullOrWhiteSpace(NumeroDocumento) && !string.IsNullOrWhiteSpace(Nombre);

    private void AlCambiarDatos()
    {
        Error = null;
        GuardarCommand.NotificarCambio();
    }

    private async Task GuardarAsync()
    {
        var cliente = ValidadorCliente.Normalizar(new Cliente
        {
            TipoDocumento = TipoDocumento,
            NumeroDocumento = NumeroDocumento,
            Nombre = Nombre,
            Direccion = Direccion,
        });
        var errores = ValidadorCliente.Errores(cliente);
        if (errores.Count > 0)
        {
            Error = string.Join(" ", errores);
            return;
        }

        try
        {
            Guardado = await _clientes.RegistrarAsync(cliente);
            Cerrar?.Invoke(true);
        }
        catch (SinConexionException)
        {
            // T-06: el diálogo sigue abierto con lo escrito para reintentar.
            Error = "Sin conexión con la PC principal. Sus datos se conservan: pulse Guardar para reintentar.";
            ConexionPerdida?.Invoke();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }
}
