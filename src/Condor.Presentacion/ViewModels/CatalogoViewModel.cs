using System.Collections.ObjectModel;
using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Ventana Catálogo (F2, HU-13, T-88): los productos con su precio, unidad y
/// código de barras. Se buscan por nombre o pasándolos por el lector, y al
/// producto elegido se le asigna el código leído: así los productos que ya
/// estaban en el catálogo se venden después con el lector.
/// </summary>
public sealed class CatalogoViewModel : ObservableObject
{
    public const int Cantidad = 200;

    private readonly IProductoRepositorio _productos;
    private readonly Func<NuevoProductoViewModel> _nuevoProducto;
    private string _texto = string.Empty;
    private Producto? _seleccionado;
    private string _codigo = string.Empty;
    private string? _mensaje;
    private string? _error;

    public CatalogoViewModel(IProductoRepositorio productos, Func<NuevoProductoViewModel> nuevoProducto)
    {
        _productos = productos;
        _nuevoProducto = nuevoProducto;
        BuscarCommand = new AsyncRelayCommand(BuscarAsync);
        AsignarCodigoCommand = new AsyncRelayCommand(AsignarCodigoAsync,
            () => Seleccionado is not null && !string.IsNullOrWhiteSpace(Codigo));
        QuitarCodigoCommand = new AsyncRelayCommand(QuitarCodigoAsync, () => Seleccionado?.CodigoBarras is not null);
    }

    /// <summary>No se pudo leer o guardar porque no responde la PC principal.</summary>
    public event Action? ConexionPerdida;

    public ObservableCollection<Producto> Productos { get; } = new();

    /// <summary>Parte del nombre o un código de barras (leído con el lector); vacío, todo el catálogo.</summary>
    public string Texto
    {
        get => _texto;
        set => SetProperty(ref _texto, value ?? string.Empty);
    }

    public Producto? Seleccionado
    {
        get => _seleccionado;
        set
        {
            if (!SetProperty(ref _seleccionado, value))
                return;
            Codigo = value?.CodigoBarras ?? string.Empty;
            Error = null;
            QuitarCodigoCommand.NotificarCambio();
        }
    }

    /// <summary>Código para el producto elegido: se pasa el producto por el lector con el cursor en el campo.</summary>
    public string Codigo
    {
        get => _codigo;
        set
        {
            if (SetProperty(ref _codigo, value ?? string.Empty))
                AsignarCodigoCommand.NotificarCambio();
        }
    }

    /// <summary>Cuántos se encontraron o lo último que se hizo.</summary>
    public string? Mensaje
    {
        get => _mensaje;
        private set => SetProperty(ref _mensaje, value);
    }

    /// <summary>Por qué no se asignó el código; null si no hay error.</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public AsyncRelayCommand BuscarCommand { get; }

    /// <summary>Asignar el código al producto elegido (Enter en el campo del código).</summary>
    public AsyncRelayCommand AsignarCodigoCommand { get; }

    public AsyncRelayCommand QuitarCodigoCommand { get; }

    public NuevoProductoViewModel CrearNuevoProducto()
    {
        var vm = _nuevoProducto();
        if (ValidadorProducto.PareceCodigo(Texto) && Productos.Count == 0)
            vm.CodigoBarras = ValidadorProducto.NormalizarCodigo(Texto)!;
        else if (Productos.Count == 0 && !string.IsNullOrWhiteSpace(Texto))
            vm.Nombre = Texto.Trim();
        return vm;
    }

    /// <summary>Tras guardar en el diálogo: se muestra en la lista y queda elegido.</summary>
    public async Task ProductoRegistradoAsync(Producto producto)
    {
        Texto = producto.Nombre;
        await BuscarAsync();
        Seleccionado = Productos.FirstOrDefault(p => p.Id == producto.Id) ?? Seleccionado;
        Mensaje = $"Producto registrado: {producto.Nombre} - S/ {producto.Precio:0.00}. Ya está disponible en las tres PC.";
    }

    public async Task BuscarAsync()
    {
        var texto = Texto.Trim();
        try
        {
            IReadOnlyList<Producto> encontrados;
            if (texto.Length == 0)
                encontrados = await _productos.ListarAsync();
            else if (ValidadorProducto.PareceCodigo(texto) && await _productos.BuscarPorCodigoBarrasAsync(texto) is { } porCodigo)
                encontrados = new[] { porCodigo };
            else
                encontrados = await _productos.BuscarPorNombreAsync(texto, Cantidad);

            Productos.Clear();
            foreach (var p in encontrados.Take(Cantidad))
                Productos.Add(p);
            Seleccionado = Productos.FirstOrDefault();
            var sinCodigo = Productos.Count(p => p.CodigoBarras is null);
            Mensaje = (Productos.Count, texto.Length) switch
            {
                (0, 0) => "El catálogo está vacío. Pulse Nuevo producto.",
                (0, _) when ValidadorProducto.PareceCodigo(texto) =>
                    $"Ningún producto tiene el código {ValidadorProducto.NormalizarCodigo(texto)}: elija el producto y asígnelo, o pulse Nuevo producto.",
                (0, _) => $"No hay productos con «{texto}».",
                (var n, _) => $"{(n == 1 ? "1 producto" : $"{n} productos")}, {sinCodigo} sin código de barras.",
            };
        }
        catch (SinConexionException)
        {
            Mensaje = "Sin conexión con la PC principal: no se puede listar el catálogo.";
            ConexionPerdida?.Invoke();
        }
    }

    private Task AsignarCodigoAsync() => GuardarCodigoAsync(Codigo);

    private Task QuitarCodigoAsync() => GuardarCodigoAsync(null);

    private async Task GuardarCodigoAsync(string? codigo)
    {
        var producto = Seleccionado!;
        if (ValidadorProducto.ErrorCodigo(codigo) is { } error)
        {
            Error = error;
            return;
        }

        try
        {
            var actualizado = await _productos.AsignarCodigoBarrasAsync(producto.Id, codigo);
            var posicion = Productos.IndexOf(producto);
            if (posicion >= 0)
                Productos[posicion] = actualizado;
            Seleccionado = actualizado;
            Codigo = actualizado.CodigoBarras ?? string.Empty;
            QuitarCodigoCommand.NotificarCambio();
            Error = null;
            Mensaje = actualizado.CodigoBarras is null
                ? $"Se quitó el código de barras de {actualizado.Nombre}."
                : $"{actualizado.Nombre} ya se vende con el lector (código {actualizado.CodigoBarras}).";
        }
        catch (SinConexionException)
        {
            Error = "Sin conexión con la PC principal: el código no se guardó.";
            ConexionPerdida?.Invoke();
        }
        catch (Exception ex)
        {
            // CodigoBarrasDuplicadoException dice de qué producto es el código.
            Error = ex.Message;
        }
    }
}
