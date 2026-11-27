using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;

namespace Condor.Sunat;

/// <summary>
/// Revisa que la PC esté lista para facturar de verdad (T-99): al abrir el
/// sistema en producción, avisa qué falta antes de emitir comprobantes con
/// validez ante la SUNAT. En beta no revisa nada: es el ambiente de pruebas.
/// </summary>
public static class ChequeoProduccion
{
    /// <summary>RUC provisional que deja la migración V005 hasta cargar el de la ficha RUC.</summary>
    public const string RucProvisional = "20600000005";

    /// <summary>Días antes del vencimiento del certificado desde los que se avisa.</summary>
    public const int DiasAvisoCertificado = 30;

    /// <summary>Lo que falta para producción, en el orden en que se resuelve; vacío si todo está listo.</summary>
    public static IReadOnlyList<string> Pendientes(ConfiguracionSunat config, Empresa empresa, DateTime? ahora = null,
        string? carpeta = null)
    {
        var pendientes = new List<string>();
        if (!config.EsProduccion)
            return pendientes;

        if (empresa.Ruc == RucProvisional || !ValidadorDocumento.EsRucValido(empresa.Ruc))
            pendientes.Add("Los datos de la empresa siguen siendo los provisionales: cargue el RUC, la razón social y la dirección de la ficha RUC (docs/produccion.md, paso 1).");
        if (!ValidadorGuia.EsUbigeo(empresa.Ubigeo))
            pendientes.Add("El ubigeo del domicilio fiscal de la empresa no es válido (6 dígitos).");
        if (!config.Configurada)
            pendientes.Add("Falta el usuario secundario SOL y su clave en la sección Sunat de condor.json.");

        if (string.IsNullOrWhiteSpace(config.Certificado))
        {
            pendientes.Add("Falta el certificado digital (.pfx) en la sección Sunat de condor.json.");
        }
        else
        {
            try
            {
                var certificado = config.CargarCertificado(empresa.Ruc, carpeta);
                var momento = ahora ?? DateTime.Now;
                if (certificado.NotAfter < momento.AddDays(DiasAvisoCertificado))
                    pendientes.Add($"El certificado digital vence el {certificado.NotAfter:dd/MM/yyyy}: tramite su renovación.");
            }
            catch (Exception ex) when (ex is CertificadoInvalidoException or IOException or System.Security.Cryptography.CryptographicException)
            {
                pendientes.Add($"El certificado digital no se pudo abrir: {ex.Message}");
            }
        }

        if (!config.GuiasConfiguradas)
            pendientes.Add("Faltan el ClientId y el ClientSecret de la API de guías: las guías de remisión quedarán pendientes de envío.");
        return pendientes;
    }
}
