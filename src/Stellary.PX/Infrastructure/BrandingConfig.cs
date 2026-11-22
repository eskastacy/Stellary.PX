using System.Text.Json;

namespace Stellary.PX.Infrastructure;

public sealed class SocialLink
{
    public string Platform { get; set; } = "";
    public string Url { get; set; } = "";

    /// <summary>Compact form for the settings list: no scheme, no query, no www.</summary>
    public string DisplayUrl
    {
        get
        {
            var text = Url;
            var query = text.IndexOf('?');
            if (query >= 0) text = text[..query];
            text = text.Replace("https://", "", StringComparison.OrdinalIgnoreCase)
                       .Replace("http://", "", StringComparison.OrdinalIgnoreCase);
            if (text.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) text = text[4..];
            return text.TrimEnd('/');
        }
    }
}

public sealed class BrandingConfig
{
    public string ProductName { get; set; } = "Stellary.PX";
    public string DeveloperName { get; set; } = "Eska Stacy";
    public string DeveloperBrand { get; set; } = "EskaProject-X";
    public string DeveloperHandle { get; set; } = "@jar.ajhi_";
    public string AvatarFile { get; set; } = "assets/avatar.png";
    public List<SocialLink> Links { get; set; } = new()
    {
        new() { Platform = "Linktree", Url = "https://linktr.ee/eskastacy_" },
        new() { Platform = "WhatsApp", Url = "https://whatsapp.com/channel/0029VbAowqoC1Fu3uTLcRe1n" },
        new() { Platform = "TikTok", Url = "https://www.tiktok.com/@jarachyy_?_r=1&_t=ZS-99b76hsvJha" },
        new() { Platform = "Discord", Url = "https://discord.gg/eZSNxNghu" },
        new() { Platform = "Instagram", Url = "https://www.instagram.com/jar.ajhi_" }
    };

    public string Url(string platform) =>
        Links.FirstOrDefault(l => string.Equals(l.Platform, platform, StringComparison.OrdinalIgnoreCase))?.Url ?? "";

    public static BrandingConfig Load()
    {
        var fallback = LoadEmbedded();
        var config = JsonStore.Load(Paths.BrandingFile, fallback);
        if (config.Links.Count == 0) config.Links = fallback.Links;
        return config;
    }

    /// <summary>
    /// Reads the branding shipped inside the assembly. A single-file release has
    /// no branding.json next to the executable, so the embedded copy is the
    /// baseline and a loose file still wins when the user edits one.
    /// </summary>
    private static BrandingConfig LoadEmbedded()
    {
        try
        {
            using var stream = typeof(BrandingConfig).Assembly
                .GetManifestResourceStream("Stellary.PX.Payload.branding.json");
            if (stream is null) return new BrandingConfig();

            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<BrandingConfig>(reader.ReadToEnd(), JsonStore.Options)
                   ?? new BrandingConfig();
        }
        catch
        {
            return new BrandingConfig();
        }
    }
}
