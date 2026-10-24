using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Panel Cliente de la pantalla Nueva venta (HU-03, T-23): un solo campo
/// "DNI o RUC" y el botón Buscar. Si el cliente existe, se completan su nombre
/// o razón social y su dirección; si no, se ofrece registrarlo.
/// </summary>
public sealed class ClienteVentaViewModel : ObservableObject
{
    private readonly IClienteRepositorio _clientes;
    private string _documento = string.Empty;
    private Cliente? _seleccionado;
    private string? _error;
    private bool _noEncontrado;
    private bool _asignando;

    public ClienteVentaViewModel(IClienteRepositorio clientes)
    {
        _clientes = clientes;
        BuscarCommand = new AsyncRelayCommand(BuscarAsync, () => !string.IsNullOrWhiteSpace(Documento));
        QuitarCommand = new RelayCommand(Limpiar, () => Seleccionado is not null);
    }

    /// <summary>La búsqueda falló porque no responde la PC principal (T-06).</summary>
    public event Action? ConexionPerdida;

    /// <summary>DNI o RUC tal como lo digita el vendedor.</summary>
    public string Documento
    {
        get => _documento;
        set
        {
            if (!SetProperty(ref _documento, value ?? string.Empty))
                return;
            OnPropertyChanged(nameof(TipoDigitado));
            BuscarCommand.NotificarCambio();
            if (_asignando)
                return;

            // Si cambia el número, el cliente elegido ya no corresponde.
            Seleccionado = null;
            NoEncontrado = false;
            Error = null;
        }
    }

    /// <summary>Cliente de la venta en curso; null si la boleta va sin cliente.</summary>
    public Cliente? Seleccionado
    {
        get => _seleccionado;
        private set
        {
            if (!SetProperty(ref _seleccionado, value))
                return;
            OnPropertyChanged(nameof(HayCliente));
            QuitarCommand.NotificarCambio();
        }
    }

    public bool HayCliente => Seleccionado is not null;

    /// <summary>Mensaje en rojo bajo el campo; el campo se marca mientras haya error.</summary>
    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
                OnPropertyChanged(nameof(TieneError));
        }
    }

    public bool TieneError => Error is not null;

    /// <summary>El documento es válido pero no hay cliente registrado con él.</summary>
    public bool NoEncontrado
    {
        get => _noEncontrado;
        private set => SetProperty(ref _noEncontrado, value);
    }

    /// <summary>Tipo que corresponde a lo digitado (8 dígitos DNI, 11 RUC); null si aún no es ninguno.</summary>
    public TipoDocumento? TipoDigitado => ValidadorDocumento.TipoSegunLargo(Documento);

    public AsyncRelayCommand BuscarCommand { get; }

    public RelayCommand QuitarCommand { get; }

    /// <summary>Deja al cliente elegido en la venta (al encontrarlo o al registrarlo).</summary>
    public void Seleccionar(Cliente cliente)
    {
        _asignando = true;
        Documento = cliente.NumeroDocumento;
        _asignando = false;

        Seleccionado = cliente;
        NoEncontrado = false;
        Error = null;
    }

    /// <summary>Vacía el panel: venta sin cliente.</summary>
    public void Limpiar()
    {
        _asignando = true;
        Documento = string.Empty;
        _asignando = false;

        Seleccionado = null;
        NoEncontrado = false;
        Error = null;
    }

    /// <summary>Marca el campo en error con el mensaje indicado (la usa la emisión, HU-05).</summary>
    public void SenalarError(string mensaje) => Error = mensaje;

    private async Task BuscarAsync()
    {
        var numero = ValidadorDocumento.Limpiar(Documento);
        var tipo = ValidadorDocumento.TipoSegunLargo(numero);
        Seleccionado = null;
        NoEncontrado = false;

        if (tipo is null)
        {
            Error = "Digite un DNI (8 dígitos) o un RUC (11 dígitos).";
            return;
        }

        // Criterio 3: un RUC con el dígito verificador mal no se busca.
        if (ValidadorDocumento.Error(tipo.Value, numero) is { } error)
        {
            Error = error;
            return;
        }

        try
        {
            var cliente = await _clientes.BuscarPorDocumentoAsync(tipo.Value, numero);
            if (cliente is null)
            {
                Error = null;
                NoEncontrado = true;
            }
            else
            {
                Seleccionar(cliente);
            }
        }
        catch (SinConexionException)
        {
            Error = "Sin conexión con la PC principal: no se puede buscar el cliente.";
            ConexionPerdida?.Invoke();
        }
        catch (Exception ex)
        {
            Error = $"No se pudo buscar el cliente: {ex.Message}";
        }
    }
}
