using System.Globalization;
using System.Windows.Data;

namespace Condor.Escritorio.Convertidores;

/// <summary>
/// True si todos los valores se escriben igual (por ejemplo, el módulo abierto
/// y el CommandParameter del botón del menú): así se pinta el botón activo.
/// </summary>
public sealed class IgualesConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length > 1 && values.All(v => v?.ToString() == values[0]?.ToString());

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
