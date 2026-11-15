using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Dominio.Ventas;
using Condor.Pruebas.Infraestructura;
using Condor.Sunat;

namespace Condor.Pruebas.Sunat;

/// <summary>Reenvío automático de los pendientes (HU-17, T-56), con la SUNAT falsa y repositorios en memoria.</summary>
public class ReenvioPendientesTests
{
    private sealed record Escenario(ReenvioPendientes Reenvio, EnvioSunatFalso Sunat, EnvioSunatRepositorioEnMemoria Registro,
        ComprobanteRepositorioEnMemoria Comprobantes);

    private static Escenario Crear(IEnvioSunat? sunat = null, string terminal = "PC-1", int lote = 20)
    {
        var falsa = new EnvioSunatFalso();
        var registro = new EnvioSunatRepositorioEnMemoria();
        var comprobantes = new ComprobanteRepositorioEnMemoria();
        var reenvio = new ReenvioPendientes(sunat ?? falsa, registro, comprobantes, new EmpresaRepositorioEnMemoria(),
            terminal, TimeSpan.Zero, lote);
        return new Escenario(reenvio, falsa, registro, comprobantes);
    }

    /// <summary>Boletas emitidas sin internet: quedaron pendientes con un intento fallido.</summary>
    private static async Task<List<Comprobante>> PendientesAsync(Escenario e, int cantidad)
    {
        var emitidas = new List<Comprobante>();
        for (var i = 0; i < cantidad; i++)
        {
            var boleta = EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.Boleta, new List<DetalleComprobante>
            {
                new() { ProductoId = 1, Descripcion = "Cemento Portland tipo I x 42.5 kg", Unidad = "BLS", Cantidad = 2, PrecioUnitario = 32.50m },
            }));
            boleta.TerminalId = 1;
            emitidas.Add(await e.Comprobantes.EmitirAsync(boleta));
            await e.Registro.RegistrarFallaAsync(boleta.Id, "No hay conexión a internet.");
        }
        return emitidas;
    }

    [Fact]
    public async Task Vuelve_internet_y_los_pendientes_se_envian_del_mas_antiguo_al_mas_reciente()
    {
        var e = Crear();
        var emitidas = await PendientesAsync(e, 3);

        var resultado = await e.Reenvio.ReenviarAsync();

        Assert.Equal(emitidas.Select(c => c.Id), e.Sunat.Enviados.Select(c => c.Id));
        Assert.Equal(3, resultado.Aceptados);
        Assert.All(emitidas, c => Assert.Equal(EstadoSunat.Aceptado, e.Registro.Estado(c.Id)));
        Assert.Empty(await e.Registro.ListarPendientesAsync());
        Assert.Equal("Reenvío a la SUNAT: 3 aceptados.", resultado.Resumen);
    }

    [Fact]
    public async Task Sin_pendientes_no_envia_nada()
    {
        var e = Crear();

        var resultado = await e.Reenvio.ReenviarAsync();

        Assert.Same(ResultadoReenvio.Nada, resultado);
        Assert.Null(resultado.Resumen);
        Assert.Empty(e.Sunat.Enviados);
    }

    [Fact]
    public async Task Sigue_sin_internet_cuenta_un_intento_y_libera_los_demas()
    {
        var e = Crear();
        var emitidas = await PendientesAsync(e, 3);
        e.Sunat.SinInternet = true;

        var resultado = await e.Reenvio.ReenviarAsync();

        Assert.True(resultado.Cortado);
        Assert.Equal(3, resultado.SiguenPendientes);
        Assert.Equal(2, e.Registro.Intentos[emitidas[0].Id]); // al emitir y ahora
        Assert.Equal(1, e.Registro.Intentos[emitidas[1].Id]); // no se gastó un intento
        Assert.Empty(e.Registro.Reservados);
        Assert.Contains("Se detuvo: No hay conexión a internet.", resultado.Resumen);
    }

    [Fact]
    public async Task Si_internet_se_corta_a_mitad_quedan_pendientes_solo_los_que_faltaban()
    {
        var sunat = new SunatQueSeCae(despuesDe: 2);
        var e = Crear(sunat);
        var emitidas = await PendientesAsync(e, 5);

        var resultado = await e.Reenvio.ReenviarAsync();

        Assert.Equal(2, resultado.Aceptados);
        Assert.Equal(3, resultado.SiguenPendientes);
        Assert.Equal(EstadoSunat.Aceptado, e.Registro.Estado(emitidas[1].Id));
        Assert.Equal(EstadoSunat.Pendiente, e.Registro.Estado(emitidas[2].Id));
        Assert.Empty(e.Registro.Reservados);

        // En la vuelta siguiente, con internet, salen los que faltaban.
        sunat.DespuesDe = int.MaxValue;
        var siguiente = await e.Reenvio.ReenviarAsync();
        Assert.Equal(3, siguiente.Aceptados);
        Assert.Empty(await e.Registro.ListarPendientesAsync());
    }

    [Fact]
    public async Task El_rechazo_queda_registrado_y_no_se_vuelve_a_enviar()
    {
        var e = Crear();
        var emitidas = await PendientesAsync(e, 2);
        e.Sunat.Respuesta = EnvioSunatFalso.Rechazada();

        var resultado = await e.Reenvio.ReenviarAsync();
        var otra = await e.Reenvio.ReenviarAsync();

        Assert.Equal(2, resultado.Rechazados);
        Assert.Equal(EstadoSunat.Rechazado, e.Registro.Estado(emitidas[0].Id));
        Assert.Same(ResultadoReenvio.Nada, otra);
        Assert.Equal(2, e.Sunat.Enviados.Count);
    }

    [Fact]
    public async Task Un_certificado_vencido_detiene_la_vuelta_con_el_motivo()
    {
        var e = Crear(new SunatConCertificadoVencido());
        await PendientesAsync(e, 2);

        var resultado = await e.Reenvio.ReenviarAsync();

        Assert.True(resultado.Cortado);
        Assert.Contains("certificado", resultado.Corte);
        Assert.Equal(2, resultado.SiguenPendientes);
    }

    [Fact]
    public async Task Cada_vuelta_envia_como_maximo_el_lote()
    {
        var e = Crear(lote: 2);
        await PendientesAsync(e, 5);

        var primera = await e.Reenvio.ReenviarAsync();

        Assert.Equal(2, primera.Enviados);
        Assert.Equal(3, (await e.Registro.ListarPendientesAsync()).Count);
    }

    [Fact]
    public async Task Dos_PC_no_reenvian_el_mismo_comprobante()
    {
        var e = Crear(lote: 2);
        await PendientesAsync(e, 3);
        var sunatPc2 = new EnvioSunatFalso();
        var otraPc = new ReenvioPendientes(sunatPc2, e.Registro, e.Comprobantes, new EmpresaRepositorioEnMemoria(), "PC-2",
            TimeSpan.Zero, lote: 2);
        e.Sunat.Demora = new TaskCompletionSource();

        var pc1 = e.Reenvio.ReenviarAsync(); // aparta 2 y espera a la SUNAT
        var pc2 = await otraPc.ReenviarAsync();
        e.Sunat.Demora.SetResult();
        await pc1;

        Assert.Equal(1, pc2.Enviados);
        Assert.Equal(2, e.Sunat.Enviados.Count);
        Assert.Empty(e.Sunat.Enviados.Select(c => c.Id).Intersect(sunatPc2.Enviados.Select(c => c.Id)));
    }

    /// <summary>Acepta los primeros envíos y después se queda sin internet.</summary>
    private sealed class SunatQueSeCae : IEnvioSunat
    {
        private int _enviados;

        public SunatQueSeCae(int despuesDe) => DespuesDe = despuesDe;

        public int DespuesDe { get; set; }

        public string Ambiente => "prueba";

        public Task<RespuestaSunat> EnviarAsync(Empresa empresa, Comprobante comprobante, CancellationToken ct = default)
        {
            if (_enviados >= DespuesDe)
                throw new SunatNoDisponibleException("No hay conexión a internet.");
            _enviados++;
            return Task.FromResult(EnvioSunatFalso.Aceptada());
        }
    }

    private sealed class SunatConCertificadoVencido : IEnvioSunat
    {
        public string Ambiente => "prueba";

        public Task<RespuestaSunat> EnviarAsync(Empresa empresa, Comprobante comprobante, CancellationToken ct = default) =>
            throw new CertificadoInvalidoException("El certificado digital venció el 01/11/2026: renuévelo con la entidad certificadora.");
    }
}
