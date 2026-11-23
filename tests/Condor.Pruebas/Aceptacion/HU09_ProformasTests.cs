using Condor.Datos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Dominio.Ventas;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;
using Npgsql;

namespace Condor.Pruebas.Aceptacion;

/// <summary>
/// HU-09: emitir proformas y convertirlas en venta.
/// Criterio 1: si el detalle está armado y se elige Proforma, al presionar
/// Emitir e imprimir el sistema la guarda con la serie PR01 y su vigencia de
/// 7 días, la imprime y no la envía a la SUNAT.
/// Criterio 2: si la proforma está vigente, al elegirla en Proformas (F7) y
/// pulsar Convertir en venta, su detalle y su cliente pasan a la pantalla de
/// venta sin reescribirlos, y al emitir queda como boleta o factura con el
/// siguiente correlativo, enlazada a la proforma.
/// Criterio 3: si la proforma ya se convirtió o venció, el sistema no deja
/// convertirla (aunque otra PC lo intente a la vez) y dice en qué comprobante
/// se convirtió o cuándo venció.
/// </summary>
/// <remarks>Usa la base de pruebas, la ticketera falsa y la SUNAT falsa.</remarks>
[Collection(BaseDatos.Nombre)]
public class HU09_ProformasTests
{
    private readonly BaseDatosFixture _bd;

    public HU09_ProformasTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task CA1_la_proforma_se_guarda_con_PR01_y_7_dias_de_vigencia_se_imprime_y_no_va_a_la_SUNAT()
    {
        var (vm, sunat, ticketera) = await PantallaAsync();
        await AgregarAsync(vm, "Cemento", "40");
        await AgregarAsync(vm, "Fierro", "25");
        var antes = await UltimoCorrelativoAsync("PR01");

        vm.Emision!.EsProforma = true;
        await vm.Emision.EmitirCommand.EjecutarAsync();
        await Task.WhenAll(vm.UltimaImpresion, vm.UltimoEnvioSunat);

        var proforma = vm.Emision.Ultimo!;
        var hoy = EmisionComprobante.FechaPeru(DateTimeOffset.Now);
        Assert.Null(vm.Emision.Error);
        Assert.Equal("PR01", proforma.SerieCodigo);
        Assert.Equal(antes + 1, proforma.Numero);
        Assert.Equal(hoy.AddDays(6), proforma.ValidaHasta);

        var guardada = await new ComprobanteRepositorio(_bd.Conexiones).ObtenerAsync(proforma.Id);
        Assert.Equal(TipoComprobante.Proforma, guardada!.TipoComprobante);
        Assert.Equal(2, guardada.Detalle.Count);
        Assert.Empty(sunat.Enviados);

        var ticket = Assert.Single(ticketera.Impresos).ComoTexto();
        Assert.Contains("PROFORMA", ticket);
        Assert.Contains($"Válida hasta: {hoy.AddDays(6):dd/MM/yyyy}", ticket);
    }

    [FactConBD]
    public async Task CA2_convertir_la_proforma_lleva_el_detalle_y_el_cliente_y_emite_la_factura_enlazada()
    {
        var (vm, sunat, ticketera) = await PantallaAsync();
        var ruc = await CotizarObraAsync(vm);
        var proforma = vm.Emision!.Ultimo!;
        var antes = await UltimoCorrelativoAsync("F001");

        // Días después la constructora vuelve: se busca su proforma por el RUC.
        var (otra, otraSunat, _) = await PantallaAsync();
        var ventana = otra.CrearProformas()!;
        ventana.Texto = ruc;
        await ventana.BuscarAsync();
        Comprobante? elegida = null;
        ventana.ProformaElegida += p => elegida = p;
        ventana.Seleccionada = ventana.Proformas.Single(f => f.Proforma.Id == proforma.Id);
        await ventana.ConvertirCommand.EjecutarAsync();
        otra.ConvertirProforma(elegida!);

        Assert.Equal(proforma.Detalle.Select(l => (l.Descripcion, l.Cantidad, l.PrecioUnitario)),
            otra.Venta.Detalle.Select(l => (l.Descripcion, l.Cantidad, l.PrecioUnitario)));
        Assert.Equal(ruc, otra.Cliente!.Seleccionado!.NumeroDocumento);
        Assert.True(otra.Emision!.EsFactura);

        await otra.Emision.EmitirCommand.EjecutarAsync();
        await Task.WhenAll(otra.UltimaImpresion, otra.UltimoEnvioSunat);

        var factura = otra.Emision.Ultimo!;
        Assert.Null(otra.Emision.Error);
        Assert.Equal("F001", factura.SerieCodigo);
        Assert.Equal(antes + 1, factura.Numero);
        Assert.Equal(proforma.Total, factura.Total);
        var repo = new ComprobanteRepositorio(_bd.Conexiones);
        Assert.Equal(proforma.Id, (await repo.ObtenerAsync(factura.Id))!.ProformaId);
        Assert.Equal(factura.NumeroCompleto, (await repo.ObtenerAsync(proforma.Id))!.ConvertidaEn);
        Assert.Same(factura, Assert.Single(otraSunat.Enviados));
        Assert.Empty(sunat.Enviados); // la proforma nunca fue a la SUNAT
        Assert.Single(ticketera.Impresos);
    }

