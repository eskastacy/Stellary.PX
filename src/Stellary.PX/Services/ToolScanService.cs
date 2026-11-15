using System.Diagnostics;

namespace Stellary.PX.Services;

public sealed class ToolScanService
{
    private static readonly string[] KnownTools =
    {
        "cheatengine", "cheatengine-i386", "cheatengine-x86_64",
        "x64dbg", "x32dbg", "ollydbg", "ida", "ida64",
        "procmon", "procmon64", "processhacker", "wireshark",
        "fiddler", "httpdebuggerui", "httpdebuggersvc",
        "dnspy", "de4dot", "scylla", "scylla_x64", "scylla_x86",
        "ksdumper", "megadumper"
    };

    public sealed record Hit(string Name, int Pid);

    public IReadOnlyList<Hit> Scan()
    {
        var hits = new List<Hit>();
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var name = p.ProcessName.ToLowerInvariant();
                    if (KnownTools.Contains(name)) hits.Add(new Hit(p.ProcessName, p.Id));
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("scan", ex.Message);
        }
        return hits;
    }
}
