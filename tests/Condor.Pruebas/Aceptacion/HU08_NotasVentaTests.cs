using Condor.Datos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Impresion;
using Condor.Dominio.Ventas;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;
using Npgsql;

namespace Condor.Pruebas.Aceptacion;

/// <summary>
/// HU-08: emitir notas de venta.
/// Criterio 1: si el detalle está armado y se elige Nota de venta, al
/// presionar Emitir e imprimir el sistema la guarda con la serie NV01 y el
/// siguiente correlativo y la imprime con la leyenda de documento interno.
/// Criterio 2: la nota de venta no se envía a la SUNAT ni cuenta como
/// pendiente de envío.
/// </summary>
/// <remarks>Usa la base de pruebas, la ticketera falsa y la SUNAT falsa.</remarks>
[Collection(BaseDatos.Nombre)]
public class HU08_NotasVentaTests
{
    private const int NotasPorPc = 10;

    private readonly BaseDatosFixture _bd;

    public HU08_NotasVentaTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task CA1_la_nota_de_venta_se_guarda_con_NV01_el_siguiente_numero_y_se_imprime_como_documento_interno()
    {
        var (vm, _, ticketera) = await PantallaAsync();
        await AgregarAsync(vm, "Cemento", "25"); // S/ 812.50: más de S/ 700 y sin DNI
        var antes = await UltimoCorrelativoAsync("NV01");

        vm.Emision!.EsNotaVenta = true;
        await vm.Emision.EmitirCommand.EjecutarAsync();
        await Task.WhenAll(vm.UltimaImpresion, vm.UltimoEnvioSunat);

        var nota = vm.Emision.Ultimo!;
        Assert.Null(vm.Emision.Error);
        Assert.Equal("NV01", nota.SerieCodigo);
        Assert.Equal(antes + 1, nota.Numero);
        Assert.Equal(812.50m, nota.Total);

        var guardada = await new ComprobanteRepositorio(_bd.Conexiones).ObtenerAsync(nota.Id);
        Assert.Equal(TipoComprobante.NotaVenta, guardada!.TipoComprobante);
        Assert.Single(guardada.Detalle);
        Assert.Equal(1, guardada.Impresiones);

        var ticket = Assert.Single(ticketera.Impresos).ComoTexto();
        Assert.Contains("NOTA DE VENTA", ticket);
        Assert.Contains(nota.NumeroCompleto, ticket);
        Assert.Contains("Documento interno de control.", ticket);
        Assert.DoesNotContain("IGV", ticket);
    }

    [FactConBD]
    public async Task CA1_la_nota_de_venta_puede_llevar_al_cliente()
    {
        var (vm, _, ticketera) = await PantallaAsync();
        var dni = DatosPrueba.DniNuevo();
        await new ClienteRepositorio(_bd.Conexiones).RegistrarAsync(DatosPrueba.ClienteDni(dni));
        await AgregarAsync(vm, "Clavo", "2");
        vm.Cliente!.Documento = dni;
        await vm.Cliente.BuscarCommand.EjecutarAsync();

        vm.Emision!.EsNotaVenta = true;
        await vm.Emision.EmitirCommand.EjecutarAsync();
        await vm.UltimaImpresion;

        var guardada = await new ComprobanteRepositorio(_bd.Conexiones).ObtenerAsync(vm.Emision.Ultimo!.Id);
        Assert.Equal(dni, guardada!.Cliente!.NumeroDocumento);
        Assert.Contains($"DNI: {dni}", Assert.Single(ticketera.Impresos).ComoTexto());
    }

    [FactConBD]
    public async Task CA2_la_nota_de_venta_no_se_envia_a_la_SUNAT_ni_queda_pendiente()
    {
        var (vm, sunat, _) = await PantallaAsync();
        await AgregarAsync(vm, "Fierro", "3");
        var registro = new EnvioSunatRepositorio(_bd.Conexiones);
        var pendientesAntes = (await registro.ResumenPendientesAsync()).Cantidad;

        vm.Emision!.EsNotaVenta = true;
        await vm.Emision.EmitirCommand.EjecutarAsync();
        await Task.WhenAll(vm.UltimaImpresion, vm.UltimoEnvioSunat);
        var nota = vm.Emision.Ultimo!;

        Assert.Empty(sunat.Enviados);
        Assert.Null(vm.EnvioSunat!.Mensaje);
        Assert.Equal(pendientesAntes, (await registro.ResumenPendientesAsync()).Cantidad);
        Assert.DoesNotContain(nota.Id, await registro.ListarPendientesAsync(1000));
        Assert.False(await TieneEnvioAsync(nota.Id)); // ni un intento anotado
    }

