using System.Collections.ObjectModel;
using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Dominio.Ventas;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Ventana Guías de remisión (F8, HU-10, T-94): las guías con su estado ante
/// la SUNAT, Nueva guía, reimprimir y enviar ya las pendientes. Al emitir una
/// guía se imprime y se envía a la SUNAT en el acto.
/// </summary>
public sealed class GuiasViewModel : ObservableObject
{
    public const int Cantidad = 200;

    private readonly IGuiaRemisionRepositorio _guias;
    private readonly EnvioGuias _envio;
    private readonly ImpresionViewModel? _impresion;
    private readonly Func<NuevaGuiaViewModel> _nuevaGuia;
    private readonly Func<DateOnly> _hoy;
    private DateTime? _desde;
    private DateTime? _hasta;
    private string _texto = string.Empty;
    private GuiaRemision? _seleccionada;
    private string? _mensaje;

    /// <param name="impresion">Ticketera; null si esta PC no imprime.</param>
    /// <param name="hoy">Fecha de hoy en Perú; por defecto la del reloj de la PC.</param>
    public GuiasViewModel(IGuiaRemisionRepositorio guias, EnvioGuias envio, ImpresionViewModel? impresion,
        Func<NuevaGuiaViewModel> nuevaGuia, Func<DateOnly>? hoy = null)
    {
        _guias = guias;
        _envio = envio;
        _impresion = impresion;
        _nuevaGuia = nuevaGuia;
        _hoy = hoy ?? (() => EmisionComprobante.FechaPeru(DateTimeOffset.Now));
        var dia = _hoy();
        _desde = dia.AddDays(-30).ToDateTime(TimeOnly.MinValue);
        _hasta = dia.ToDateTime(TimeOnly.MinValue);

        BuscarCommand = new AsyncRelayCommand(BuscarAsync);
        ReimprimirCommand = new AsyncRelayCommand(ReimprimirAsync, () => Seleccionada is not null && _impresion is not null);
        EnviarPendientesCommand = new AsyncRelayCommand(EnviarPendientesAsync, () => _envio.Configurado && PendientesSunat > 0);
    }

    public event Action? ConexionPerdida;

    public ObservableCollection<GuiaRemision> Guias { get; } = new();

    public DateTime? Desde
    {
        get => _desde;
        set => SetProperty(ref _desde, value?.Date);
    }

    public DateTime? Hasta
    {
        get => _hasta;
        set => SetProperty(ref _hasta, value?.Date);
    }

    /// <summary>Número (T001-5), DNI o RUC del destinatario, o parte de su nombre.</summary>
    public string Texto
    {
        get => _texto;
        set => SetProperty(ref _texto, value ?? string.Empty);
    }

    public GuiaRemision? Seleccionada
    {
        get => _seleccionada;
        set
        {
            if (SetProperty(ref _seleccionada, value))
                ReimprimirCommand.NotificarCambio();
        }
    }

    public int PendientesSunat => Guias.Count(g => g.EstadoSunat == EstadoSunat.Pendiente);

    public bool EnvioConfigurado => _envio.Configurado;

    public string? Mensaje
    {
        get => _mensaje;
        private set => SetProperty(ref _mensaje, value);
    }

    public AsyncRelayCommand BuscarCommand { get; }

    public AsyncRelayCommand ReimprimirCommand { get; }

    /// <summary>Envía o consulta ya las guías pendientes, sin esperar al reenvío automático.</summary>
    public AsyncRelayCommand EnviarPendientesCommand { get; }

    public NuevaGuiaViewModel CrearNuevaGuia()
    {
        var vm = _nuevaGuia();
        vm.ConexionPerdida += () => ConexionPerdida?.Invoke();
        return vm;
    }

    public async Task BuscarAsync()
    {
        var desde = Desde is { } d ? DateOnly.FromDateTime(d) : (DateOnly?)null;
        var hasta = Hasta is { } h ? DateOnly.FromDateTime(h) : (DateOnly?)null;
        if (desde > hasta)
        {
            Mensaje = "La fecha Desde es posterior a Hasta: corríjala y pulse Buscar.";
            return;
        }

        try
        {
            var encontradas = await _guias.BuscarAsync(new FiltroGuias(desde, hasta, Texto, Cantidad));
            Guias.Clear();
            foreach (var g in encontradas)
                Guias.Add(g);
            Seleccionada = Guias.FirstOrDefault();
            OnPropertyChanged(nameof(PendientesSunat));
            EnviarPendientesCommand.NotificarCambio();
            Mensaje = Guias.Count switch
            {
                0 => "No hay guías con esos filtros. Pulse Nueva guía para emitir una.",
                var n => $"{(n == 1 ? "1 guía" : $"{n} guías")}" + (PendientesSunat > 0 ? $", {PendientesSunat} pendientes de la SUNAT." : "."),
            };
        }
        catch (SinConexionException)
        {
            Mensaje = "Sin conexión con la PC principal: no se pueden listar las guías.";
            ConexionPerdida?.Invoke();
        }
    }

    /// <summary>
    /// Guía recién emitida (HU-10, criterio 1): se imprime para que acompañe a
    /// la mercadería, se envía a la SUNAT y queda elegida en la lista.
    /// </summary>
    public async Task GuiaEmitidaAsync(GuiaRemision guia)
    {
        var partes = new List<string> { $"Guía {guia.NumeroCompleto} emitida." };
        if (_impresion is not null)
            partes.Add(await _impresion.ImprimirGuiaAsync(guia, _guias) ? "Impresa." : _impresion.Error ?? "No se imprimió.");

        try
        {
            var respuesta = await _envio.EnviarAsync(guia);
            partes.Add(respuesta?.Resumen ?? $"Pendiente de envío a la SUNAT: {guia.MensajeSunat}");
        }
        catch (SinConexionException)
        {
            partes.Add("No se pudo anotar el envío: no responde la PC principal.");
            ConexionPerdida?.Invoke();
        }

        Texto = string.Empty;
        await BuscarAsync();
        Seleccionada = Guias.FirstOrDefault(g => g.Id == guia.Id) ?? Seleccionada;
        Mensaje = string.Join(" ", partes);
    }

    private async Task ReimprimirAsync()
    {
        var guia = Seleccionada!;
        Mensaje = await _impresion!.ImprimirGuiaAsync(guia, _guias)
            ? $"{guia.NumeroCompleto} reimpresa en {_impresion.Impresora}."
            : _impresion.Error;
    }

    private async Task EnviarPendientesAsync()
    {
        Mensaje = "Enviando las guías pendientes a la SUNAT…";
        try
        {
            var resultado = await _envio.ReenviarPendientesAsync();
            await BuscarAsync();
            Mensaje = resultado.Resumen;
        }
        catch (SinConexionException)
        {
            Mensaje = "Sin conexión con la PC principal.";
            ConexionPerdida?.Invoke();
        }
    }
}
