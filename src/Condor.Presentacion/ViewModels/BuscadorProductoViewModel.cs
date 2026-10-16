using System.Collections.ObjectModel;
using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Campo Producto con autocompletado (HU-02): a partir de la tercera letra
/// muestra los productos que coinciden, con su precio.
/// </summary>
public sealed class BuscadorProductoViewModel : ObservableObject
{
    public const int MinimoLetras = 3;
    public const int MaximoSugerencias = 15;

    private readonly IProductoRepositorio _productos;
    private CancellationTokenSource? _busquedaEnCurso;
    private string _texto = string.Empty;
    private Producto? _seleccionado;
    private bool _mostrarSugerencias;
    private string? _error;
    private bool _eligiendo;

    public BuscadorProductoViewModel(IProductoRepositorio productos)
    {
        _productos = productos;
        ElegirCommand = new RelayCommand(_ => Elegir(Seleccionado), _ => Seleccionado is not null);
        CerrarCommand = new RelayCommand(() => MostrarSugerencias = false);
    }

    /// <summary>Se dispara cuando el usuario elige un producto de la lista.</summary>
    public event Action<Producto>? ProductoElegido;

    /// <summary>La búsqueda falló porque no responde la PC principal (T-06).</summary>
    public event Action? ConexionPerdida;

    /// <summary>
    /// Espera tras la última tecla antes de consultar, para no lanzar una
    /// consulta por cada letra. En las pruebas se pone en cero.
    /// </summary>
    public TimeSpan Espera { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Última búsqueda lanzada; las pruebas la esperan con await.</summary>
    public Task UltimaBusqueda { get; private set; } = Task.CompletedTask;

    public ObservableCollection<Producto> Sugerencias { get; } = new();

    public string Texto
    {
        get => _texto;
        set
        {
            if (!SetProperty(ref _texto, value ?? string.Empty) || _eligiendo)
                return;
            UltimaBusqueda = ProgramarBusquedaAsync(_texto);
        }
    }

    public Producto? Seleccionado
    {
        get => _seleccionado;
        set
        {
            if (SetProperty(ref _seleccionado, value))
                ElegirCommand.NotificarCambio();
        }
    }

    public bool MostrarSugerencias
    {
        get => _mostrarSugerencias;
        private set => SetProperty(ref _mostrarSugerencias, value);
    }

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public RelayCommand ElegirCommand { get; }

    public RelayCommand CerrarCommand { get; }

    /// <summary>Elige el producto: lo notifica y deja su nombre en el campo sin volver a buscar.</summary>
    public void Elegir(Producto? producto)
    {
        if (producto is null)
            return;

        _eligiendo = true;
        Texto = producto.Nombre;
        _eligiendo = false;

        MostrarSugerencias = false;
        ProductoElegido?.Invoke(producto);
    }

    /// <summary>
    /// Vuelve a consultar si la lista está abierta: la usa el aviso de cambios
    /// del catálogo para mostrar los productos recién registrados en otra PC.
    /// </summary>
    public void Refrescar()
    {
        // Se repite la búsqueda si la lista está abierta o si lo escrito no
        // encontraba nada (quizá el producto nuevo coincide). No se toca un
        // producto ya elegido.
        var hayTexto = Texto.Trim().Length >= MinimoLetras;
        if (hayTexto && (MostrarSugerencias || Sugerencias.Count == 0))
            UltimaBusqueda = ProgramarBusquedaAsync(Texto);
    }

    /// <summary>Vacía el campo para el siguiente producto.</summary>
    public void Limpiar()
    {
        _busquedaEnCurso?.Cancel();
        _eligiendo = true;
        Texto = string.Empty;
        _eligiendo = false;
        Sugerencias.Clear();
        Seleccionado = null;
        MostrarSugerencias = false;
    }

    private async Task ProgramarBusquedaAsync(string texto)
    {
        // Cancela la búsqueda anterior: solo importa la del texto actual.
        _busquedaEnCurso?.Cancel();
        var cts = new CancellationTokenSource();
        _busquedaEnCurso = cts;
        Error = null;

        if (texto.Trim().Length < MinimoLetras)
        {
            Sugerencias.Clear();
            MostrarSugerencias = false;
            return;
        }

        try
        {
            if (Espera > TimeSpan.Zero)
                await Task.Delay(Espera, cts.Token);

            var encontrados = await _productos.BuscarPorNombreAsync(texto.Trim(), MaximoSugerencias, cts.Token);
            if (cts.IsCancellationRequested)
                return;

            Sugerencias.Clear();
            foreach (var p in encontrados)
                Sugerencias.Add(p);
            Seleccionado = Sugerencias.FirstOrDefault();
            MostrarSugerencias = Sugerencias.Count > 0;
        }
        catch (OperationCanceledException)
        {
            // El usuario siguió escribiendo; la búsqueda nueva reemplaza a esta.
        }
        catch (SinConexionException)
        {
            Sugerencias.Clear();
            MostrarSugerencias = false;
            Error = "Sin conexión con la PC principal: no se puede buscar.";
            ConexionPerdida?.Invoke();
        }
        catch (Exception ex)
        {
            Sugerencias.Clear();
            MostrarSugerencias = false;
            Error = $"No se pudo buscar el producto: {ex.Message}";
        }
    }
}
