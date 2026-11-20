using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Aviso de respaldo vencido (HU-15, criterio 2; T-63): al abrir el sistema,
/// si el último respaldo tiene más de 24 horas (AvisoHoras de condor.json) o
/// nunca se hizo uno, la pantalla de venta pide generar uno nuevo. Se ve en
/// las tres PC porque la fecha del último respaldo está en la base central.
/// </summary>
public sealed class AvisoRespaldoViewModel : ObservableObject
{
    private readonly IRespaldoRepositorio _registro;
    private readonly Func<DateTimeOffset> _ahora;
    private string? _aviso;

    /// <param name="horas">Horas sin respaldo a partir de las que se avisa.</param>
    /// <param name="ahora">Para las pruebas; por defecto la hora de la PC.</param>
    public AvisoRespaldoViewModel(IRespaldoRepositorio registro, int horas = 24, Func<DateTimeOffset>? ahora = null)
    {
        _registro = registro;
        _ahora = ahora ?? (() => DateTimeOffset.Now);
        Horas = horas;
        DescartarCommand = new RelayCommand(() => Aviso = null);
    }

    public int Horas { get; }

    /// <summary>Texto del aviso; null si el último respaldo es reciente o el usuario lo dejó para más tarde.</summary>
    public string? Aviso
    {
        get => _aviso;
        private set => SetProperty(ref _aviso, value);
    }

    /// <summary>"Más tarde": oculta el aviso hasta la próxima vez que se abra el sistema.</summary>
    public RelayCommand DescartarCommand { get; }

    /// <summary>True si ya pasaron más de <paramref name="horas"/> horas desde el último respaldo (o nunca hubo uno).</summary>
    public static bool Vencido(Respaldo? ultimo, DateTimeOffset ahora, int horas) =>
        ultimo is null || ahora - ultimo.TerminadoEn > TimeSpan.FromHours(horas);

    /// <summary>Consulta el último respaldo y muestra el aviso si corresponde. No lanza excepciones.</summary>
    public async Task RevisarAsync()
    {
        Respaldo? ultimo;
        try
        {
            ultimo = await _registro.UltimoAsync();
        }
        catch (Exception)
        {
            // Sin la PC principal ya se ve el aviso de conexión; se revisa al volver.
            return;
        }

        var ahora = _ahora();
        if (!Vencido(ultimo, ahora, Horas))
        {
            Aviso = null;
            return;
        }

        Aviso = ultimo is null
            ? "Aún no se ha generado ningún respaldo de la base de datos. Conecte el disco externo a la PC principal y pulse Respaldo (F9)."
            : $"El último respaldo es del {ultimo.TerminadoEn.ToLocalTime():dd/MM/yyyy HH:mm} (hace {Hace(ahora - ultimo.TerminadoEn)}). " +
              "Genere uno nuevo: conecte el disco externo a la PC principal y pulse Respaldo (F9).";
    }

    /// <summary>Se generó un respaldo en esta PC: el aviso ya no corresponde.</summary>
    public void RespaldoGenerado(Respaldo respaldo)
    {
        if (!Vencido(respaldo, _ahora(), Horas))
            Aviso = null;
    }

    private static string Hace(TimeSpan tiempo) => tiempo.TotalDays >= 2
        ? $"{(int)tiempo.TotalDays} días"
        : $"{(int)tiempo.TotalHours} horas";
}
