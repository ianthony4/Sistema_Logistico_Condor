using Condor.Dominio.Entidades;

namespace Condor.Pruebas.Dominio;

public class RespaldoTests
{
    [Fact]
    public void El_nombre_lleva_la_fecha_y_la_hora_sin_caracteres_que_Windows_no_admite()
    {
        var nombre = Respaldo.NombreArchivo(new DateTimeOffset(2026, 11, 16, 18, 30, 15, TimeSpan.FromHours(-5)));

        Assert.Equal("condor_2026-11-16_18-30.backup", nombre);
        Assert.Empty(nombre.Intersect(Path.GetInvalidFileNameChars().Concat(new[] { ':', '/' })));
    }

    [Theory]
    [InlineData(512, "1 KB")]
    [InlineData(845_312, "825 KB")]
    [InlineData(13_002_342, "12.4 MB")]
    public void El_tamano_se_muestra_en_KB_o_MB(long bytes, string esperado)
    {
        Assert.Equal(esperado, new Respaldo { Bytes = bytes }.Tamano);
    }
}