    [FactConBD]
    public async Task CA3_una_proforma_convertida_no_se_vuelve_a_convertir_y_dice_en_que_comprobante_quedo()
    {
        var (vm, _, _) = await PantallaAsync();
        var ruc = await CotizarObraAsync(vm);
        var proforma = vm.Emision!.Ultimo!;

        // La PC-2 abrió Proformas y cargó la proforma; mientras, la PC-1 la vende.
        var (pc2, _, _) = await PantallaAsync();
        var completa = (await new ComprobanteRepositorio(_bd.Conexiones).ObtenerAsync(proforma.Id))!;
        pc2.ConvertirProforma(completa);
        var (pc1, _, _) = await PantallaAsync();
        pc1.ConvertirProforma((await new ComprobanteRepositorio(_bd.Conexiones).ObtenerAsync(proforma.Id))!);
        await pc1.Emision!.EmitirCommand.EjecutarAsync();
        var vendida = pc1.Emision.Ultimo!;
        var numeroAntes = await UltimoCorrelativoAsync("F001");

        await pc2.Emision!.EmitirCommand.EjecutarAsync();

        Assert.Contains("ya se convirtió en venta", pc2.Emision.Error);
        Assert.Equal(numeroAntes, await UltimoCorrelativoAsync("F001")); // no se gastó número
        Assert.Equal(2, pc2.Venta.Detalle.Count);

        // En la lista ya figura en qué factura terminó y no se puede convertir.
        var ventana = pc2.CrearProformas()!;
        ventana.Texto = ruc;
        await ventana.BuscarAsync();
        var fila = ventana.Proformas.Single(f => f.Proforma.Id == proforma.Id);
        Assert.Equal(EstadoProforma.Convertida, fila.Estado);
        Assert.Equal($"Convertida en {vendida.NumeroCompleto}", fila.EstadoTexto);
        ventana.Seleccionada = fila;
        Assert.False(ventana.ConvertirCommand.CanExecute(null));
    }

    [FactConBD]
    public async Task CA3_una_proforma_vencida_no_se_convierte_y_dice_cuando_vencio()
    {
        var (vm, _, _) = await PantallaAsync();
        var ruc = await CotizarObraAsync(vm);
        var proforma = vm.Emision!.Ultimo!;
        await EjecutarAsync("UPDATE comprobante SET valida_hasta = current_date - 2 WHERE id = @id", proforma.Id);

        var ventana = vm.CrearProformas()!;
        ventana.Texto = ruc;
        await ventana.BuscarAsync();
        var fila = ventana.Proformas.Single(f => f.Proforma.Id == proforma.Id);

        Assert.Equal(EstadoProforma.Vencida, fila.Estado);
        Assert.StartsWith("Vencida el ", fila.EstadoTexto);
        ventana.Seleccionada = fila;
        Assert.False(ventana.ConvertirCommand.CanExecute(null));

        // Aunque una PC con la pantalla abierta desde antes intente emitirla, la base central no la acepta.
        vm.ConvertirProforma((await new ComprobanteRepositorio(_bd.Conexiones).ObtenerAsync(proforma.Id))!);
        await vm.Emision.EmitirCommand.EjecutarAsync();
        Assert.Contains("vencida", vm.Emision.Error);
    }