    [FactConBD]
    public async Task CA2_la_boleta_siguiente_si_se_envia_a_la_SUNAT()
    {
        var (vm, sunat, _) = await PantallaAsync();
        await AgregarAsync(vm, "Clavo", "1");
        vm.Emision!.EsNotaVenta = true;
        await vm.Emision.EmitirCommand.EjecutarAsync();
        await Task.WhenAll(vm.UltimaImpresion, vm.UltimoEnvioSunat);

        await AgregarAsync(vm, "Clavo", "1");
        await vm.Emision.EmitirCommand.EjecutarAsync(); // tras la nota, vuelve a Boleta
        await Task.WhenAll(vm.UltimaImpresion, vm.UltimoEnvioSunat);

        var boleta = Assert.Single(sunat.Enviados);
        Assert.Equal("B001", boleta.SerieCodigo);
    }

    [FactConBD]
    public async Task Tres_PC_emitiendo_notas_de_venta_a_la_vez_no_repiten_ni_saltan_numeros()
    {
        await using var local = new TresPC();
        var serie = await CrearSerieNotaVentaAsync();
        var producto = await new ProductoRepositorio(local.Pc[0])
            .RegistrarAsync(DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Clavo"), 7.50m, "KG"));
        var terminales = await Task.WhenAll(local.Pc.Select((pc, i) =>
            new TerminalRepositorio(pc).RegistrarAccesoAsync(DatosPrueba.NombreUnico($"PC-{i + 1}"))));

        using var largada = new Barrier(3);
        await Task.WhenAll(local.Pc.Select((pc, i) => Task.Run(async () =>
        {
            var repo = new ComprobanteRepositorio(pc);
            largada.SignalAndWait();
            for (var n = 0; n < NotasPorPc; n++)
            {
                var detalle = new List<DetalleComprobante>
                {
                    new() { ProductoId = producto.Id, Descripcion = producto.Nombre, Unidad = "KG", Cantidad = n + 1, PrecioUnitario = producto.Precio },
                };
                var nota = EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.NotaVenta, detalle));
                nota.SerieCodigo = serie;
                nota.TerminalId = terminales[i].Id;
                await repo.EmitirAsync(nota);
            }
        })));

        Assert.Equal(Enumerable.Range(1, 3 * NotasPorPc).Select(x => (long)x), await NumerosAsync(serie));
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
        vm.IniciarSesion(new Sesion(await new TerminalRepositorio(pc).RegistrarAccesoAsync("PC-HU08")));
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

    private async Task<bool> TieneEnvioAsync(long id)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM comprobante_sunat WHERE comprobante_id = @id", conexion);
        cmd.Parameters.AddWithValue("id", id);
        return (long)(await cmd.ExecuteScalarAsync())! > 0;
    }

    /// <summary>Serie de notas de venta de prueba (K + 3 dígitos) para contar desde 1.</summary>
    private async Task<string> CrearSerieNotaVentaAsync()
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO serie (tipo_comprobante, codigo)
            SELECT 'NV', 'K' || lpad(n::text, 3, '0')
            FROM generate_series(1, 999) AS n
            WHERE NOT EXISTS (SELECT 1 FROM serie WHERE codigo = 'K' || lpad(n::text, 3, '0'))
            LIMIT 1
            RETURNING codigo
            """, conexion);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<List<long>> NumerosAsync(string serie)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT c.numero FROM comprobante c JOIN serie s ON s.id = c.serie_id WHERE s.codigo = @s ORDER BY c.numero", conexion);
        cmd.Parameters.AddWithValue("s", serie);
        var numeros = new List<long>();
        await using var lector = await cmd.ExecuteReaderAsync();
        while (await lector.ReadAsync())
            numeros.Add(lector.GetInt64(0));
        return numeros;
    }
}
