using System.Windows.Input;

namespace Condor.Presentacion.Mvvm;

/// <summary>
/// Comando asíncrono: deshabilita el botón mientras la operación está en curso
/// para que no se dispare dos veces (por ejemplo, un doble clic en Guardar).
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _ejecutar;
    private readonly Func<bool>? _puedeEjecutar;
    private bool _enCurso;

    public AsyncRelayCommand(Func<Task> ejecutar, Func<bool>? puedeEjecutar = null)
    {
        _ejecutar = ejecutar;
        _puedeEjecutar = puedeEjecutar;
    }

    public event EventHandler? CanExecuteChanged;

    public bool EnCurso => _enCurso;

    public bool CanExecute(object? parametro) => !_enCurso && (_puedeEjecutar?.Invoke() ?? true);

    public async void Execute(object? parametro) => await EjecutarAsync();

    public async Task EjecutarAsync()
    {
        if (!CanExecute(null))
            return;

        _enCurso = true;
        NotificarCambio();
        try
        {
            await _ejecutar();
        }
        finally
        {
            _enCurso = false;
            NotificarCambio();
        }
    }

    public void NotificarCambio() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
