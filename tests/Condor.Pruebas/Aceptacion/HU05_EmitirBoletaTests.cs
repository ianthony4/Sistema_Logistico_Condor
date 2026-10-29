using Condor.Datos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Ventas;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;
using Npgsql;

namespace Condor.Pruebas.Aceptacion;

/// <summary>
/// HU-05: emitir boleta con o sin DNI.
/// Criterio 1: si el detalle tiene al menos una línea y el tipo es Boleta, al
/// presionar Emitir e imprimir se guarda la boleta con serie B001 y el
/// siguiente número correlativo, lista para el envío a la SUNAT.
/// Criterio 2: si el tipo es Boleta con DNI y el DNI está vacío o no tiene 8
/// dígitos, no se puede emitir y se señala el campo.
/// </summary>
[Collection(BaseDatos.Nombre)]
public class HU05_EmitirBoletaTests
{
    private const int BoletasPorPc = 25;

    private readonly BaseDatosFixture _bd;

    public HU05_EmitirBoletaTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task CA1_la_boleta_se_guarda_con_B001_y_el_siguiente_correlativo()
    {
        var (vm, _) = await PantallaAsync(_bd.Conexiones);
        await AgregarAsync(vm, "Cemento");
        var antes = await UltimoCorrelativoAsync("B001");

        await vm.Emision!.EmitirCommand.EjecutarAsync();

        var boleta = vm.Emision.Ultimo;
        Assert.NotNull(boleta);
        Assert.Null(vm.Emision.Error);
        Assert.Equal("B001", boleta.SerieCodigo);
        Assert.Equal(antes + 1, boleta.Numero);

        var guardada = await new ComprobanteRepositorio(_bd.Conexiones).ObtenerAsync(boleta.Id);
        Assert.Equal(TipoComprobante.Boleta, guardada!.TipoComprobante);
        Assert.Equal("PENDIENTE", guardada.EstadoSunat); // lista para el envío (HU-07)
        Assert.Single(guardada.Detalle);
        Assert.Equal(guardada.OpGravada + guardada.Igv, guardada.Total);
    }

    [FactConBD]
    public async Task CA1_boleta_con_DNI_queda_a_nombre_del_cliente()
    {
        var (vm, pc) = await PantallaAsync(_bd.Conexiones);
        var dni = DatosPrueba.DniNuevo();
        await new ClienteRepositorio(pc).RegistrarAsync(DatosPrueba.ClienteDni(dni));
        await AgregarAsync(vm, "Fierro");
        vm.Cliente!.Documento = dni;
        await vm.Cliente.BuscarCommand.EjecutarAsync();
        vm.Emision!.EsBoletaConDni = true;

        await vm.Emision.EmitirCommand.EjecutarAsync();

        var guardada = await new ComprobanteRepositorio(pc).ObtenerAsync(vm.Emision.Ultimo!.Id);
        Assert.Equal(dni, guardada!.Cliente!.NumeroDocumento);
        Assert.Equal("B001", guardada.SerieCodigo);
    }

    [Theory]
    [InlineData("")]         // DNI vacío
    [InlineData("4587963")]  // 7 dígitos
    [InlineData("458796321")] // 9 dígitos
    public async Task CA2_boleta_con_DNI_sin_un_DNI_valido_no_se_emite_y_senala_el_campo(string dni)
    {
        var comprobantes = new ComprobanteRepositorioEnMemoria();
        var vm = new MainViewModel(new ProductoRepositorioEnMemoria(DatosPrueba.Catalogo()),
            new ClienteRepositorioEnMemoria(DatosPrueba.Clientes()), comprobantes);
        vm.Buscador!.Espera = TimeSpan.Zero;
        vm.IniciarSesion(new Sesion(new Terminal { Id = 1, Nombre = "PC-1" }));
        await AgregarAsync(vm, "clavo");
        vm.Cliente!.Documento = dni;
        if (dni.Length > 0)
            await vm.Cliente.BuscarCommand.EjecutarAsync();
        vm.Emision!.EsBoletaConDni = true;

        await vm.Emision.EmitirCommand.EjecutarAsync();

        Assert.Empty(comprobantes.Comprobantes);
        Assert.True(vm.Cliente.TieneError);
        Assert.NotNull(vm.Emision.Error);
        Assert.Single(vm.Venta.Detalle);
    }

