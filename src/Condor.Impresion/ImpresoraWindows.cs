using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Condor.Dominio.Impresion;

namespace Condor.Impresion;

/// <summary>
/// Ticketera térmica instalada en Windows (HU-12, T-32). Envía los bytes
/// ESC/POS tal cual (RAW) al spooler, sin pasar por el diálogo de impresión.
/// </summary>
/// <remarks>
/// El spooler acepta el trabajo aunque la ticketera esté apagada o sin papel
/// y lo deja en cola. Para avisar al vendedor (criterio 2 de HU-12) se revisa
/// el estado de la impresora antes de enviar y, después, se espera a que el
/// trabajo salga de la cola. Si queda con error o no sale a tiempo, se borra
/// de la cola (para que no se imprima horas después) y se lanza
/// ImpresionException: el comprobante ya está guardado y se reimprime luego.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class ImpresoraWindows : IImpresoraTickets
{
    private readonly TimeSpan _espera;

    /// <param name="nombre">Nombre de la impresora en Configuración > Impresoras de Windows.</param>
    /// <param name="espera">Cuánto esperar a que el ticket salga antes de darlo por fallido.</param>
    public ImpresoraWindows(string nombre, TimeSpan? espera = null)
    {
        Nombre = nombre;
        _espera = espera ?? TimeSpan.FromSeconds(10);
    }

    public string Nombre { get; }

    public Task ImprimirAsync(Ticket ticket, CancellationToken ct = default)
    {
        var datos = ComandosEscPos.Generar(ticket);
        // Las llamadas al spooler bloquean: se hacen fuera del hilo de la ventana.
        return Task.Run(() => Enviar(datos, ct), ct);
    }

    private void Enviar(byte[] datos, CancellationToken ct)
    {
        if (!Winspool.OpenPrinter(Nombre, out var impresora, IntPtr.Zero))
            throw new ImpresionException(
                $"No se encontró la ticketera \"{Nombre}\". Revise que esté instalada y que el nombre en condor.json (Ticketera > Nombre) sea el mismo que en Windows.",
                new Win32Exception());

        try
        {
            RevisarEstado(impresora);

            var documento = new Winspool.DocInfo1 { NombreDocumento = "Comprobante Cóndor", TipoDatos = "RAW" };
            var trabajo = Winspool.StartDocPrinter(impresora, 1, documento);
            if (trabajo == 0)
                throw new ImpresionException("Windows no aceptó el ticket para imprimir.", new Win32Exception());

            var escritos = 0;
            try
            {
                Winspool.StartPagePrinter(impresora);
                var memoria = Marshal.AllocHGlobal(datos.Length);
                try
                {
                    Marshal.Copy(datos, 0, memoria, datos.Length);
                    Winspool.WritePrinter(impresora, memoria, datos.Length, out escritos);
                }
                finally
                {
                    Marshal.FreeHGlobal(memoria);
                }
                Winspool.EndPagePrinter(impresora);
            }
            finally
            {
                Winspool.EndDocPrinter(impresora);
            }

            if (escritos != datos.Length)
            {
                Winspool.SetJob(impresora, trabajo, 0, IntPtr.Zero, Winspool.JobControlDelete);
                throw new ImpresionException("La ticketera no recibió el ticket completo.");
            }

            EsperarTrabajo(impresora, trabajo, ct);
        }
        finally
        {
            Winspool.ClosePrinter(impresora);
        }
    }

    /// <summary>Si Windows ya sabe que la ticketera no puede imprimir, se avisa sin enviar nada.</summary>
    private void RevisarEstado(IntPtr impresora)
    {
        var info = Winspool.LeerInfo2(impresora);
        if (info is null)
            return;

        var (estado, atributos) = info.Value;
        if ((atributos & Winspool.AtributoTrabajarSinConexion) != 0
            || (estado & (Winspool.EstadoDesconectada | Winspool.EstadoNoDisponible)) != 0)
            throw new ImpresionException($"La ticketera \"{Nombre}\" está apagada o desconectada. Enciéndala y reimprima el comprobante.");
        if ((estado & Winspool.EstadoSinPapel) != 0)
            throw new ImpresionException($"La ticketera \"{Nombre}\" no tiene papel. Cambie el rollo y reimprima el comprobante.");
        if ((estado & (Winspool.EstadoTapaAbierta | Winspool.EstadoAtasco)) != 0)
            throw new ImpresionException($"La ticketera \"{Nombre}\" tiene la tapa abierta o el papel atascado.");
        if ((estado & (Winspool.EstadoError | Winspool.EstadoPausada | Winspool.EstadoRequiereAtencion)) != 0)
            throw new ImpresionException($"La ticketera \"{Nombre}\" tiene un error o está en pausa. Revísela y reimprima el comprobante.");
    }

    /// <summary>Espera a que el trabajo salga de la cola; si se traba, lo borra y avisa.</summary>
    private void EsperarTrabajo(IntPtr impresora, int trabajo, CancellationToken ct)
    {
        var limite = DateTime.UtcNow + _espera;
        while (DateTime.UtcNow < limite)
        {
            var estado = Winspool.EstadoTrabajo(impresora, trabajo);
            if (estado is null || (estado.Value & Winspool.TrabajoImpreso) != 0)
                return; // ya no está en la cola (o quedó marcado como impreso)

            if ((estado.Value & Winspool.TrabajoConProblemas) != 0)
                break;

            ct.WaitHandle.WaitOne(250);
            ct.ThrowIfCancellationRequested();
        }

        Winspool.SetJob(impresora, trabajo, 0, IntPtr.Zero, Winspool.JobControlDelete);
        throw new ImpresionException(
            $"La ticketera \"{Nombre}\" no imprimió: puede estar apagada, sin papel o con la tapa abierta. Revísela y reimprima el comprobante.");
    }
}

