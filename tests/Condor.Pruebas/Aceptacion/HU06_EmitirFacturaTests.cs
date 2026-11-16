using Condor.Datos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Dominio.Ventas;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;
using Npgsql;

namespace Condor.Pruebas.Aceptacion;

/// <summary>
/// HU-06: emitir factura electrónica con RUC.
/// Criterio 1: si el tipo elegido es Factura y el cliente tiene RUC válido, al
/// presionar Emitir e imprimir el sistema guarda la factura con serie F001 y
/// el siguiente correlativo, la envía a la SUNAT y la imprime.
/// Criterio 2: si el tipo elegido es Factura y no se ha seleccionado un
/// cliente con RUC, el sistema no permite emitir y pide el RUC.
/// </summary>
/// <remarks>Usa la base de pruebas, la ticketera falsa y la SUNAT falsa.</remarks>
[Collection(BaseDatos.Nombre)]
public class HU06_EmitirFacturaTests
{
    private const int FacturasPorPc = 10;

    private readonly BaseDatosFixture _bd;

    public HU06_EmitirFacturaTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task CA1_la_factura_se_guarda_con_F001_el_siguiente_numero_se_envia_y_se_imprime()
    {
        var (vm, sunat, ticketera) = await PantallaAsync();
        var ruc = await RegistrarConstructoraAsync();
        await AgregarAsync(vm, "Cemento", "20");
        var antes = await UltimoCorrelativoAsync("F001");

        vm.Emision!.EsFactura = true;
        vm.Cliente!.Documento = ruc;
        await vm.Cliente.BuscarCommand.EjecutarAsync();
        await vm.Emision.EmitirCommand.EjecutarAsync();
        await Task.WhenAll(vm.UltimaImpresion, vm.UltimoEnvioSunat);

        var factura = vm.Emision.Ultimo!;
        Assert.Null(vm.Emision.Error);
        Assert.Equal("F001", factura.SerieCodigo);
        Assert.Equal(antes + 1, factura.Numero);

        // La guarda en la base central a nombre de la constructora...
        var guardada = await new ComprobanteRepositorio(_bd.Conexiones).ObtenerAsync(factura.Id);
        Assert.Equal(TipoComprobante.Factura, guardada!.TipoComprobante);
        Assert.Equal(ruc, guardada.Cliente!.NumeroDocumento);
        // ...la envía a la SUNAT...
        Assert.Same(factura, Assert.Single(sunat.Enviados));
        Assert.Equal(EstadoSunat.Aceptado, guardada.EstadoSunat);
        // ...y la imprime con los datos de la factura.
        var ticket = Assert.Single(ticketera.Impresos).ComoTexto();
        Assert.Contains("FACTURA ELECTRÓNICA", ticket);
        Assert.Contains(factura.NumeroCompleto, ticket);
        Assert.Contains($"RUC: {ruc}", ticket);
        Assert.Contains("Forma de pago: Contado", ticket);
    }

    [FactConBD]
    public async Task CA2_sin_cliente_con_RUC_no_se_emite_y_se_pide_el_RUC()
    {
        var (vm, sunat, ticketera) = await PantallaAsync();
        await AgregarAsync(vm, "Fierro", "10");
        var antes = await UltimoCorrelativoAsync("F001");
        var pedidos = 0;
        vm.Emision!.RucRequerido += () => pedidos++;

        vm.Emision.EsFactura = true;
        await vm.Emision.EmitirCommand.EjecutarAsync();

        Assert.Null(vm.Emision.Ultimo);
        Assert.Contains("RUC", vm.Emision.Error);
        Assert.True(vm.Cliente!.TieneError);
        Assert.True(pedidos >= 1);
        Assert.Equal(antes, await UltimoCorrelativoAsync("F001")); // no se gastó ningún número
        Assert.Empty(ticketera.Impresos);
        Assert.Empty(sunat.Enviados);
        Assert.Single(vm.Venta.Detalle); // la venta sigue en pantalla para completar el RUC
    }

