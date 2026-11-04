using Condor.Dominio.Entidades;

namespace Condor.Dominio.Validacion;

/// <summary>
/// Reglas del DNI y del RUC (HU-03, T-21). Se aplican al buscar un cliente,
/// al registrarlo y antes de emitir un comprobante nominativo.
/// </summary>
public static class ValidadorDocumento
{
    public const int LargoDni = 8;
    public const int LargoRuc = 11;

    /// <summary>Pesos del dígito verificador del RUC (módulo 11, SUNAT).</summary>
    private static readonly int[] PesosRuc = { 5, 4, 3, 2, 7, 6, 5, 4, 3, 2 };

    /// <summary>
    /// Prefijos válidos del RUC: 10 persona natural, 15 y 17 casos especiales,
    /// 20 persona jurídica (como las ferreterías, constructoras y obras).
    /// </summary>
    private static readonly string[] PrefijosRuc = { "10", "15", "16", "17", "20" };

    /// <summary>Quita espacios, puntos y guiones que se cuelan al digitar o pegar el número.</summary>
    public static string Limpiar(string? numero) =>
        new((numero ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c is not ('.' or '-')).ToArray());

    /// <summary>
    /// Tipo de documento según el largo de lo digitado: 8 dígitos es DNI y 11 es
    /// RUC. Así el vendedor usa un solo campo "DNI o RUC". Null si no es ninguno.
    /// </summary>
    public static TipoDocumento? TipoSegunLargo(string? numero) => Limpiar(numero) switch
    {
        { Length: LargoDni } n when SoloDigitos(n) => TipoDocumento.Dni,
        { Length: LargoRuc } n when SoloDigitos(n) => TipoDocumento.Ruc,
        _ => null,
    };

    public static bool EsDniValido(string? numero)
    {
        var n = Limpiar(numero);
        return n.Length == LargoDni && SoloDigitos(n);
    }

    public static bool EsRucValido(string? numero) => ErrorRuc(Limpiar(numero)) is null;

    /// <summary>Mensaje en español si el número no es válido para el tipo; null si lo es.</summary>
    public static string? Error(TipoDocumento tipo, string? numero)
    {
        var n = Limpiar(numero);
        return tipo switch
        {
            TipoDocumento.Dni when !EsDniValido(n) => $"El DNI debe tener {LargoDni} dígitos.",
            TipoDocumento.Ruc => ErrorRuc(n),
            TipoDocumento.SinDocumento when n.Length > 0 => "Un cliente sin documento no lleva número.",
            _ => null,
        };
    }

    /// <summary>Dígito verificador que corresponde a los 10 primeros dígitos del RUC.</summary>
    public static int DigitoVerificadorRuc(string primerosDiez)
    {
        if (primerosDiez.Length != 10 || !SoloDigitos(primerosDiez))
            throw new ArgumentException("Se esperaban los 10 primeros dígitos del RUC.", nameof(primerosDiez));

        var suma = 0;
        for (var i = 0; i < 10; i++)
            suma += (primerosDiez[i] - '0') * PesosRuc[i];

        // 11 - resto; si da 10 el dígito es 0 y si da 11 es 1.
        return (11 - suma % 11) % 10;
    }

    private static string? ErrorRuc(string n)
    {
        if (n.Length != LargoRuc || !SoloDigitos(n))
            return $"El RUC debe tener {LargoRuc} dígitos.";
        if (!PrefijosRuc.Contains(n[..2]))
            return "El RUC debe empezar con 10, 15, 16, 17 o 20.";
        if (DigitoVerificadorRuc(n[..10]) != n[10] - '0')
            return "El RUC no es válido: revise los números (el dígito verificador no coincide).";
        return null;
    }

    private static bool SoloDigitos(string texto) => texto.Length > 0 && texto.All(char.IsAsciiDigit);
}
