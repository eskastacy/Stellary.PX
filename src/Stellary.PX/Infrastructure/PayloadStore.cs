using System.Reflection;

namespace Stellary.PX.Infrastructure;

/// <summary>
/// Ships the uncap payload inside the assembly and materializes it on disk.
/// The engine must be a real file: both the message-hook loader and
/// LoadLibraryEx take a path, and a single-file build has no loose DLL.
/// </summary>
public static class PayloadStore
{
    public const string DllFileName = "UncapEngine.dll";
    public const string IniFileName = "UncapEngine.ini";

    private const string DllResource = "Stellary.PX.Payload.UncapEngine.dll";
    private const string IniResource = "Stellary.PX.Payload.UncapEngine.ini";

    private static readonly object Gate = new();
    private static string? _dllPath;
    private static string? _iniPath;

    public static string DllPath => Ensure().Dll;
    public static string IniPath => Ensure().Ini;

    private static (string Dll, string Ini) Ensure()
    {
        if (_dllPath is not null && _iniPath is not null) return (_dllPath, _iniPath);

        lock (Gate)
        {
            if (_dllPath is not null && _iniPath is not null) return (_dllPath, _iniPath);

            var version = typeof(PayloadStore).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            var directory = Path.Combine(Paths.AppData, "payload", version);
            Directory.CreateDirectory(directory);

            _dllPath = Extract(DllResource, Path.Combine(directory, DllFileName));
            _iniPath = Extract(IniResource, Path.Combine(directory, IniFileName));
            return (_dllPath, _iniPath);
        }
    }

    /// <summary>
    /// Writes the resource beside the versioned payload. An existing file with
    /// the right length is reused; a file still mapped by a running emulator is
    /// kept rather than replaced.
    /// </summary>
    private static string Extract(string resourceName, string destination)
    {
        var assembly = typeof(PayloadStore).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource missing: {resourceName}");

        var expectedLength = stream.Length;
        if (File.Exists(destination) && new FileInfo(destination).Length == expectedLength)
            return destination;

        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temp = destination + "." + Environment.ProcessId + ".tmp";
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            stream.CopyTo(file);

        try
        {
            if (File.Exists(destination)) File.Replace(temp, destination, null);
            else File.Move(temp, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); } catch { }

            // Another instance may have written it first, or a loaded copy is
            // still locked. A complete copy is good enough.
            if (!File.Exists(destination) || new FileInfo(destination).Length != expectedLength)
                throw;
        }

        return destination;
    }
}