    [FactConBD]
    public async Task CA2_con_un_cliente_de_DNI_tampoco_se_emite_la_factura()
    {
        var (vm, _, _) = await PantallaAsync();
        var dni = DatosPrueba.DniNuevo();
        await new ClienteRepositorio(_bd.Conexiones).RegistrarAsync(DatosPrueba.ClienteDni(dni));
        await AgregarAsync(vm, "Clavo", "3");
        vm.Cliente!.Documento = dni;
        await vm.Cliente.BuscarCommand.EjecutarAsync();
        var antes = await UltimoCorrelativoAsync("F001");

        vm.Emision!.EsFactura = true;
        await vm.Emision.EmitirCommand.EjecutarAsync();

        Assert.Null(vm.Emision.Ultimo);
        Assert.Contains("RUC", vm.Emision.Error);
        Assert.Equal(antes, await UltimoCorrelativoAsync("F001"));
    }

    [FactConBD]
    public async Task CA2_luego_de_buscar_el_RUC_la_misma_venta_se_emite()
    {
        var (vm, _, _) = await PantallaAsync();
        var ruc = await RegistrarConstructoraAsync();
        await AgregarAsync(vm, "Esmalte", "4");
        vm.Emision!.EsFactura = true;
        await vm.Emision.EmitirCommand.EjecutarAsync();
        Assert.NotNull(vm.Emision.Error);

        vm.Cliente!.Documento = ruc;
        await vm.Cliente.BuscarCommand.EjecutarAsync();
        await vm.Emision.EmitirCommand.EjecutarAsync();

        Assert.Null(vm.Emision.Error);
        Assert.Equal("F001", vm.Emision.Ultimo!.SerieCodigo);
    }

    [FactConBD]
    public async Task Tres_PC_emitiendo_facturas_a_la_vez_no_repiten_ni_saltan_numeros()
    {
        await using var local = new TresPC();
        var serie = await CrearSerieFacturaAsync();
        var cliente = await new ClienteRepositorio(local.Pc[0]).RegistrarAsync(DatosPrueba.ClienteRuc(DatosPrueba.RucNuevo()));
        var producto = await new ProductoRepositorio(local.Pc[0])
            .RegistrarAsync(DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Cemento"), 32.50m, "BLS"));
        var terminales = await Task.WhenAll(local.Pc.Select((pc, i) =>
            new TerminalRepositorio(pc).RegistrarAccesoAsync(DatosPrueba.NombreUnico($"PC-{i + 1}"))));

        using var largada = new Barrier(3);
        await Task.WhenAll(local.Pc.Select((pc, i) => Task.Run(async () =>
        {
            var repo = new ComprobanteRepositorio(pc);
            largada.SignalAndWait();
            for (var n = 0; n < FacturasPorPc; n++)
            {
                var detalle = new List<DetalleComprobante>
                {
                    new() { ProductoId = producto.Id, Descripcion = producto.Nombre, Unidad = "BLS", Cantidad = n + 1, PrecioUnitario = producto.Precio },
                };
                var factura = EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.Factura, detalle, cliente));
                factura.SerieCodigo = serie;
                factura.TerminalId = terminales[i].Id;
                await repo.EmitirAsync(factura);
            }
        })));

        Assert.Equal(Enumerable.Range(1, 3 * FacturasPorPc).Select(x => (long)x), await NumerosAsync(serie));
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
        vm.IniciarSesion(new Sesion(await new TerminalRepositorio(pc).RegistrarAccesoAsync("PC-HU06")));
        return (vm, sunat, ticketera);
    }

    /// <summary>Registra una constructora con un RUC nuevo y devuelve el RUC.</summary>
    private async Task<string> RegistrarConstructoraAsync()
    {
        var ruc = DatosPrueba.RucNuevo();
        await new ClienteRepositorio(_bd.Conexiones).RegistrarAsync(DatosPrueba.ClienteRuc(ruc));
        return ruc;
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

    /// <summary>Serie de facturas de prueba (V + 3 dígitos) para contar desde 1.</summary>
    private async Task<string> CrearSerieFacturaAsync()
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO serie (tipo_comprobante, codigo)
            SELECT '01', 'V' || lpad(n::text, 3, '0')
            FROM generate_series(1, 999) AS n
            WHERE NOT EXISTS (SELECT 1 FROM serie WHERE codigo = 'V' || lpad(n::text, 3, '0'))
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
