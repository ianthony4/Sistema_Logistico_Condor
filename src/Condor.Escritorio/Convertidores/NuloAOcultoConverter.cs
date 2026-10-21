using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Condor.Escritorio.Convertidores;

/// <summary>
/// Oculta un elemento cuando el valor enlazado es null o texto vacío
/// (por ejemplo, el mensaje de error cuando no hay error).
/// </summary>
public sealed class NuloAOcultoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
