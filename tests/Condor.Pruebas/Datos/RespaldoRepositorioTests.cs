using Condor.Datos;
using Condor.Dominio.Entidades;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Datos;

/// <summary>Migración V010 y registro de los respaldos (HU-15, T-60).</summary>
[Collection(BaseDatos.Nombre)]
public class RespaldoRepositorioTests
{
    private readonly BaseDatosFixture _bd;

    public RespaldoRepositorioTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task Registrar_guarda_el_respaldo_y_es_el_ultimo_para_todas_las_PC()
    {
        var principal = await new TerminalRepositorio(_bd.Conexiones).RegistrarAccesoAsync("PC-PRINCIPAL");
        var repo = new RespaldoRepositorio(_bd.Conexiones);
        var ahora = await PosteriorATodosAsync(repo);

        var guardado = await repo.RegistrarAsync(Nuevo(principal.Id, ahora));

        Assert.True(guardado.Id > 0);
        await using var pcs = new TresPC();
        var visto = await new RespaldoRepositorio(pcs.Pc[2]).UltimoAsync();
        Assert.Equal(guardado.Id, visto!.Id);
        Assert.Equal(Respaldo.NombreArchivo(ahora), visto.Archivo);
        Assert.Equal(@"E:\Respaldos Condor", visto.Carpeta);
        Assert.Equal(845_312, visto.Bytes);
        Assert.Equal("PC-PRINCIPAL", visto.TerminalNombre);
        Assert.Equal(ahora.ToUnixTimeSeconds(), visto.TerminadoEn.ToUnixTimeSeconds());
    }

    [FactConBD]
    public async Task Listar_devuelve_del_mas_reciente_al_mas_antiguo()
    {
        var principal = await new TerminalRepositorio(_bd.Conexiones).RegistrarAccesoAsync("PC-PRINCIPAL");
        var repo = new RespaldoRepositorio(_bd.Conexiones);
        var referencia = await PosteriorATodosAsync(repo);

        var viejo = await repo.RegistrarAsync(Nuevo(principal.Id, referencia));
        var nuevo = await repo.RegistrarAsync(Nuevo(principal.Id, referencia.AddDays(2)));

        var lista = await repo.ListarAsync(2);
        Assert.Equal(new[] { nuevo.Id, viejo.Id }, lista.Select(r => r.Id));
    }

    [FactConBD]
    public async Task Un_respaldo_vacio_no_se_registra()
    {
        var principal = await new TerminalRepositorio(_bd.Conexiones).RegistrarAccesoAsync("PC-PRINCIPAL");
        var vacio = Nuevo(principal.Id, DateTimeOffset.Now);
        vacio.Bytes = 0;

        await Assert.ThrowsAsync<ArgumentException>(() => new RespaldoRepositorio(_bd.Conexiones).RegistrarAsync(vacio));
    }

    /// <summary>Un día después del último respaldo que ya está en la base de pruebas (de otras ejecuciones).</summary>
    private static async Task<DateTimeOffset> PosteriorATodosAsync(RespaldoRepositorio repo)
    {
        var ultimo = await repo.UltimoAsync();
        return (ultimo is null || ultimo.TerminadoEn < DateTimeOffset.Now ? DateTimeOffset.Now : ultimo.TerminadoEn).AddDays(1);
    }

    private static Respaldo Nuevo(int terminalId, DateTimeOffset terminado) => new()
    {
        Archivo = Respaldo.NombreArchivo(terminado),
        Carpeta = @"E:\Respaldos Condor",
        Bytes = 845_312,
        IniciadoEn = terminado.AddSeconds(-4),
        TerminadoEn = terminado,
        TerminalId = terminalId,
    };
}
