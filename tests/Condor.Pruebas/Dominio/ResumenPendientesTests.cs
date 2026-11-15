using Condor.Dominio.Sunat;

namespace Condor.Pruebas.Dominio;

public class ResumenPendientesTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 10, 0, 0, TimeSpan.FromHours(-5));

    [Theory]
    [InlineData(1, false)]
    [InlineData(47, false)]
    [InlineData(49, true)]
    [InlineData(80, true)]
    public void Avisa_cuando_el_mas_antiguo_lleva_mas_de_dos_dias(int horas, bool porVencer)
    {
        var resumen = new ResumenPendientes(3, Ahora.AddHours(-horas));

        Assert.Equal(porVencer, resumen.PorVencer(Ahora));
    }

    [Fact]
    public void Sin_pendientes_no_hay_nada_por_vencer()
    {
        Assert.False(ResumenPendientes.Ninguno.PorVencer(Ahora));
        Assert.Equal(0, ResumenPendientes.Ninguno.Cantidad);
    }
}
