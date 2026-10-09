using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// ViewModel de la ventana principal (pantalla Nueva venta).
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private string _mensaje = "Listo.";

    public MainViewModel()
    {
        ModuloPendienteCommand = new RelayCommand(
            p => Mensaje = $"El módulo {p} se implementará en un próximo sprint.");
    }

    public string Titulo => "Ferretería Cóndor Majes - Nueva venta";

    public string Terminal { get; private set; } = "-";

    public string Usuario { get; private set; } = "-";

    /// <summary>Texto de la barra de estado.</summary>
    public string Mensaje
    {
        get => _mensaje;
        set => SetProperty(ref _mensaje, value);
    }

    /// <summary>Atiende los botones F2 a F9 de módulos que aún no existen.</summary>
    public RelayCommand ModuloPendienteCommand { get; }
}
