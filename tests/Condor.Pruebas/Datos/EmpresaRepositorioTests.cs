using Condor.Datos;
using Condor.Dominio.Validacion;
using Condor.Pruebas.Infraestructura;
using Npgsql;

namespace Condor.Pruebas.Datos;

/// <summary>Migración V005 y datos de la empresa (T-26).</summary>
[Collection(BaseDatos.Nombre)]
public class EmpresaRepositorioTests
{
    private readonly BaseDatosFixture _bd;

    public EmpresaRepositorioTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task La_empresa_tiene_un_RUC_valido_y_razon_social()
    {
        var empresa = await new EmpresaRepositorio(_bd.Conexiones).ObtenerAsync();

        Assert.True(ValidadorDocumento.EsRucValido(empresa.Ruc), empresa.Ruc);
        Assert.False(string.IsNullOrWhiteSpace(empresa.RazonSocial));
        Assert.False(string.IsNullOrWhiteSpace(empresa.Direccion));
    }

    [FactConBD]
    public async Task No_se_puede_agregar_una_segunda_empresa()
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO empresa (id, ruc, razon_social, direccion) VALUES (2, '20131312955', 'Otra', 'Otra')", conexion);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }

    [FactConBD]
    public async Task Un_comprobante_cuyos_totales_no_cuadran_se_rechaza()
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        var terminal = await new TerminalRepositorio(_bd.Conexiones).RegistrarAccesoAsync(DatosPrueba.NombreUnico("PC-V005"));
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO comprobante (serie_id, numero, terminal_id, op_gravada, igv, total)
            SELECT id, 99999999, @t, 10.00, 1.80, 12.00 FROM serie WHERE codigo = 'PR01'
            """, conexion);
        cmd.Parameters.AddWithValue("t", terminal.Id);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("ck_comprobante_totales", ex.ConstraintName);
    }
}
