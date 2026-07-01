using System.Runtime.InteropServices;
using System.Text;

namespace LabelPrinter.Printing;

/// <summary>
/// Sends raw EPL bytes to a Windows named printer (USB / network).
/// </summary>
public static class RawPrinterHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DOCINFOW
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pDocName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string pOutputFile;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string pDataType;
    }

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string? pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, ref DOCINFOW di);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    public static void SendStringToPrinter(string printerName, string data, string dataType = "RAW")
    {
        var bytes = Encoding.ASCII.GetBytes(data);
        SendBytesToPrinter(printerName, bytes, dataType);
    }

    public static void SendBytesToPrinter(string printerName, byte[] bytes, string dataType = "RAW")
    {
        if (string.IsNullOrWhiteSpace(printerName))
            throw new InvalidOperationException("Printer name is not configured.");

        if (!OpenPrinter(printerName, out var hPrinter, IntPtr.Zero))
            throw new InvalidOperationException($"OpenPrinter failed for '{printerName}'. Error: {Marshal.GetLastWin32Error()}");

        try
        {
            var di = new DOCINFOW
            {
                pDocName = "ControlCode Label",
                pDataType = dataType
            };

            if (!StartDocPrinter(hPrinter, 1, ref di))
                throw new InvalidOperationException($"StartDocPrinter failed. Error: {Marshal.GetLastWin32Error()}");

            try
            {
                if (!StartPagePrinter(hPrinter))
                    throw new InvalidOperationException($"StartPagePrinter failed. Error: {Marshal.GetLastWin32Error()}");

                try
                {
                    var ptr = Marshal.AllocCoTaskMem(bytes.Length);
                    try
                    {
                        Marshal.Copy(bytes, 0, ptr, bytes.Length);
                        if (!WritePrinter(hPrinter, ptr, bytes.Length, out var written) || written != bytes.Length)
                            throw new InvalidOperationException($"WritePrinter failed. Error: {Marshal.GetLastWin32Error()}");
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(ptr);
                    }
                }
                finally
                {
                    EndPagePrinter(hPrinter);
                }
            }
            finally
            {
                EndDocPrinter(hPrinter);
            }
        }
        finally
        {
            ClosePrinter(hPrinter);
        }
    }
}
