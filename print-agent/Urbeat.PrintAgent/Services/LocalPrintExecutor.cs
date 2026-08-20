using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Urbeat.PrintAgent.Services;

public sealed class LocalPrintExecutor : ILocalPrintExecutor
{
    public async Task<LocalPrintExecutionResult> PrintRawTextAsync(string printerName, string rawText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            return new LocalPrintExecutionResult { Outcome = PrintOutcome.Failed, Message = "Nenhuma impressora local foi configurada." };
        }

        if (OperatingSystem.IsWindows())
        {
            return PrintOnWindows(printerName, rawText);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return await PrintWithLpAsync(printerName, rawText, cancellationToken);
        }

        return new LocalPrintExecutionResult { Outcome = PrintOutcome.Unknown, Message = "Plataforma nao suportada pelo agente local." };
    }

    private static LocalPrintExecutionResult PrintOnWindows(string printerName, string rawText)
    {
        var bytes = Encoding.UTF8.GetBytes(rawText.Replace("\n", "\r\n"));
        var documentName = $"Urbeat-{DateTime.UtcNow:yyyyMMddHHmmss}";

        if (!NativeMethods.OpenPrinter(printerName, out var printerHandle, IntPtr.Zero))
        {
            return new LocalPrintExecutionResult { Outcome = PrintOutcome.Failed, Message = $"Nao foi possivel abrir a impressora '{printerName}'." };
        }

        try
        {
            var docInfo = new NativeMethods.DOCINFOA
            {
                pDocName = documentName,
                pDataType = "RAW"
            };

            var jobId = NativeMethods.StartDocPrinter(printerHandle, 1, docInfo);
            if (jobId == 0)
            {
                return new LocalPrintExecutionResult { Outcome = PrintOutcome.Failed, Message = $"Falha ao iniciar o job RAW em '{printerName}'." };
            }

            try
            {
                if (!NativeMethods.StartPagePrinter(printerHandle))
                {
                    return new LocalPrintExecutionResult { Outcome = PrintOutcome.Failed, Message = $"Falha ao iniciar a pagina em '{printerName}'." };
                }

                try
                {
                    var pointer = Marshal.AllocHGlobal(bytes.Length);
                    try
                    {
                        Marshal.Copy(bytes, 0, pointer, bytes.Length);
                        if (!NativeMethods.WritePrinter(printerHandle, pointer, bytes.Length, out var written) || written != bytes.Length)
                        {
                            return new LocalPrintExecutionResult { Outcome = PrintOutcome.Failed, Message = $"Falha ao escrever todos os bytes na impressora '{printerName}'." };
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pointer);
                    }
                }
                finally
                {
                    NativeMethods.EndPagePrinter(printerHandle);
                }
            }
            finally
            {
                NativeMethods.EndDocPrinter(printerHandle);
            }

            return new LocalPrintExecutionResult
            {
                Outcome = PrintOutcome.Queued,
                JobId = jobId.ToString(),
                Message = $"Job {jobId} enfileirado para '{printerName}'."
            };
        }
        finally
        {
            NativeMethods.ClosePrinter(printerHandle);
        }
    }

    private static async Task<LocalPrintExecutionResult> PrintWithLpAsync(string printerName, string rawText, CancellationToken cancellationToken)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"urbeat-print-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(tempFile, rawText, Encoding.UTF8, cancellationToken);

        try
        {
            var args = $"-d \"{printerName}\" -o raw \"{tempFile}\"";
            var startInfo = new ProcessStartInfo("lp", args)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new LocalPrintExecutionResult { Outcome = PrintOutcome.Failed, Message = "Nao foi possivel iniciar o comando lp." };
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                return new LocalPrintExecutionResult { Outcome = PrintOutcome.Failed, Message = string.IsNullOrWhiteSpace(error) ? "lp retornou erro." : error.Trim() };
            }

            var requestId = ExtractLpRequestId(output);

            return new LocalPrintExecutionResult
            {
                Outcome = PrintOutcome.Queued,
                JobId = requestId,
                Message = string.IsNullOrWhiteSpace(requestId)
                    ? $"Job enfileirado em '{printerName}'."
                    : $"Job {requestId} enfileirado em '{printerName}'."
            };
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    internal static string? ExtractLpRequestId(string output)
    {
        const string marker = "request id is";
        var index = output.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;

        var rest = output[(index + marker.Length)..].TrimStart();
        var end = rest.IndexOf(' ');
        var token = end > 0 ? rest[..end] : rest;
        token = token.Trim().TrimEnd('.');

        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public sealed class DOCINFOA
        {
            [MarshalAs(UnmanagedType.LPStr)]
            public string pDocName = string.Empty;

            [MarshalAs(UnmanagedType.LPStr)]
            public string pOutputFile = string.Empty;

            [MarshalAs(UnmanagedType.LPStr)]
            public string pDataType = string.Empty;
        }

        [DllImport("winspool.drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern bool OpenPrinter(string szPrinter, out IntPtr hPrinter, IntPtr pd);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
        [return: MarshalAs(UnmanagedType.U4)]
        public static extern uint StartDocPrinter(IntPtr hPrinter, int level, [In] DOCINFOA di);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);
    }
}
