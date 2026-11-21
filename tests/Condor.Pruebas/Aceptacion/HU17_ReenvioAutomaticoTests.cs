using Condor.Datos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;
using Condor.Sunat;
using Npgsql;

namespace Condor.Pruebas.Aceptacion;

/// <summary>
/// HU-17: reenvío automático de pendientes a la SUNAT.
/// Criterio 1: si hay comprobantes pendientes por falta de internet y vuelve
/// la conexión, el sistema los reenvía solos, del más antiguo al más
/// reciente, y guarda su estado y su CDR sin que el usuario haga nada.
/// Criterio 2: si dos PC tienen la SUNAT configurada y reenvían a la vez,
/// cada comprobante se envía una sola vez.
/// Criterio 3: si un pendiente lleva más de 2 días, la pantalla de venta
/// avisa que está por vencer el plazo de 3 días de la SUNAT.
/// </summary>
/// <remarks>Usa la base de pruebas y la SUNAT falsa.</remarks>
[Collection(BaseDatos.Nombre)]
public class HU17_ReenvioAutomaticoTests
{
    private readonly BaseDatosFixture _bd;

    public HU17_ReenvioAutomaticoTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task CA1_al_volver_internet_los_pendientes_se_envian_solos_del_mas_antiguo_al_mas_reciente()
    {
        await PendientesDePrueba.ApartarLosDeOtrasPruebasAsync(_bd.Conexiones);
        var (vm, sunat) = await PantallaAsync();
        sunat.SinInternet = true;
        var primera = await VenderAsync(vm);
        var segunda = await VenderAsync(vm);
        Assert.Equal(EstadoSunat.Pendiente, await EstadoAsync(primera.Id));

        sunat.SinInternet = false;
        var resultado = await vm.ReenvioSunat!.ReenviarAsync(); // lo que hace el temporizador

        Assert.Equal(2, resultado!.Aceptados);
        Assert.Equal(new[] { primera.Id, segunda.Id }, sunat.Enviados.Select(c => c.Id));
        Assert.Equal(EstadoSunat.Aceptado, await EstadoAsync(primera.Id));
        Assert.Equal(EstadoSunat.Aceptado, await EstadoAsync(segunda.Id));
        Assert.True(await TieneCdrAsync(segunda.Id));
        Assert.Equal("Reenvío a la SUNAT: 2 aceptados.", vm.Mensaje);
    }

    [FactConBD]
    public async Task CA1_mientras_sigue_sin_internet_quedan_pendientes_y_no_se_gastan_intentos()
    {
        await PendientesDePrueba.ApartarLosDeOtrasPruebasAsync(_bd.Conexiones);
        var (vm, sunat) = await PantallaAsync();
        sunat.SinInternet = true;
        var primera = await VenderAsync(vm);
        var segunda = await VenderAsync(vm);

        var resultado = await vm.ReenvioSunat!.ReenviarAsync();

        Assert.True(resultado!.Cortado);
        Assert.Equal(EstadoSunat.Pendiente, await EstadoAsync(primera.Id));
        Assert.Equal(2, await IntentosAsync(primera.Id)); // al emitir y en el reenvío
        Assert.Equal(1, await IntentosAsync(segunda.Id)); // solo al emitir
        Assert.Null(await PendientesDePrueba.ReservadoPorAsync(_bd.Conexiones, segunda.Id));
        Assert.NotNull(vm.ReenvioSunat.Aviso);
    }

    [FactConBD]
    public async Task CA1_la_factura_pendiente_tambien_se_reenvia()
    {
        await PendientesDePrueba.ApartarLosDeOtrasPruebasAsync(_bd.Conexiones);
        var (vm, sunat) = await PantallaAsync();
        var ruc = DatosPrueba.RucNuevo();
        await new ClienteRepositorio(_bd.Conexiones).RegistrarAsync(DatosPrueba.ClienteRuc(ruc));
        sunat.SinInternet = true;
        vm.Emision!.EsFactura = true;
        vm.Cliente!.Documento = ruc;
        await vm.Cliente.BuscarCommand.EjecutarAsync();
        var factura = await VenderAsync(vm);
        Assert.Equal("F001", factura.SerieCodigo);

        sunat.SinInternet = false;
        await vm.ReenvioSunat!.ReenviarAsync();

        Assert.Equal(EstadoSunat.Aceptado, await EstadoAsync(factura.Id));
    }

    [FactConBD]
    public async Task CA1_el_reenvio_no_se_cruza_con_el_envio_de_una_venta_recien_emitida()
    {
        await PendientesDePrueba.ApartarLosDeOtrasPruebasAsync(_bd.Conexiones);
        var (vm, sunat) = await PantallaAsync();
        sunat.Demora = new TaskCompletionSource(); // la SUNAT aún no responde al envío de la venta
        var boleta = await VenderAsync(vm, esperarSunat: false);
        var comprobantes = new ComprobanteRepositorio(_bd.Conexiones);
        var conEspera = new ReenvioPendientes(new EnvioSunatFalso(), new EnvioSunatRepositorio(_bd.Conexiones), comprobantes,
            new EmpresaRepositorio(_bd.Conexiones), "PC-2"); // espera por defecto: 2 minutos

        var resultado = await conEspera.ReenviarAsync();

        Assert.Same(ResultadoReenvio.Nada, resultado);
        sunat.Demora.SetResult();
        await vm.UltimoEnvioSunat;
        Assert.Equal(EstadoSunat.Aceptado, await EstadoAsync(boleta.Id));
        Assert.Single(sunat.Enviados);
    }