    [FactConBD]
    public async Task CA3_en_su_ultimo_dia_de_vigencia_la_proforma_aun_se_convierte()
    {
        var (vm, _, _) = await PantallaAsync();
        await CotizarObraAsync(vm);
        var proforma = vm.Emision!.Ultimo!;
        await EjecutarAsync("UPDATE comprobante SET valida_hasta = (now() AT TIME ZONE 'America/Lima')::date WHERE id = @id", proforma.Id);

        vm.ConvertirProforma((await new ComprobanteRepositorio(_bd.Conexiones).ObtenerAsync(proforma.Id))!);
        await vm.Emision.EmitirCommand.EjecutarAsync();

        Assert.Null(vm.Emision.Error);
        Assert.Equal(proforma.Id, vm.Emision.Ultimo!.ProformaId);
    }

    /// <summary>Emite una proforma para una constructora con RUC nuevo y devuelve el RUC.</summary>
    private async Task<string> CotizarObraAsync(MainViewModel vm)
    {
        var ruc = DatosPrueba.RucNuevo();
        await new ClienteRepositorio(_bd.Conexiones).RegistrarAsync(DatosPrueba.ClienteRuc(ruc));
        await AgregarAsync(vm, "Cemento", "20");
        await AgregarAsync(vm, "Fierro", "10");
        vm.Cliente!.Documento = ruc;
        await vm.Cliente.BuscarCommand.EjecutarAsync();
        vm.Emision!.EsProforma = true;
        await vm.Emision.EmitirCommand.EjecutarAsync();
        await vm.UltimaImpresion;
        Assert.Null(vm.Emision.Error);
        Assert.Equal(TipoComprobante.Proforma, vm.Emision.Ultimo!.TipoComprobante);
        return ruc;
    }

    /// <summary>Pantalla Nueva venta contra la base de pruebas, con ticketera y SUNAT falsas.</summary>
    private async Task<(MainViewModel Vm, EnvioSunatFalso Sunat, ImpresoraFalsa Ticketera)> PantallaAsync()
    {
        var pc = _bd.Conexiones;
        var productos = new ProductoRepositorio(pc);
        foreach (var p in DatosPrueba.Catalogo())
        {
            p.Nombre = DatosPrueba.NombreUnico(p.Nombre.Split(' ')[0]);
            await productos.RegistrarAsync(p);
        }

        var comprobantes = new ComprobanteRepositorio(pc);
        var ticketera = new ImpresoraFalsa();
        var sunat = new EnvioSunatFalso();
        var vm = new MainViewModel(productos, new ClienteRepositorio(pc), comprobantes)
        {
            Impresion = new ImpresionViewModel(ticketera, new EmpresaRepositorio(pc), comprobantes),
            EnvioSunat = new EnvioSunatViewModel(sunat, new EmpresaRepositorio(pc), new EnvioSunatRepositorio(pc)),
        };
        vm.Buscador!.Espera = TimeSpan.Zero;
        vm.IniciarSesion(new Sesion(await new TerminalRepositorio(pc).RegistrarAccesoAsync("PC-HU09")));
        return (vm, sunat, ticketera);
    }

    private static async Task AgregarAsync(MainViewModel vm, string texto, string cantidad)
    {
        vm.Buscador!.Texto = texto;
        await vm.Buscador.UltimaBusqueda;
        Assert.NotNull(vm.Buscador.Seleccionado);
        vm.Buscador.Elegir(vm.Buscador.Seleccionado);
        vm.Venta.CantidadTexto = cantidad;
        Assert.True(vm.Venta.Agregar());
    }

    private async Task<long> UltimoCorrelativoAsync(string serie)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand("SELECT ultimo_correlativo FROM serie WHERE codigo = @s", conexion);
        cmd.Parameters.AddWithValue("s", serie);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task EjecutarAsync(string sql, long id)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(sql, conexion);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}
