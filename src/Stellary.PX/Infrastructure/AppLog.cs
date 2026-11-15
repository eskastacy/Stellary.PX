using System.Text;

namespace Stellary.PX.Infrastructure;

public enum LogLevel { Info, Warn, Error }

/// <summary>Append-only session log. Thread-safe; never throws into callers.</summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string _file = "";

    public static string FilePath
    {
        get
        {
            if (_file.Length == 0)
            {
                Paths.Ensure();
                _file = Path.Combine(Paths.Logs, $"stellary-{DateTime.Now:yyyyMMdd}.log");
            }
            return _file;
        }
    }

    public static void Info(string source, string message) => Write(LogLevel.Info, source, message);
    public static void Warn(string source, string message) => Write(LogLevel.Warn, source, message);
    public static void Error(string source, string message) => Write(LogLevel.Error, source, message);

    public static void Write(LogLevel level, string source, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {level,-5}  {source,-18}  {message}{Environment.NewLine}";
        try
        {
            lock (Gate)
            {
                File.AppendAllText(FilePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never break the app.
        }
    }

    public static IReadOnlyList<string> Tail(int lines = 120)
    {
        try
        {
            lock (Gate)
            {
                if (!File.Exists(FilePath)) return Array.Empty<string>();
                using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                var all = new List<string>();
                while (sr.ReadLine() is { } l)
                {
                    all.Add(l);
                    if (all.Count > lines * 3) all.RemoveRange(0, lines);
                }
                return all.Count <= lines ? all : all.GetRange(all.Count - lines, lines);
            }
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
