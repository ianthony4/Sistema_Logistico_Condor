using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;

namespace Condor.Presentacion.ViewModels;

/// <summary>Resultado de una vuelta de envío de guías pendientes.</summary>
public sealed record ResultadoEnvioGuias(int Aceptadas, int Rechazadas, int Pendientes, string? Falla = null)
{
    public static ResultadoEnvioGuias Nada { get; } = new(0, 0, 0);

    public string Resumen => (Aceptadas, Rechazadas, Pendientes, Falla) switch
    {
        (0, 0, 0, null) => "No había guías pendientes.",
        _ when Falla is not null => $"Guías: {Aceptadas} aceptadas; las demás siguen pendientes: {Falla}",
        _ => $"Guías: {Aceptadas} aceptadas, {Rechazadas} rechazadas, {Pendientes} en proceso.",
    };
}

/// <summary>
/// Envía las guías de remisión a la SUNAT y guarda la respuesta (HU-10, T-94):
/// al emitir y, para las pendientes, en cada vuelta del reenvío (consulta su
/// ticket o las envía de nuevo). Nunca deja de registrar la guía: sin
/// internet o sin configurar, queda pendiente con el motivo.
/// </summary>
public sealed class EnvioGuias
{
    private readonly IEnvioGuiaSunat? _envio;
    private readonly IGuiaRemisionRepositorio _guias;
    private readonly IEmpresaRepositorio _empresas;
    private Empresa? _empresa;

    /// <param name="envio">API de guías; null si esta PC no la tiene configurada.</param>
    public EnvioGuias(IEnvioGuiaSunat? envio, IGuiaRemisionRepositorio guias, IEmpresaRepositorio empresas)
    {
        _envio = envio;
        _guias = guias;
        _empresas = empresas;
    }

    public bool Configurado => _envio is not null;

    /// <summary>
    /// Envía la guía (completa, con su detalle) y anota la respuesta. Devuelve
    /// la respuesta, o null si no llegó a la SUNAT (el motivo queda en la guía).
    /// Lanza SinConexionException si no responde la PC principal.
    /// </summary>
    public async Task<RespuestaSunat?> EnviarAsync(GuiaRemision guia, CancellationToken ct = default)
    {
        if (_envio is null)
        {
            await AnotarFallaAsync(guia, "La API de guías no está configurada en esta PC (ClientId y ClientSecret de condor.json).", ct);
            return null;
        }

        try
        {
            _empresa ??= await _empresas.ObtenerAsync(ct);
            var respuesta = await _envio.EnviarAsync(_empresa, guia, ct);
            await _guias.RegistrarRespuestaAsync(guia.Id, respuesta, ct);
            guia.EstadoSunat = respuesta.Estado;
            guia.MensajeSunat = respuesta.Descripcion;
            guia.Ticket = respuesta.Ticket ?? guia.Ticket;
            return respuesta;
        }
        catch (SinConexionException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sin internet, certificado vencido...: la guía es válida para el traslado y queda pendiente.
            await AnotarFallaAsync(guia, ex.Message, ct);
            return null;
        }
    }

    /// <summary>
    /// Una vuelta: consulta o reenvía las guías pendientes, de la más antigua a
    /// la más reciente. Si se corta internet, se detiene.
    /// </summary>
    public async Task<ResultadoEnvioGuias> ReenviarPendientesAsync(CancellationToken ct = default)
    {
        if (_envio is null)
            return ResultadoEnvioGuias.Nada;

        int aceptadas = 0, rechazadas = 0, pendientes = 0;
        foreach (var resumen in await _guias.ListarPendientesAsync(ct: ct))
        {
            var guia = await _guias.ObtenerAsync(resumen.Id, ct);
            if (guia is null)
                continue;
            var respuesta = await EnviarAsync(guia, ct);
            switch (respuesta?.Estado)
            {
                case null:
                    return new ResultadoEnvioGuias(aceptadas, rechazadas, pendientes, guia.MensajeSunat);
                case EstadoSunat.Aceptado or EstadoSunat.Observado:
                    aceptadas++;
                    break;
                case EstadoSunat.Rechazado:
                    rechazadas++;
                    break;
                default:
                    pendientes++;
                    break;
            }
        }
        return new ResultadoEnvioGuias(aceptadas, rechazadas, pendientes);
    }

    private async Task AnotarFallaAsync(GuiaRemision guia, string motivo, CancellationToken ct)
    {
        guia.EstadoSunat = EstadoSunat.Pendiente;
        guia.MensajeSunat = motivo;
        await _guias.RegistrarFallaAsync(guia.Id, motivo, ct);
    }
}
