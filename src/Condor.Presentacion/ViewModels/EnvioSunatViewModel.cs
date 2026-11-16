using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>
/// Envío a la SUNAT al emitir (HU-07, T-47). Corre en segundo plano mientras
/// sale el ticket: si no hay internet o la SUNAT no responde, el comprobante
/// queda pendiente y la venta siguiente no espera (criterio 2). El resultado
/// se muestra en la pantalla de venta (criterio 1).
/// </summary>
public sealed class EnvioSunatViewModel : ObservableObject
{
    private readonly IEnvioSunat? _envio;
    private readonly IEmpresaRepositorio _empresas;
    private readonly IEnvioSunatRepositorio _registro;
    private Empresa? _empresa;
    private Comprobante? _ultimo;
    private string? _estado;
    private string? _mensaje;
    private bool _enviando;

    /// <param name="envio">Servicio de la SUNAT; null si esta PC no la tiene configurada (todo queda pendiente).</param>
    public EnvioSunatViewModel(IEnvioSunat? envio, IEmpresaRepositorio empresas, IEnvioSunatRepositorio registro)
    {
        _envio = envio;
        _empresas = empresas;
        _registro = registro;
    }

    /// <summary>No se pudo guardar la respuesta porque no responde la PC principal.</summary>
    public event Action? ConexionPerdida;

    public bool Configurada => _envio is not null;

    /// <summary>"SUNAT beta", "SUNAT producción" o "SUNAT sin configurar", para el pie de la pantalla.</summary>
    public string Ambiente => _envio is null ? "SUNAT sin configurar" : $"SUNAT {_envio.Ambiente}";

    /// <summary>Último comprobante enviado (o que se intentó enviar) desde esta PC.</summary>
    public Comprobante? Ultimo
    {
        get => _ultimo;
        private set => SetProperty(ref _ultimo, value);
    }

    /// <summary>Estado del último: PENDIENTE, ACEPTADO, OBSERVADO o RECHAZADO; null antes del primero.</summary>
    public string? Estado
    {
        get => _estado;
        private set => SetProperty(ref _estado, value);
    }

    /// <summary>Aviso para la pantalla de venta; null antes de la primera emisión.</summary>
    public string? Mensaje
    {
        get => _mensaje;
        private set => SetProperty(ref _mensaje, value);
    }

    public bool Enviando
    {
        get => _enviando;
        private set => SetProperty(ref _enviando, value);
    }

    /// <summary>
    /// Envía el comprobante y guarda la respuesta en la base central. Nunca
    /// lanza excepciones: todo problema deja el comprobante pendiente con su
    /// motivo. Devuelve la respuesta de la SUNAT o null si no llegó a ella.
    /// </summary>
    public async Task<RespuestaSunat?> EnviarAsync(Comprobante comprobante)
    {
        Ultimo = comprobante;
        var numero = comprobante.NumeroCompleto;

        if (_envio is null)
        {
            await AnotarPendienteAsync(comprobante, "La SUNAT no está configurada en esta PC (sección Sunat de condor.json).");
            return null;
        }

        Enviando = true;
        Estado = EstadoSunat.Pendiente;
        Mensaje = $"Enviando {numero} a la SUNAT…";
        try
        {
            _empresa ??= await _empresas.ObtenerAsync();
            var respuesta = await _envio.EnviarAsync(_empresa, comprobante);

            comprobante.EstadoSunat = respuesta.Estado;
            comprobante.MensajeSunat = respuesta.Descripcion;
            Estado = respuesta.Estado;
            Mensaje = $"{numero}: {respuesta.Resumen}";
            await GuardarAsync(() => _registro.RegistrarRespuestaAsync(comprobante.Id, respuesta));
            return respuesta;
        }
        catch (SunatNoDisponibleException ex)
        {
            await AnotarPendienteAsync(comprobante, ex.Message);
            return null;
        }
        catch (SinConexionException)
        {
            // No se pudo leer la empresa: tampoco se puede anotar nada ahora.
            Estado = EstadoSunat.Pendiente;
            Mensaje = $"{numero} quedó pendiente de envío a la SUNAT: no responde la PC principal.";
            ConexionPerdida?.Invoke();
            return null;
        }
        catch (Exception ex)
        {
            // Certificado vencido, clave del certificado equivocada...: se avisa y queda pendiente.
            await AnotarPendienteAsync(comprobante, ex.Message);
            return null;
        }
        finally
        {
            Enviando = false;
        }
    }

    private async Task AnotarPendienteAsync(Comprobante comprobante, string motivo)
    {
        comprobante.EstadoSunat = EstadoSunat.Pendiente;
        comprobante.MensajeSunat = motivo;
        Estado = EstadoSunat.Pendiente;
        Mensaje = $"{comprobante.NumeroCompleto} quedó pendiente de envío a la SUNAT: {motivo} El ticket es válido y puede seguir vendiendo.";
        await GuardarAsync(() => _registro.RegistrarFallaAsync(comprobante.Id, motivo));
    }

    private async Task GuardarAsync(Func<Task> guardar)
    {
        try
        {
            await guardar();
        }
        catch (SinConexionException)
        {
            ConexionPerdida?.Invoke();
        }
        catch (Exception)
        {
            // El aviso en pantalla ya está; no se interrumpe la venta por no poder anotarlo.
        }
    }
}