    [FactConBD]
    public async Task CA2_dos_PC_que_reenvian_a_la_vez_envian_cada_comprobante_una_sola_vez()
    {
        await PendientesDePrueba.ApartarLosDeOtrasPruebasAsync(_bd.Conexiones);
        var (vm, sunat) = await PantallaAsync();
        sunat.SinInternet = true;
        var pendientes = new List<long>();
        for (var i = 0; i < 6; i++)
            pendientes.Add((await VenderAsync(vm)).Id);

        await using var local = new TresPC();
        var sunatPc = new[] { new EnvioSunatFalso(), new EnvioSunatFalso() };
        var reenvios = Enumerable.Range(0, 2).Select(i => new ReenvioPendientes(sunatPc[i],
            new EnvioSunatRepositorio(local.Pc[i]), new ComprobanteRepositorio(local.Pc[i]), new EmpresaRepositorio(local.Pc[i]),
            $"PC-{i + 1}", TimeSpan.Zero, lote: 4)).ToArray();

        // Las dos PC dan sus vueltas a la vez hasta enviar los 6: ninguno sale dos veces.
        for (var vuelta = 0; vuelta < 4 && sunatPc.Sum(s => s.Enviados.Count) < pendientes.Count; vuelta++)
            await Task.WhenAll(reenvios.Select(r => r.ReenviarAsync()));

        var enviados = sunatPc.SelectMany(s => s.Enviados).Select(c => c.Id).ToList();
        Assert.Equal(enviados.Count, enviados.Distinct().Count());
        Assert.Equal(pendientes.Order(), enviados.Order());
        foreach (var id in pendientes)
            Assert.Equal(EstadoSunat.Aceptado, await EstadoAsync(id));
    }

    [FactConBD]
    public async Task CA3_un_pendiente_de_hace_mas_de_dos_dias_se_avisa_en_la_pantalla_de_venta()
    {
        await PendientesDePrueba.ApartarLosDeOtrasPruebasAsync(_bd.Conexiones);
        var (vm, sunat) = await PantallaAsync();
        sunat.SinInternet = true;
        var boleta = await VenderAsync(vm);
        await PendientesDePrueba.AtrasarAsync(_bd.Conexiones, boleta.Id, TimeSpan.FromDays(2.5));

        await vm.ReenvioSunat!.ActualizarAsync();

        Assert.True(vm.ReenvioSunat.PorVencer);
        Assert.Contains("vence el plazo de 3 días", vm.ReenvioSunat.Aviso);
    }

    /// <summary>Pantalla Nueva venta contra la base de pruebas: la SUNAT falsa al emitir y al reenviar.</summary>
    private async Task<(MainViewModel Vm, EnvioSunatFalso Sunat)> PantallaAsync()
    {
        var pc = _bd.Conexiones;
        var comprobantes = new ComprobanteRepositorio(pc);
        var registro = new EnvioSunatRepositorio(pc);
        var sunat = new EnvioSunatFalso();
        var reenvio = new ReenvioPendientes(sunat, registro, comprobantes, new EmpresaRepositorio(pc), "PC-HU17", TimeSpan.Zero);
        var vm = new MainViewModel(new ProductoRepositorio(pc), new ClienteRepositorio(pc), comprobantes)
        {
            Impresion = new ImpresionViewModel(new ImpresoraFalsa(), new EmpresaRepositorio(pc), comprobantes),
            EnvioSunat = new EnvioSunatViewModel(sunat, new EmpresaRepositorio(pc), registro),
            ReenvioSunat = new ReenvioSunatViewModel(reenvio, registro),
        };
        vm.Buscador!.Espera = TimeSpan.Zero;
        vm.IniciarSesion(new Sesion(await new TerminalRepositorio(pc).RegistrarAccesoAsync("PC-HU17")));
        return (vm, sunat);
    }

    private async Task<Comprobante> VenderAsync(MainViewModel vm, bool esperarSunat = true)
    {
        var producto = await new ProductoRepositorio(_bd.Conexiones)
            .RegistrarAsync(DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Cemento Portland"), 32.50m, "BLS"));
        vm.Buscador!.Elegir(producto);
        vm.Venta.CantidadTexto = "2";
        Assert.True(vm.Venta.Agregar());

        await vm.Emision!.EmitirCommand.EjecutarAsync();
        Assert.Null(vm.Emision.Error);
        await vm.UltimaImpresion;
        if (esperarSunat)
            await vm.UltimoEnvioSunat;
        return vm.Emision.Ultimo!;
    }

    private async Task<string> EstadoAsync(long id) => (string)(await EscalarAsync("SELECT estado_sunat FROM comprobante WHERE id = @id", id))!;

    private async Task<int> IntentosAsync(long id) =>
        (int)(await EscalarAsync("SELECT intentos FROM comprobante_sunat WHERE comprobante_id = @id", id))!;

    private async Task<bool> TieneCdrAsync(long id) =>
        (bool)(await EscalarAsync("SELECT cdr_zip IS NOT NULL FROM comprobante_sunat WHERE comprobante_id = @id", id))!;

    private async Task<object?> EscalarAsync(string sql, long id)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(sql, conexion);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteScalarAsync();
    }
}
