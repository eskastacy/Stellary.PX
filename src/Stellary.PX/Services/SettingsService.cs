using Stellary.PX.Infrastructure;
using Stellary.PX.Models;

namespace Stellary.PX.Services;

public sealed class SettingsService
{
    public AppSettings Current { get; private set; }
    public event EventHandler? Changed;

    public SettingsService()
    {
        Paths.Ensure();
        Current = JsonStore.Load(Paths.SettingsFile, new AppSettings());
    }

    public void Save()
    {
        JsonStore.Save(Paths.SettingsFile, Current);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reload()
    {
        Current = JsonStore.Load(Paths.SettingsFile, new AppSettings());
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
