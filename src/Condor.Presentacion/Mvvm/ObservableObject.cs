using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Condor.Presentacion.Mvvm;

/// <summary>
/// Base de los ViewModel: notifica a la vista cuando cambia una propiedad.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propiedad = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));

    /// <summary>
    /// Asigna el valor al campo y notifica solo si realmente cambió.
    /// </summary>
    protected bool SetProperty<T>(ref T campo, T valor, [CallerMemberName] string? propiedad = null)
    {
        if (EqualityComparer<T>.Default.Equals(campo, valor))
            return false;

        campo = valor;
        OnPropertyChanged(propiedad);
        return true;
    }
}
