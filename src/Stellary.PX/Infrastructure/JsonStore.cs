using System.Text.Json;

namespace Stellary.PX.Infrastructure;

public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static T Load<T>(string path, T fallback)
    {
        try
        {
            if (!File.Exists(path)) return fallback;
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return fallback;
            return JsonSerializer.Deserialize<T>(json, Options) ?? fallback;
        }
        catch (Exception ex)
        {
            AppLog.Warn("store", $"load failed for {Path.GetFileName(path)}: {ex.Message}");
            return fallback;
        }
    }

    public static bool Save<T>(string path, T value)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("store", $"save failed for {Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }
}
