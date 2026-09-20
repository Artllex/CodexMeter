namespace CodexMeter;

static class Diagnostics
{
    // Deliberately exclude prompt contents, paths and exception messages.
    public static void Report(string operation, Exception error) =>
        System.Diagnostics.Trace.TraceWarning("{0}: {1} (0x{2:X8})", operation, error.GetType().Name, error.HResult);
}
