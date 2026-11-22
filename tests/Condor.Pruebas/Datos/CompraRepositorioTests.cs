using Condor.Datos;
using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;
using Condor.Pruebas.Infraestructura;
using Npgsql;

namespace Condor.Pruebas.Datos;

[Collection(BaseDatos.Nombre)]
public class CompraRepositorioTests
{
    private readonly BaseDatosFixture _bd;

    public CompraRepositorioTests(BaseDatosFixture bd) => _bd = bd;

    private static DateOnly Hoy => DateOnly.FromDateTime(DateTime.Today);

    [FactConBD]
    public async Task Registrar_guarda_la_compra_con_su_detalle_y_su_proveedor()
    {
        var proveedor = await ProveedorAsync();
        var repo = new CompraRepositorio(_bd.Conexiones);

        var compra = await repo.RegistrarAsync(await CompraAsync(proveedor, numero: 12345));
        var guardada = await repo.ObtenerAsync(compra.Id);

        Assert.True(compra.Id > 0);
        Assert.NotNull(guardada);
        Assert.Equal("Factura F001-00012345", guardada.DocumentoCompleto);
        Assert.Equal(proveedor.Ruc, guardada.Proveedor!.Ruc);
        Assert.Equal(proveedor.RazonSocial, guardada.Proveedor.RazonSocial);
        Assert.Equal(Hoy.AddDays(-1), guardada.FechaEmision);
        Assert.Equal("PC-COMPRAS", guardada.TerminalNombre);
        Assert.Equal(2, guardada.Detalle.Count);
        Assert.Equal("VAR", guardada.Detalle[0].Unidad);
        Assert.Equal(31.5m, guardada.Detalle[0].CostoUnitario);
        Assert.Equal(guardada.Detalle.Sum(l => l.Importe), guardada.Total);
        Assert.Equal(guardada.Total, guardada.OpGravada + guardada.Igv);
    }

    [FactConBD]
    public async Task El_costo_admite_cuatro_decimales()
    {
        var proveedor = await ProveedorAsync();
        var repo = new CompraRepositorio(_bd.Conexiones);
        var compra = await CompraAsync(proveedor, numero: 7);
        compra.Detalle.RemoveAt(1);
        compra.Detalle[0].Cantidad = 1000;
        compra.Detalle[0].CostoUnitario = 0.0450m;
        ValidadorCompra.Preparar(compra, Hoy);

        var guardada = await repo.ObtenerAsync((await repo.RegistrarAsync(compra)).Id);

        Assert.Equal(0.0450m, guardada!.Detalle[0].CostoUnitario);
        Assert.Equal(45.00m, guardada.Total);
    }

    [FactConBD]
    public async Task El_mismo_comprobante_del_proveedor_no_se_registra_dos_veces()
    {
        var proveedor = await ProveedorAsync();
        var repo = new CompraRepositorio(_bd.Conexiones);
        await repo.RegistrarAsync(await CompraAsync(proveedor, numero: 501));
        var repetida = await CompraAsync(proveedor, numero: 501);
        repetida.Serie = "F001";

        var ex = await Assert.ThrowsAsync<CompraDuplicadaException>(() => repo.RegistrarAsync(repetida));

        Assert.Contains("Factura F001-00000501", ex.Message);
        Assert.Contains(proveedor.RazonSocial, ex.Message);
        Assert.Equal(0, repetida.Id);
        Assert.Equal(1L, await ContarAsync(proveedor.Id));
    }

    [FactConBD]
    public async Task El_mismo_numero_de_otro_proveedor_o_en_boleta_si_se_registra()
    {
        var aceros = await ProveedorAsync();
        var yura = await ProveedorAsync();
        var repo = new CompraRepositorio(_bd.Conexiones);
        await repo.RegistrarAsync(await CompraAsync(aceros, numero: 88));

        var deOtro = await repo.RegistrarAsync(await CompraAsync(yura, numero: 88));
        var boleta = await CompraAsync(aceros, numero: 88);
        boleta.TipoDocumento = TipoDocumentoCompra.Boleta;
        await repo.RegistrarAsync(boleta);

        Assert.True(deOtro.Id > 0);
        Assert.True(boleta.Id > 0);
    }

