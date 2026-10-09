using System.Windows.Input;

namespace Condor.Presentacion.Mvvm;

/// <summary>
/// Comando síncrono para enlazar botones y atajos de teclado a un ViewModel.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _ejecutar;
    private readonly Func<object?, bool>? _puedeEjecutar;

    public RelayCommand(Action ejecutar, Func<bool>? puedeEjecutar = null)
        : this(_ => ejecutar(), puedeEjecutar is null ? null : _ => puedeEjecutar())
    {
    }

    public RelayCommand(Action<object?> ejecutar, Func<object?, bool>? puedeEjecutar = null)
    {
        _ejecutar = ejecutar;
        _puedeEjecutar = puedeEjecutar;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parametro) => _puedeEjecutar?.Invoke(parametro) ?? true;

    public void Execute(object? parametro) => _ejecutar(parametro);

    public void NotificarCambio() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
