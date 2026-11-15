using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;

namespace Condor.Sunat;

/// <summary>
/// Reenvío automático de los comprobantes pendientes (HU-17, T-56). En cada
/// vuelta aparta los pendientes que le tocan a esta PC (T-55), del más
/// antiguo al más reciente, los envía y guarda la respuesta. Si se corta
/// internet, se detiene sin gastar intentos en los demás y los libera para
/// la próxima vuelta.
/// </summary>
public sealed class ReenvioPendientes : IReenvioPendientes
{
    /// <summary>
    /// Tiempo mínimo desde la emisión y entre intentos: deja que el envío al
    /// emitir (HU-07) termine primero. Crece con cada intento fallido.
    /// </summary>
    public static readonly TimeSpan EsperaPorDefecto = TimeSpan.FromMinutes(2);

    /// <summary>Espera al reenviar a pedido del usuario: solo lo justo para no cruzarse con el envío al emitir.</summary>
    public static readonly TimeSpan EsperaInmediata = TimeSpan.FromSeconds(30);

    /// <summary>Comprobantes por vuelta: pocos, para que la vuelta no tarde y otra PC pueda ayudar.</summary>
    public const int LotePorDefecto = 20;

    private readonly IEnvioSunat _envio;
    private readonly IEnvioSunatRepositorio _registro;
    private readonly IComprobanteRepositorio _comprobantes;
    private readonly IEmpresaRepositorio _empresas;
    private readonly string _terminal;
    private Empresa? _empresa;

    public ReenvioPendientes(IEnvioSunat envio, IEnvioSunatRepositorio registro, IComprobanteRepositorio comprobantes,
        IEmpresaRepositorio empresas, string terminal, TimeSpan? espera = null, int lote = LotePorDefecto)
    {
        _envio = envio;
        _registro = registro;
        _comprobantes = comprobantes;
        _empresas = empresas;
        _terminal = terminal;
        Espera = espera ?? EsperaPorDefecto;
        Lote = Math.Clamp(lote, 1, 200);
    }

    public TimeSpan Espera { get; }

    public int Lote { get; }

    /// <summary>
    /// Una vuelta del reenvío. Lanza SinConexionException si no responde la
    /// PC principal (las reservas vencen solas y otra PC los retoma).
    /// </summary>
    public async Task<ResultadoReenvio> ReenviarAsync(bool inmediato = false, CancellationToken ct = default)
    {
        var espera = inmediato && EsperaInmediata < Espera ? EsperaInmediata : Espera;
        var apartados = await _registro.ReservarPendientesAsync(_terminal, espera, Lote, ct);
        if (apartados.Count == 0)
            return ResultadoReenvio.Nada;

        int enviados = 0, aceptados = 0, rechazados = 0, pendientes = 0;
        var porEnviar = new Queue<long>(apartados);
        try
        {
            _empresa ??= await _empresas.ObtenerAsync(ct);
            while (porEnviar.TryPeek(out var id))
            {
                ct.ThrowIfCancellationRequested();
                var comprobante = await _comprobantes.ObtenerAsync(id, ct);
                if (comprobante is null)
                {
                    porEnviar.Dequeue();
                    continue;
                }

                RespuestaSunat respuesta;
                try
                {
                    respuesta = await _envio.EnviarAsync(_empresa, comprobante, ct);
                }
                catch (SunatNoDisponibleException ex)
                {
                    // Sin internet: este cuenta un intento; los demás esperan a la próxima vuelta.
                    porEnviar.Dequeue();
                    await _registro.RegistrarFallaAsync(id, ex.Message, ct);
                    return new ResultadoReenvio(enviados, aceptados, rechazados, pendientes + 1 + porEnviar.Count, ex.Message);
                }
                catch (Exception ex) when (ex is not (OperationCanceledException or SinConexionException))
                {
                    // Certificado vencido o clave equivocada: fallaría igual con los demás.
                    porEnviar.Dequeue();
                    await _registro.RegistrarFallaAsync(id, ex.Message, ct);
                    return new ResultadoReenvio(enviados, aceptados, rechazados, pendientes + 1 + porEnviar.Count, ex.Message);
                }

                porEnviar.Dequeue();
                await _registro.RegistrarRespuestaAsync(id, respuesta, ct);
                comprobante.EstadoSunat = respuesta.Estado;
                comprobante.MensajeSunat = respuesta.Descripcion;
                enviados++;
                switch (respuesta.Estado)
                {
                    case EstadoSunat.Aceptado or EstadoSunat.Observado:
                        aceptados++;
                        break;
                    case EstadoSunat.Rechazado:
                        rechazados++;
                        break;
                    default:
                        pendientes++;
                        break;
                }
            }
        }
        finally
        {
            // Los que no se llegaron a enviar vuelven a quedar libres, sin sumar intentos.
            if (porEnviar.Count > 0)
            {
                try
                {
                    await _registro.LiberarAsync(_terminal, porEnviar.ToList(), CancellationToken.None);
                }
                catch (Exception)
                {
                    // Sin la PC principal no se pueden liberar: la reserva vence sola.
                }
            }
        }

        return new ResultadoReenvio(enviados, aceptados, rechazados, pendientes);
    }
}