/// <summary>Funciones del spooler de Windows (winspool.drv) para imprimir en modo RAW.</summary>
[SupportedOSPlatform("windows")]
internal static class Winspool
{
    // Estado de la impresora (PRINTER_INFO_2.Status).
    public const int EstadoPausada = 0x1;
    public const int EstadoError = 0x2;
    public const int EstadoAtasco = 0x8;
    public const int EstadoSinPapel = 0x10;
    public const int EstadoDesconectada = 0x80;
    public const int EstadoNoDisponible = 0x1000;
    public const int EstadoRequiereAtencion = 0x100000;
    public const int EstadoTapaAbierta = 0x400000;

    // PRINTER_INFO_2.Attributes: "Usar impresora sin conexión".
    public const int AtributoTrabajarSinConexion = 0x400;

    // Estado del trabajo (JOB_INFO_1.Status): error, sin conexión, sin papel,
    // bloqueado o requiere intervención del usuario.
    public const int TrabajoConProblemas = 0x2 | 0x20 | 0x40 | 0x200 | 0x400;

    // Impreso o completo: sigue en la cola solo si Windows guarda los documentos impresos.
    public const int TrabajoImpreso = 0x80 | 0x1000;

    public const int JobControlDelete = 5;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public sealed class DocInfo1
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string NombreDocumento = string.Empty;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ArchivoSalida;
        [MarshalAs(UnmanagedType.LPWStr)] public string TipoDatos = "RAW";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo2
    {
        public IntPtr ServerName, PrinterName, ShareName, PortName, DriverName, Comment, Location, DevMode,
            SepFile, PrintProcessor, Datatype, Parameters, SecurityDescriptor;
        public int Attributes, Priority, DefaultPriority, StartTime, UntilTime, Status, Jobs, AveragePpm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobInfo1
    {
        public int JobId;
        public IntPtr PrinterName, MachineName, UserName, Document, Datatype, StatusText;
        public int Status, Priority, Position, TotalPages, PagesPrinted;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public short[] Submitted; // SYSTEMTIME
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool OpenPrinter(string nombre, out IntPtr impresora, IntPtr predeterminados);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool ClosePrinter(IntPtr impresora);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int StartDocPrinter(IntPtr impresora, int nivel, [In] DocInfo1 documento);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool EndDocPrinter(IntPtr impresora);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool StartPagePrinter(IntPtr impresora);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool EndPagePrinter(IntPtr impresora);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool WritePrinter(IntPtr impresora, IntPtr datos, int cantidad, out int escritos);

    [DllImport("winspool.drv", EntryPoint = "SetJobW", SetLastError = true)]
    public static extern bool SetJob(IntPtr impresora, int trabajo, int nivel, IntPtr datos, int comando);

    [DllImport("winspool.drv", EntryPoint = "GetPrinterW", SetLastError = true)]
    private static extern bool GetPrinter(IntPtr impresora, int nivel, IntPtr buffer, int tamano, out int necesario);

    [DllImport("winspool.drv", EntryPoint = "GetJobW", SetLastError = true)]
    private static extern bool GetJob(IntPtr impresora, int trabajo, int nivel, IntPtr buffer, int tamano, out int necesario);

    /// <summary>Estado y atributos de la impresora; null si Windows no los informa.</summary>
    public static (int Estado, int Atributos)? LeerInfo2(IntPtr impresora)
    {
        GetPrinter(impresora, 2, IntPtr.Zero, 0, out var necesario);
        if (necesario <= 0)
            return null;

        var buffer = Marshal.AllocHGlobal(necesario);
        try
        {
            if (!GetPrinter(impresora, 2, buffer, necesario, out _))
                return null;
            var info = Marshal.PtrToStructure<PrinterInfo2>(buffer);
            return (info.Status, info.Attributes);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Estado del trabajo en la cola; null si ya no está (terminó de imprimirse).</summary>
    public static int? EstadoTrabajo(IntPtr impresora, int trabajo)
    {
        GetJob(impresora, trabajo, 1, IntPtr.Zero, 0, out var necesario);
        if (necesario <= 0)
            return null;

        var buffer = Marshal.AllocHGlobal(necesario);
        try
        {
            if (!GetJob(impresora, trabajo, 1, buffer, necesario, out _))
                return null;
            return Marshal.PtrToStructure<JobInfo1>(buffer).Status;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
