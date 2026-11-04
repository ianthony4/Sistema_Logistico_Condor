namespace Condor.Dominio.Impresion;

/// <summary>
/// Importe en letras para la leyenda "SON:" del comprobante (HU-12, T-31):
/// 745.00 → "SETECIENTOS CUARENTA Y CINCO CON 00/100 SOLES".
/// </summary>
public static class MontoEnLetras
{
    public const decimal Maximo = 999_999_999.99m;

    private static readonly string[] HastaVeintinueve =
    {
        "CERO", "UNO", "DOS", "TRES", "CUATRO", "CINCO", "SEIS", "SIETE", "OCHO", "NUEVE",
        "DIEZ", "ONCE", "DOCE", "TRECE", "CATORCE", "QUINCE", "DIECISÉIS", "DIECISIETE", "DIECIOCHO", "DIECINUEVE",
        "VEINTE", "VEINTIUNO", "VEINTIDÓS", "VEINTITRÉS", "VEINTICUATRO", "VEINTICINCO", "VEINTISÉIS", "VEINTISIETE",
        "VEINTIOCHO", "VEINTINUEVE",
    };

    private static readonly string[] Decenas =
        { "", "", "", "TREINTA", "CUARENTA", "CINCUENTA", "SESENTA", "SETENTA", "OCHENTA", "NOVENTA" };

    private static readonly string[] Centenas =
    {
        "", "CIENTO", "DOSCIENTOS", "TRESCIENTOS", "CUATROCIENTOS", "QUINIENTOS",
        "SEISCIENTOS", "SETECIENTOS", "OCHOCIENTOS", "NOVECIENTOS",
    };

    /// <summary>Leyenda completa en soles, con los céntimos en número.</summary>
    public static string Soles(decimal monto)
    {
        if (monto < 0 || monto > Maximo)
            throw new ArgumentOutOfRangeException(nameof(monto), $"El importe debe estar entre 0 y {Maximo:N2}.");

        monto = Math.Round(monto, 2, MidpointRounding.AwayFromZero);
        var enteros = (long)Math.Truncate(monto);
        var centimos = (int)((monto - enteros) * 100);
        return $"{Numero(enteros)} CON {centimos:00}/100 SOLES";
    }

    /// <summary>Número entero en letras mayúsculas (0 a 999 999 999).</summary>
    public static string Numero(long n)
    {
        if (n is < 0 or > 999_999_999)
            throw new ArgumentOutOfRangeException(nameof(n));
        if (n == 0)
            return HastaVeintinueve[0];

        var millones = (int)(n / 1_000_000);
        var miles = (int)(n / 1_000 % 1_000);
        var resto = (int)(n % 1_000);
        var partes = new List<string>();

        if (millones == 1)
            partes.Add("UN MILLÓN");
        else if (millones > 1)
            partes.Add($"{HastaMil(millones, apocope: true)} MILLONES");

        if (miles == 1)
            partes.Add("MIL");
        else if (miles > 1)
            partes.Add($"{HastaMil(miles, apocope: true)} MIL");

        if (resto > 0)
            partes.Add(HastaMil(resto, apocope: false));

        return string.Join(" ", partes);
    }

    /// <summary>
    /// 1 a 999. Con apócope, el "uno" final pierde la o delante de mil o
    /// millones: VEINTIÚN MIL, TREINTA Y UN MILLONES, CIENTO UN MIL.
    /// </summary>
    private static string HastaMil(int n, bool apocope)
    {
        if (n == 100)
            return "CIEN";

        var centena = Centenas[n / 100];
        var decenas = HastaCien(n % 100, apocope);
        return string.Join(" ", new[] { centena, decenas }.Where(p => p.Length > 0));
    }

    private static string HastaCien(int n, bool apocope)
    {
        if (n == 0)
            return string.Empty;
        if (apocope && n == 1)
            return "UN";
        if (apocope && n == 21)
            return "VEINTIÚN";
        if (n < 30)
            return HastaVeintinueve[n];

        var unidad = n % 10;
        if (unidad == 0)
            return Decenas[n / 10];
        return $"{Decenas[n / 10]} Y {(apocope && unidad == 1 ? "UN" : HastaVeintinueve[unidad])}";
    }
}
