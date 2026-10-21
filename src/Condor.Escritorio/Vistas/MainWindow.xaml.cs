using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Condor.Presentacion.ViewModels;

namespace Condor.Escritorio.Vistas;

public partial class MainWindow : Window
{
    private static readonly CultureInfo Peru = new("es-PE");

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => PintarBarraTitulo();

        // Fecha y hora del encabezado.
        MostrarFechaHora();
        var reloj = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        reloj.Tick += (_, _) => MostrarFechaHora();
        reloj.Start();

        // Tras agregar una línea, el cursor vuelve al campo Producto (T-17).
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is MainViewModel vm)
                vm.Venta.LineaAgregada += _ => CampoProducto.Focus();
        };
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private BuscadorProductoViewModel? Buscador => Vm?.Buscador;

    // Diálogo Nuevo producto (HU-01, T-13): al guardar, el producto queda
    // elegido en el campo Producto y las otras PC reciben el aviso.
    private void NuevoProducto_Click(object sender, RoutedEventArgs e)
    {
        var vm = Vm?.CrearNuevoProducto();
        if (vm is null)
            return;

        var dialogo = new NuevoProductoWindow(vm) { Owner = this };
        if (dialogo.ShowDialog() == true && vm.Guardado is not null)
            Vm!.ProductoRegistrado(vm.Guardado);
        CampoProducto.Focus();
    }

    // Teclado del campo Producto: flecha abajo entra a la lista, Enter elige
    // la primera sugerencia y Esc cierra la lista. Así se vende sin mouse.
    private void CampoProducto_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var buscador = Buscador;
        if (buscador is null || !buscador.MostrarSugerencias)
            return;

        switch (e.Key)
        {
            case Key.Down:
                EnfocarSugerencia();
                e.Handled = true;
                break;
            case Key.Enter:
                buscador.Elegir(buscador.Seleccionado);
                CampoCantidad.Focus();
                e.Handled = true;
                break;
            case Key.Escape:
                buscador.CerrarCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void ListaSugerencias_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var buscador = Buscador;
        if (buscador is null)
            return;

        if (e.Key == Key.Enter)
        {
            buscador.Elegir(buscador.Seleccionado);
            CampoCantidad.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            buscador.CerrarCommand.Execute(null);
            CampoProducto.Focus();
            e.Handled = true;
        }
    }

    private void ListaSugerencias_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        Buscador?.Elegir(Buscador.Seleccionado);
        CampoCantidad.Focus();
    }

    // Cantidad: Enter agrega la línea al detalle (criterio 2 de HU-02);
    // las flechas arriba/abajo suben o bajan la cantidad de uno en uno.
    private void CampoCantidad_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is null)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                Vm.Venta.Agregar();
                break;
            case Key.Up:
                CambiarCantidad(+1);
                break;
            case Key.Down:
                CambiarCantidad(-1);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void SubirCantidad_Click(object sender, RoutedEventArgs e) => CambiarCantidad(+1);

    private void BajarCantidad_Click(object sender, RoutedEventArgs e) => CambiarCantidad(-1);

    private void CambiarCantidad(int paso)
    {
        var venta = Vm?.Venta;
        if (venta is null)
            return;
        NuevoProductoViewModel.IntentarLeerPrecio(venta.CantidadTexto, out var actual);
        venta.CantidadTexto = Math.Max(1, Math.Floor(actual) + paso).ToString("0", CultureInfo.InvariantCulture);
        CampoCantidad.CaretIndex = CampoCantidad.Text.Length;
    }

    // Barra de título crema como el resto de la ventana (Windows 11; en
    // Windows 10 la llamada no hace nada y queda la barra del sistema).
    private void PintarBarraTitulo()
    {
        var ventana = new WindowInteropHelper(this).Handle;
        var fondo = 0x00DFE8ED; // #EDE8DF en formato 0x00BBGGRR
        var texto = 0x001D222A; // #2A221D
        DwmSetWindowAttribute(ventana, DwmwaCaptionColor, ref fondo, sizeof(int));
        DwmSetWindowAttribute(ventana, DwmwaTextColor, ref texto, sizeof(int));
    }

    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int atributo, ref int valor, int tamano);

    private void MostrarFechaHora()
    {
        var ahora = DateTime.Now;
        Fecha.Text = ahora.ToString("d MMM yyyy", Peru);
        Hora.Text = ahora.ToString("HH:mm", Peru);
    }

    private void CampoCantidad_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        CampoCantidad.SelectAll();

    // Esc fuera de la lista de sugerencias: descartar la venta en curso.
    private void Ventana_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelarVenta_Click(sender, e);
            e.Handled = true;
        }
    }

    private void CancelarVenta_Click(object sender, RoutedEventArgs e)
    {
        var venta = Vm?.Venta;
        if (venta is null || venta.Detalle.Count == 0)
            return;

        var respuesta = MessageBox.Show(this, "¿Descartar la venta en curso?", "Cancelar venta",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (respuesta == MessageBoxResult.Yes)
        {
            venta.Cancelar();
            Buscador?.Limpiar();
            CampoProducto.Focus();
        }
    }

    private void EnfocarSugerencia()
    {
        if (ListaSugerencias.Items.Count == 0)
            return;
        if (ListaSugerencias.SelectedIndex < 0)
            ListaSugerencias.SelectedIndex = 0;
        ListaSugerencias.UpdateLayout();
        if (ListaSugerencias.ItemContainerGenerator.ContainerFromIndex(ListaSugerencias.SelectedIndex) is ListBoxItem item)
            item.Focus();
    }
}