    [FactConBD]
    public async Task Tres_PC_emitiendo_boletas_a_la_vez_no_repiten_ni_saltan_numeros()
    {
        await using var local = new TresPC();
        var serie = await CrearSerieBoletaAsync();
        var producto = await new ProductoRepositorio(local.Pc[0])
            .RegistrarAsync(DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Cinta aislante"), 3.50m));
        var terminales = await Task.WhenAll(local.Pc.Select((pc, i) =>
            new TerminalRepositorio(pc).RegistrarAccesoAsync(DatosPrueba.NombreUnico($"PC-{i + 1}"))));

        using var largada = new Barrier(3);
        await Task.WhenAll(local.Pc.Select((pc, i) => Task.Run(async () =>
        {
            var repo = new ComprobanteRepositorio(pc);
            largada.SignalAndWait();
            for (var n = 0; n < BoletasPorPc; n++)
            {
                var detalle = new List<DetalleComprobante>
                {
                    new() { ProductoId = producto.Id, Descripcion = producto.Nombre, Cantidad = n + 1, PrecioUnitario = producto.Precio },
                };
                var boleta = EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.Boleta, detalle));
                boleta.SerieCodigo = serie;
                boleta.TerminalId = terminales[i].Id;
                await repo.EmitirAsync(boleta);
            }
        })));

        var numeros = await NumerosAsync(serie);
        var total = 3 * BoletasPorPc;
        Assert.Equal(Enumerable.Range(1, total).Select(x => (long)x), numeros);
        Assert.Equal(total, await LineasAsync(serie)); // cada boleta con su línea
    }

    /// <summary>Pantalla Nueva venta conectada a la base de pruebas, con la terminal registrada.</summary>
    private static async Task<(MainViewModel Vm, FabricaConexiones Pc)> PantallaAsync(FabricaConexiones pc)
    {
        var productos = new ProductoRepositorio(pc);
        foreach (var p in DatosPrueba.Catalogo())
        {
            p.Nombre = DatosPrueba.NombreUnico(p.Nombre.Split(' ')[0]);
            await productos.RegistrarAsync(p);
        }

        var vm = new MainViewModel(productos, new ClienteRepositorio(pc), new ComprobanteRepositorio(pc));
        vm.Buscador!.Espera = TimeSpan.Zero;
        var terminal = await new TerminalRepositorio(pc).RegistrarAccesoAsync("PC-HU05");
        vm.IniciarSesion(new Sesion(terminal));
        return (vm, pc);
    }

    private static async Task AgregarAsync(MainViewModel vm, string texto)
    {
        vm.Buscador!.Texto = texto;
        await vm.Buscador.UltimaBusqueda;
        Assert.NotNull(vm.Buscador.Seleccionado);
        vm.Buscador.Elegir(vm.Buscador.Seleccionado);
        vm.Venta.CantidadTexto = "2";
        Assert.True(vm.Venta.Agregar());
    }

    private async Task<long> UltimoCorrelativoAsync(string serie)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand("SELECT ultimo_correlativo FROM serie WHERE codigo = @s", conexion);
        cmd.Parameters.AddWithValue("s", serie);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Serie de boletas de prueba (Z + 3 dígitos) para contar desde 1.</summary>
    private async Task<string> CrearSerieBoletaAsync()
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO serie (tipo_comprobante, codigo)
            SELECT '03', 'Z' || lpad(n::text, 3, '0')
            FROM generate_series(1, 999) AS n
            WHERE NOT EXISTS (SELECT 1 FROM serie WHERE codigo = 'Z' || lpad(n::text, 3, '0'))
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

    private async Task<int> LineasAsync(string serie)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT count(*) FROM comprobante_detalle d
            JOIN comprobante c ON c.id = d.comprobante_id
            JOIN serie s ON s.id = c.serie_id
            WHERE s.codigo = @s
            """, conexion);
        cmd.Parameters.AddWithValue("s", serie);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }
}
