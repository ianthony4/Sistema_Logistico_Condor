using System.Collections.ObjectModel;
using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Ventana Clientes (F3, T-97): los clientes registrados con búsqueda por DNI,
/// RUC o nombre, + Nuevo cliente y Usar en la venta, que lo deja elegido en la
/// pantalla Nueva venta.
/// </summary>
public sealed class ClientesViewModel : ObservableObject
{
    private readonly IClienteRepositorio _clientes;
    private string _texto = string.Empty;
    private Cliente? _seleccionado;
    private string? _mensaje;

    public ClientesViewModel(IClienteRepositorio clientes)
    {
        _clientes = clientes;
        BuscarCommand = new AsyncRelayCommand(BuscarAsync);
        UsarCommand = new RelayCommand(() => ClienteElegido?.Invoke(Seleccionado!), () => Seleccionado is not null);
    }

    /// <summary>Se pulsó Usar en la venta: la ventana se cierra y la venta lleva a este cliente.</summary>
    public event Action<Cliente>? ClienteElegido;

    public event Action? ConexionPerdida;

    public ObservableCollection<Cliente> Clientes { get; } = new();

    /// <summary>DNI, RUC (o sus primeros dígitos) o parte del nombre o razón social.</summary>
    public string Texto
    {
        get => _texto;
        set => SetProperty(ref _texto, value ?? string.Empty);
    }

    public Cliente? Seleccionado
    {
        get => _seleccionado;
        set
        {
            if (SetProperty(ref _seleccionado, value))
                UsarCommand.NotificarCambio();
        }
    }

    public string? Mensaje
    {
        get => _mensaje;
        private set => SetProperty(ref _mensaje, value);
    }

    public AsyncRelayCommand BuscarCommand { get; }

    public RelayCommand UsarCommand { get; }

    /// <summary>Diálogo Nuevo cliente con lo buscado propuesto si es un DNI o RUC.</summary>
    public NuevoClienteViewModel CrearNuevoCliente()
    {
        var vm = new NuevoClienteViewModel(_clientes);
        vm.ConexionPerdida += () => ConexionPerdida?.Invoke();
        if (Clientes.Count == 0 && Texto.Trim().Length > 0 && Texto.Trim().All(char.IsAsciiDigit))
            vm.Proponer(Texto.Trim());
        return vm;
    }

    /// <summary>Tras guardar en el diálogo: aparece en la lista, elegido.</summary>
    public async Task ClienteRegistradoAsync(Cliente cliente)
    {
        Texto = cliente.NumeroDocumento;
        await BuscarAsync();
        Seleccionado = Clientes.FirstOrDefault(c => c.Id == cliente.Id) ?? Seleccionado;
        Mensaje = $"Cliente registrado: {cliente.Nombre} ({cliente.Documento}). Ya está disponible en las tres PC.";
    }

    public async Task BuscarAsync()
    {
        try
        {
            var encontrados = await _clientes.BuscarAsync(Texto);
            Clientes.Clear();
            foreach (var c in encontrados)
                Clientes.Add(c);
            Seleccionado = Clientes.FirstOrDefault();
            Mensaje = (Clientes.Count, Texto.Trim().Length) switch
            {
                (0, 0) => "Aún no hay clientes registrados. Pulse Nuevo cliente.",
                (0, _) => $"No hay clientes con «{Texto.Trim()}». Pulse Nuevo cliente para registrarlo.",
                (1, _) => "1 cliente.",
                (var n, _) => $"{n} clientes.",
            };
        }
        catch (SinConexionException)
        {
            Mensaje = "Sin conexión con la PC principal: no se pueden listar los clientes.";
            ConexionPerdida?.Invoke();
        }
    }
}