    [FactConBD]
    public async Task Tres_PC_registrando_la_misma_factura_a_la_vez_solo_queda_una()
    {
        await using var local = new TresPC();
        var proveedor = await ProveedorAsync();
        var compras = new List<Compra>();
        foreach (var _ in local.Pc)
            compras.Add(await CompraAsync(proveedor, numero: 900));

        using var largada = new Barrier(3);
        var resultados = await Task.WhenAll(local.Pc.Select((pc, i) => Task.Run(async () =>
        {
            largada.SignalAndWait();
            try
            {
                await new CompraRepositorio(pc).RegistrarAsync(compras[i]);
                return (Exception?)null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        })));

        Assert.Single(resultados, r => r is null);
        Assert.All(resultados.Where(r => r is not null), r => Assert.IsType<CompraDuplicadaException>(r));
        Assert.Equal(1L, await ContarAsync(proveedor.Id));
    }

    [FactConBD]
    public async Task Si_una_linea_falla_no_queda_la_compra_a_medias()
    {
        var proveedor = await ProveedorAsync();
        var repo = new CompraRepositorio(_bd.Conexiones);
        var conError = await CompraAsync(proveedor, numero: 31);
        conError.Detalle[1].ProductoId = -1; // producto inexistente

        await Assert.ThrowsAsync<PostgresException>(() => repo.RegistrarAsync(conError));
        var despues = await repo.RegistrarAsync(await CompraAsync(proveedor, numero: 31));

        Assert.Equal(0, conError.Id);
        Assert.True(despues.Id > 0); // el número no quedó tomado
        Assert.Equal(1L, await ContarAsync(proveedor.Id));
    }

    [FactConBD]
    public async Task Buscar_filtra_por_proveedor_y_fechas_y_suma_el_total_del_filtro()
    {
        var aceros = await ProveedorAsync("Aceros");
        var yura = await ProveedorAsync("Yura");
        var repo = new CompraRepositorio(_bd.Conexiones);
        var antigua = await CompraAsync(aceros, numero: 1, fecha: Hoy.AddDays(-40));
        var reciente = await CompraAsync(aceros, numero: 2, fecha: Hoy.AddDays(-3));
        var ayer = await CompraAsync(aceros, numero: 3, fecha: Hoy.AddDays(-1));
        foreach (var c in new[] { antigua, reciente, ayer, await CompraAsync(yura, numero: 4, fecha: Hoy.AddDays(-2)) })
            await repo.RegistrarAsync(c);

        var delMes = await repo.BuscarAsync(new FiltroCompras(Hoy.AddDays(-30), Hoy, aceros.RazonSocial[..14]));
        var porRuc = await repo.BuscarAsync(new FiltroCompras(Proveedor: aceros.Ruc));
        var limitada = await repo.BuscarAsync(new FiltroCompras(Proveedor: aceros.Ruc, Limite: 1));

        Assert.Equal(new[] { ayer.Id, reciente.Id }, delMes.Compras.Select(c => c.Id));
        Assert.Equal(2, delMes.Cantidad);
        Assert.Equal(ayer.Total + reciente.Total, delMes.Total);
        Assert.Equal(3, porRuc.Cantidad);
        Assert.Empty(porRuc.Compras[0].Detalle);
        // Con el límite la lista se corta, pero la cantidad y el total son de todo el filtro.
        Assert.Single(limitada.Compras);
        Assert.Equal(3, limitada.Cantidad);
        Assert.Equal(porRuc.Total, limitada.Total);
    }

    [FactConBD]
    public async Task Sin_compras_en_el_filtro_el_total_es_cero()
    {
        var proveedor = await ProveedorAsync();

        var resultado = await new CompraRepositorio(_bd.Conexiones).BuscarAsync(new FiltroCompras(Proveedor: proveedor.Ruc));

        Assert.Empty(resultado.Compras);
        Assert.Equal(0, resultado.Cantidad);
        Assert.Equal(0m, resultado.Total);
    }

    /// <summary>Proveedor con un RUC nuevo, para que las ejecuciones no choquen.</summary>
    private async Task<Proveedor> ProveedorAsync(string nombre = "Aceros")
    {
        var proveedor = DatosPrueba.ProveedorAceros(DatosPrueba.RucNuevo());
        proveedor.RazonSocial = DatosPrueba.NombreUnico(nombre);
        return await new ProveedorRepositorio(_bd.Conexiones).RegistrarAsync(proveedor);
    }

    /// <summary>Factura F001 del proveedor con fierro y alambre, preparada por el dominio.</summary>
    private async Task<Compra> CompraAsync(Proveedor proveedor, long numero, DateOnly? fecha = null)
    {
        var productos = new ProductoRepositorio(_bd.Conexiones);
        var fierro = await productos.RegistrarAsync(DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Fierro"), 38.00m, "VAR"));
        var alambre = await productos.RegistrarAsync(DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Alambre"), 8.00m, "KG"));
        var terminal = await new TerminalRepositorio(_bd.Conexiones).RegistrarAccesoAsync("PC-COMPRAS");

        var compra = new Compra
        {
            ProveedorId = proveedor.Id,
            Proveedor = proveedor,
            Serie = "f001",
            Numero = numero,
            FechaEmision = fecha ?? Hoy.AddDays(-1),
            TerminalId = terminal.Id,
            Detalle =
            {
                new() { ProductoId = fierro.Id, Descripcion = fierro.Nombre, Unidad = "VAR", Cantidad = 50, CostoUnitario = 31.50m },
                new() { ProductoId = alambre.Id, Descripcion = alambre.Nombre, Unidad = "KG", Cantidad = 10, CostoUnitario = 6.20m },
            },
        };
        return ValidadorCompra.Preparar(compra, Hoy);
    }

    private async Task<long> ContarAsync(long proveedorId)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM compra WHERE proveedor_id = @p", conexion);
        cmd.Parameters.AddWithValue("p", proveedorId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
