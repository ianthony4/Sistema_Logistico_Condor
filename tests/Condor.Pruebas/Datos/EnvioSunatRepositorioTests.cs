using Condor.Datos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Dominio.Ventas;
using Condor.Pruebas.Infraestructura;
using Npgsql;

namespace Condor.Pruebas.Datos;

/// <summary>Migración V007 y respuestas de la SUNAT (T-45).</summary>
[Collection(BaseDatos.Nombre)]
public class EnvioSunatRepositorioTests
{
    private readonly BaseDatosFixture _bd;

    public EnvioSunatRepositorioTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task Aceptado_guarda_el_estado_el_XML_el_hash_y_el_CDR()
    {
        var boleta = await EmitirAsync();
        var repo = new EnvioSunatRepositorio(_bd.Conexiones);

        await repo.RegistrarRespuestaAsync(boleta.Id, EnvioSunatFalso.Aceptada("20600000005-03-B001-1"));

        var fila = await LeerAsync(boleta.Id);
        Assert.Equal(EstadoSunat.Aceptado, fila.Estado);
        Assert.Equal("0", fila.Codigo);
        Assert.Equal("<Invoice/>", fila.Xml);
        Assert.Equal("hS0bHq3cJ0aZyb0dA1Y4mWm3x1A=", fila.Hash);
        Assert.Equal(new byte[] { 0x50, 0x4B, 0x05, 0x06 }, fila.Cdr);
        Assert.Equal(1, fila.Intentos);
        Assert.True(fila.Respondido);
        Assert.Null(fila.UltimoError);
    }

    [FactConBD]
    public async Task Rechazado_guarda_el_codigo_y_la_descripcion_y_sale_en_el_historial()
    {
        var boleta = await EmitirAsync();
        var repo = new EnvioSunatRepositorio(_bd.Conexiones);

        await repo.RegistrarRespuestaAsync(boleta.Id, EnvioSunatFalso.Rechazada("2800", "El tipo de documento del receptor no es valido"));

        var fila = await LeerAsync(boleta.Id);
        Assert.Equal(EstadoSunat.Rechazado, fila.Estado);
        Assert.Equal("2800", fila.Codigo);
        var historial = await new ComprobanteRepositorio(_bd.Conexiones).ObtenerAsync(boleta.Id);
        Assert.Equal(EstadoSunat.Rechazado, historial!.EstadoSunat);
        Assert.Equal("El tipo de documento del receptor no es valido", historial.MensajeSunat);
    }

    [FactConBD]
    public async Task Sin_internet_suma_intentos_deja_el_motivo_y_sigue_pendiente()
    {
        var boleta = await EmitirAsync();
        var repo = new EnvioSunatRepositorio(_bd.Conexiones);

        await repo.RegistrarFallaAsync(boleta.Id, "No hay conexión a internet.");
        await repo.RegistrarFallaAsync(boleta.Id, "La SUNAT no respondió en 30 segundos.");

        var fila = await LeerAsync(boleta.Id);
        Assert.Equal(EstadoSunat.Pendiente, fila.Estado);
        Assert.Equal(2, fila.Intentos);
        Assert.Equal("La SUNAT no respondió en 30 segundos.", fila.UltimoError);
        Assert.False(fila.Respondido);
    }

    [FactConBD]
    public async Task Listar_pendientes_da_solo_pendientes_de_la_mas_antigua_a_la_mas_reciente()
    {
        var repo = new EnvioSunatRepositorio(_bd.Conexiones);
        await repo.RegistrarFallaAsync((await EmitirAsync()).Id, "Sin internet");

        var pendientes = await repo.ListarPendientesAsync(20);

        Assert.NotEmpty(pendientes);
        Assert.Equal(pendientes.Order(), pendientes);
        foreach (var id in pendientes)
            Assert.Equal(EstadoSunat.Pendiente, (await new ComprobanteRepositorio(_bd.Conexiones).ObtenerAsync(id))!.EstadoSunat);
    }

