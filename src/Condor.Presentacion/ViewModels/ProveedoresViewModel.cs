using System.Collections.ObjectModel;
using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Ventana Proveedores (F6, HU-04, T-39): lista de los proveedores con
/// búsqueda por RUC o razón social, y + Nuevo proveedor.
/// </summary>
public sealed class ProveedoresViewModel : ObservableObject
{
    private readonly IProveedorRepositorio _proveedores;
    private string _texto = string.Empty;
    private Proveedor? _seleccionado;
    private string? _mensaje;

    public ProveedoresViewModel(IProveedorRepositorio proveedores)
    {
        _proveedores = proveedores;
        BuscarCommand = new AsyncRelayCommand(BuscarAsync);
    }

    /// <summary>No se pudo leer la lista porque no responde la PC principal.</summary>
    public event Action? ConexionPerdida;

    public ObservableCollection<Proveedor> Proveedores { get; } = new();

    /// <summary>RUC o parte de la razón social o del nombre comercial.</summary>
    public string Texto
    {
        get => _texto;
        set => SetProperty(ref _texto, value ?? string.Empty);
    }

    public Proveedor? Seleccionado
    {
        get => _seleccionado;
        set => SetProperty(ref _seleccionado, value);
    }

    /// <summary>Cuántos se encontraron, el último registrado o el error.</summary>
    public string? Mensaje
    {
        get => _mensaje;
        private set => SetProperty(ref _mensaje, value);
    }

    public AsyncRelayCommand BuscarCommand { get; }

    /// <summary>Diálogo Nuevo proveedor con lo buscado propuesto como RUC o razón social.</summary>
    public NuevoProveedorViewModel CrearNuevoProveedor()
    {
        var vm = new NuevoProveedorViewModel(_proveedores);
        vm.ConexionPerdida += () => ConexionPerdida?.Invoke();
        if (Proveedores.Count == 0)
            vm.Proponer(Texto);
        return vm;
    }

    /// <summary>Tras guardar en el diálogo: se muestra en la lista y queda seleccionado.</summary>
    public async Task ProveedorRegistradoAsync(Proveedor proveedor)
    {
        Texto = string.Empty;
        await BuscarAsync();
        Seleccionado = Proveedores.FirstOrDefault(p => p.Id == proveedor.Id) ?? proveedor;
        Mensaje = $"Proveedor registrado: {proveedor.RazonSocial} (RUC {proveedor.Ruc}). Ya está disponible en las tres PC.";
    }

    public async Task BuscarAsync()
    {
        try
        {
            var encontrados = await _proveedores.BuscarAsync(Texto);
            Proveedores.Clear();
            foreach (var p in encontrados)
                Proveedores.Add(p);
            Seleccionado = Proveedores.FirstOrDefault();
            Mensaje = (Proveedores.Count, Texto.Trim().Length) switch
            {
                (0, 0) => "Aún no hay proveedores registrados. Pulse + Nuevo proveedor.",
                (0, _) => $"No hay proveedores con «{Texto.Trim()}». Pulse + Nuevo proveedor para registrarlo.",
                (1, _) => "1 proveedor.",
                (var n, _) => $"{n} proveedores.",
            };
        }
        catch (SinConexionException)
        {
            Mensaje = "Sin conexión con la PC principal: no se pueden listar los proveedores.";
            ConexionPerdida?.Invoke();
        }
    }
}
