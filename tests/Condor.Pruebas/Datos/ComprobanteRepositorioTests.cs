using Condor.Datos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Ventas;
using Condor.Pruebas.Infraestructura;
using Npgsql;

namespace Condor.Pruebas.Datos;

[Collection(BaseDatos.Nombre)]
public class ComprobanteRepositorioTests
{
    private readonly BaseDatosFixture _bd;

    public ComprobanteRepositorioTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task Emitir_guarda_cabecera_y_detalle_con_el_siguiente_numero()
    {
        var serie = await CrearSerieBoletaAsync();
        var repo = new ComprobanteRepositorio(_bd.Conexiones);

        var primera = await repo.EmitirAsync(await BoletaAsync(serie));
        var segunda = await repo.EmitirAsync(await BoletaAsync(serie));

        Assert.Equal(1, primera.Numero);
        Assert.Equal(2, segunda.Numero);
        Assert.Equal($"{serie}-00000002", segunda.NumeroCompleto);
        Assert.Equal("PENDIENTE", segunda.EstadoSunat);

        var guardada = await repo.ObtenerAsync(segunda.Id);
        Assert.NotNull(guardada);
        Assert.Equal(TipoComprobante.Boleta, guardada.TipoComprobante);
        Assert.Equal(2, guardada.Detalle.Count);
        Assert.Equal("BLS", guardada.Detalle[0].Unidad);
        Assert.Equal(segunda.Total, guardada.Total);
        Assert.Equal(guardada.Detalle.Sum(l => l.Importe), guardada.Total);
        Assert.Equal(0, guardada.Impresiones);
    }

    [FactConBD]
    public async Task Si_una_linea_falla_no_queda_la_boleta_ni_se_pierde_el_numero()
    {
        var serie = await CrearSerieBoletaAsync();
        var repo = new ComprobanteRepositorio(_bd.Conexiones);
        var conError = await BoletaAsync(serie);
        conError.Detalle[1].ProductoId = -1; // producto inexistente: viola la llave foránea

        await Assert.ThrowsAsync<PostgresException>(() => repo.EmitirAsync(conError));
        var siguiente = await repo.EmitirAsync(await BoletaAsync(serie));

        Assert.Equal(0, conError.Id);
        Assert.Equal(1, siguiente.Numero);
        Assert.Equal(1L, await ContarAsync(serie));
    }

    [FactConBD]
    public async Task La_boleta_con_cliente_lo_trae_al_reimprimir()
    {
        var serie = await CrearSerieBoletaAsync();
        var cliente = await new ClienteRepositorio(_bd.Conexiones).RegistrarAsync(DatosPrueba.ClienteDni(DatosPrueba.DniNuevo()));
        var repo = new ComprobanteRepositorio(_bd.Conexiones);

        var emitida = await repo.EmitirAsync(await BoletaAsync(serie, cliente));
        var guardada = await repo.ObtenerAsync(emitida.Id);

        Assert.Equal(cliente.Id, guardada!.ClienteId);
        Assert.Equal(cliente.NumeroDocumento, guardada.Cliente!.NumeroDocumento);
        Assert.Equal("Juan Pérez Quispe", guardada.Cliente.Nombre);
    }

    [FactConBD]
    public async Task Una_serie_de_otro_tipo_se_rechaza()
    {
        var repo = new ComprobanteRepositorio(_bd.Conexiones);
        var boleta = await BoletaAsync("F001"); // F001 es de facturas

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.EmitirAsync(boleta));
        Assert.Contains("F001", ex.Message);
    }

    [FactConBD]
    public async Task Registrar_impresion_suma_una_y_aparece_en_los_recientes()
    {
        var serie = await CrearSerieBoletaAsync();
        var repo = new ComprobanteRepositorio(_bd.Conexiones);
        var emitida = await repo.EmitirAsync(await BoletaAsync(serie));

        await repo.RegistrarImpresionAsync(emitida.Id);
        await repo.RegistrarImpresionAsync(emitida.Id);

        Assert.Equal(2, (await repo.ObtenerAsync(emitida.Id))!.Impresiones);
        var reciente = (await repo.ListarRecientesAsync(200)).First(c => c.Id == emitida.Id);
        Assert.Equal(emitida.NumeroCompleto, reciente.NumeroCompleto);
        Assert.Empty(reciente.Detalle);
    }

    /// <summary>Boleta preparada por el dominio con dos productos reales del catálogo de pruebas.</summary>
    private async Task<Comprobante> BoletaAsync(string serie, Cliente? cliente = null)
    {
        var productos = new ProductoRepositorio(_bd.Conexiones);
        var cemento = await productos.RegistrarAsync(DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Cemento"), 32.50m, "BLS"));
        var clavos = await productos.RegistrarAsync(DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Clavo 3 in"), 7.50m, "KG"));
        var terminal = await new TerminalRepositorio(_bd.Conexiones).RegistrarAccesoAsync("PC-PRUEBAS");

        var detalle = new List<DetalleComprobante>
        {
            new() { ProductoId = cemento.Id, Descripcion = cemento.Nombre, Unidad = "BLS", Cantidad = 2, PrecioUnitario = 32.50m },
            new() { ProductoId = clavos.Id, Descripcion = clavos.Nombre, Unidad = "KG", Cantidad = 1.5m, PrecioUnitario = 7.50m },
        };
        var tipo = cliente is null ? TipoEmision.Boleta : TipoEmision.BoletaConDni;
        var boleta = EmisionComprobante.Preparar(new SolicitudEmision(tipo, detalle, cliente));
        boleta.SerieCodigo = serie;
        boleta.TerminalId = terminal.Id;
        return boleta;
    }

    /// <summary>Serie de boletas de prueba (W + 3 dígitos) para no tocar la B001.</summary>
    private async Task<string> CrearSerieBoletaAsync()
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO serie (tipo_comprobante, codigo)
            SELECT '03', 'W' || lpad(n::text, 3, '0')
            FROM generate_series(1, 999) AS n
            WHERE NOT EXISTS (SELECT 1 FROM serie WHERE codigo = 'W' || lpad(n::text, 3, '0'))
            LIMIT 1
            RETURNING codigo
            """, conexion);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<long> ContarAsync(string serie)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM comprobante c JOIN serie s ON s.id = c.serie_id WHERE s.codigo = @s", conexion);
        cmd.Parameters.AddWithValue("s", serie);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
