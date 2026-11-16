using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Diálogo Nuevo proveedor (HU-04, T-38): RUC, razón social y los datos
/// comerciales opcionales. Al guardar, el proveedor queda disponible en las
/// tres PC.
/// </summary>
public sealed class NuevoProveedorViewModel : ObservableObject
{
    private readonly IProveedorRepositorio _proveedores;
    private string _ruc = string.Empty;
    private string _razonSocial = string.Empty;
    private string _nombreComercial = string.Empty;
    private string _direccion = string.Empty;
    private string _telefono = string.Empty;
    private string _correo = string.Empty;
    private string _contacto = string.Empty;
    private string? _error;

    public NuevoProveedorViewModel(IProveedorRepositorio proveedores)
    {
        _proveedores = proveedores;
        GuardarCommand = new AsyncRelayCommand(GuardarAsync, PuedeGuardar);
        CancelarCommand = new RelayCommand(() => Cerrar?.Invoke(false));
    }

    /// <summary>Lo escucha la ventana para cerrarse: true si se guardó.</summary>
    public event Action<bool>? Cerrar;

    /// <summary>Guardar falló porque no responde la PC principal.</summary>
    public event Action? ConexionPerdida;

    public string Ruc
    {
        get => _ruc;
        set
        {
            if (SetProperty(ref _ruc, value ?? string.Empty))
                AlCambiarDatos();
        }
    }

    public string RazonSocial
    {
        get => _razonSocial;
        set
        {
            if (SetProperty(ref _razonSocial, value ?? string.Empty))
                AlCambiarDatos();
        }
    }

    public string NombreComercial
    {
        get => _nombreComercial;
        set => SetProperty(ref _nombreComercial, value ?? string.Empty);
    }

    public string Direccion
    {
        get => _direccion;
        set => SetProperty(ref _direccion, value ?? string.Empty);
    }

    public string Telefono
    {
        get => _telefono;
        set => SetProperty(ref _telefono, value ?? string.Empty);
    }

    public string Correo
    {
        get => _correo;
        set => SetProperty(ref _correo, value ?? string.Empty);
    }

    public string Contacto
    {
        get => _contacto;
        set => SetProperty(ref _contacto, value ?? string.Empty);
    }

    /// <summary>Mensaje en rojo bajo el formulario; null si no hay error.</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>Proveedor guardado, con su Id; null mientras no se guarde.</summary>
    public Proveedor? Guardado { get; private set; }

    public AsyncRelayCommand GuardarCommand { get; }

    public RelayCommand CancelarCommand { get; }

    /// <summary>Propone lo buscado en la ventana Proveedores: si son 11 dígitos, como RUC; si no, como razón social.</summary>
    public void Proponer(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return;
        var limpio = ValidadorDocumento.Limpiar(texto);
        if (ValidadorDocumento.TipoSegunLargo(limpio) == TipoDocumento.Ruc)
            Ruc = limpio;
        else
            RazonSocial = texto.Trim();
    }

    private bool PuedeGuardar() => !string.IsNullOrWhiteSpace(Ruc) && !string.IsNullOrWhiteSpace(RazonSocial);

    private void AlCambiarDatos()
    {
        Error = null;
        GuardarCommand.NotificarCambio();
    }

    private async Task GuardarAsync()
    {
        var proveedor = ValidadorProveedor.Normalizar(new Proveedor
        {
            Ruc = Ruc,
            RazonSocial = RazonSocial,
            NombreComercial = NombreComercial,
            Direccion = Direccion,
            Telefono = Telefono,
            Correo = Correo,
            Contacto = Contacto,
        });
        var errores = ValidadorProveedor.Errores(proveedor);
        if (errores.Count > 0)
        {
            Error = string.Join(" ", errores);
            return;
        }

        try
        {
            Guardado = await _proveedores.RegistrarAsync(proveedor);
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
            // ProveedorDuplicadoException trae el mensaje para el usuario.
            Error = ex.Message;
        }
    }
}