    [FactConBD]
    public async Task Tras_un_fallo_el_reenvio_aceptado_borra_el_error_y_sale_de_pendientes()
    {
        var boleta = await EmitirAsync();
        var repo = new EnvioSunatRepositorio(_bd.Conexiones);
        await repo.RegistrarFallaAsync(boleta.Id, "No hay conexión a internet.");

        await repo.RegistrarRespuestaAsync(boleta.Id, EnvioSunatFalso.Aceptada());

        var fila = await LeerAsync(boleta.Id);
        Assert.Equal(EstadoSunat.Aceptado, fila.Estado);
        Assert.Equal(2, fila.Intentos);
        Assert.Null(fila.UltimoError);
        Assert.DoesNotContain(boleta.Id, await repo.ListarPendientesAsync(1000));
    }

    [FactConBD]
    public async Task Un_estado_definitivo_no_se_pisa_con_una_respuesta_posterior()
    {
        var boleta = await EmitirAsync();
        var repo = new EnvioSunatRepositorio(_bd.Conexiones);
        await repo.RegistrarRespuestaAsync(boleta.Id, EnvioSunatFalso.Aceptada());

        // Otra PC lo reenvió y la SUNAT contestó "1033: registrado previamente".
        await repo.RegistrarRespuestaAsync(boleta.Id, new RespuestaSunat
        {
            Estado = EstadoSunat.Pendiente, Codigo = "1033", Descripcion = "El comprobante fue registrado previamente con otros datos",
        });
        await repo.RegistrarFallaAsync(boleta.Id, "Sin internet");

        var fila = await LeerAsync(boleta.Id);
        Assert.Equal(EstadoSunat.Aceptado, fila.Estado);
        Assert.Equal("0", fila.Codigo);
        Assert.Null(fila.UltimoError);
        Assert.Equal(3, fila.Intentos);
    }

    [FactConBD]
    public async Task La_empresa_tiene_el_domicilio_fiscal_de_Majes_para_el_XML()
    {
        var empresa = await new EmpresaRepositorio(_bd.Conexiones).ObtenerAsync();

        Assert.Equal("040520", empresa.Ubigeo);
        Assert.Equal("AREQUIPA", empresa.Departamento);
        Assert.Equal("CAYLLOMA", empresa.Provincia);
        Assert.Equal("MAJES", empresa.Distrito);
        Assert.Equal("0000", empresa.CodigoEstablecimiento);
    }

    private sealed record Fila(string Estado, string? Codigo, string? Xml, string? Hash, byte[]? Cdr, int Intentos, bool Respondido, string? UltimoError);

    private async Task<Fila> LeerAsync(long id)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT c.estado_sunat, cs.codigo, cs.xml_firmado, cs.hash, cs.cdr_zip, cs.intentos,
                   cs.respondido_en IS NOT NULL, cs.ultimo_error
            FROM comprobante c JOIN comprobante_sunat cs ON cs.comprobante_id = c.id
            WHERE c.id = @id
            """, conexion);
        cmd.Parameters.AddWithValue("id", id);
        await using var l = await cmd.ExecuteReaderAsync();
        Assert.True(await l.ReadAsync());
        string? Texto(int i) => l.IsDBNull(i) ? null : l.GetString(i);
        return new Fila(l.GetString(0), Texto(1), Texto(2), Texto(3), l.IsDBNull(4) ? null : (byte[])l[4],
            l.GetInt32(5), l.GetBoolean(6), Texto(7));
    }

    /// <summary>Boleta B001 de la base de pruebas, con un producto nuevo.</summary>
    private async Task<Comprobante> EmitirAsync()
    {
        var producto = await new ProductoRepositorio(_bd.Conexiones)
            .RegistrarAsync(DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Cemento"), 32.50m, "BLS"));
        var terminal = await new TerminalRepositorio(_bd.Conexiones).RegistrarAccesoAsync("PC-SUNAT");
        var boleta = EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.Boleta, new List<DetalleComprobante>
        {
            new() { ProductoId = producto.Id, Descripcion = producto.Nombre, Unidad = "BLS", Cantidad = 2, PrecioUnitario = 32.50m },
        }));
        boleta.TerminalId = terminal.Id;
        return await new ComprobanteRepositorio(_bd.Conexiones).EmitirAsync(boleta);
    }
}
