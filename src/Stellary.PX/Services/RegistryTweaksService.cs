using Microsoft.Win32;
using Stellary.PX.Core;
using Stellary.PX.Models;

namespace Stellary.PX.Services;

public sealed class RegistryTweaksService
{
    private static readonly (string Path, string Name, int Value, string Label)[] Tweaks =
    {
        (@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, "Background capture"),
        (@"System\GameConfigStore", "GameDVR_Enabled", 0, "Game DVR"),
        (@"System\GameConfigStore", "GameDVR_FSEBehaviorMode", 2, "FSE behavior"),
        (@"System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode", 1, "Honor FSE"),
        (@"System\GameConfigStore", "GameDVR_DXGIHonorFSEWindowsCompatible", 1, "DXGI FSE compatibility"),
        (@"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1, "Game Mode"),
        (@"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0, "Game Bar overlay")
    };

    public int AppliedCount => Tweaks.Length;

    public StepResult Apply(AppSettings settings)
    {
        var applied = 0;
        try
        {
            foreach (var t in Tweaks)
            {
                var key = t.Path + "\\" + t.Name;
                using var reg = Registry.CurrentUser.CreateSubKey(t.Path, writable: true);
                if (reg is null) continue;

                if (!settings.RegistryBackup.ContainsKey(key))
                {
                    var existing = reg.GetValue(t.Name);
                    settings.RegistryBackup[key] = existing is int i
                        ? new RegBackupEntry { Existed = true, Dword = i }
                        : new RegBackupEntry { Existed = false };
                }

                reg.SetValue(t.Name, t.Value, RegistryValueKind.DWord);
                applied++;
            }

            Infrastructure.AppLog.Info("registry", $"{applied} values written");
            return StepResult.Success("Gaming registry", $"{applied} values applied");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error("registry", ex.Message);
            return StepResult.Fail("Gaming registry", $"{applied}/{Tweaks.Length} applied");
        }
    }

    public StepResult Restore(AppSettings settings)
    {
        var restored = 0;
        foreach (var t in Tweaks)
        {
            var key = t.Path + "\\" + t.Name;
            if (!settings.RegistryBackup.TryGetValue(key, out var backup)) continue;
            try
            {
                using var reg = Registry.CurrentUser.CreateSubKey(t.Path, writable: true);
                if (reg is null) continue;
                if (backup.Existed) reg.SetValue(t.Name, backup.Dword, RegistryValueKind.DWord);
                else reg.DeleteValue(t.Name, throwOnMissingValue: false);
                restored++;
            }
            catch (Exception ex)
            {
                Infrastructure.AppLog.Warn("registry", $"restore {key}: {ex.Message}");
            }
        }

        settings.RegistryBackup.Clear();
        return restored > 0
            ? StepResult.Success("Gaming registry", $"{restored} values restored")
            : StepResult.Skip("Gaming registry", "No backup recorded");
    }

    public bool AnyBackup(AppSettings settings) => settings.RegistryBackup.Count > 0;
}
