using System.IO;
using System.Text.Json;

namespace DomainLinksDesktop;

internal sealed class DomainLinksDesktopSettings
{
    public string BackendBaseUrl { get; init; } = "http://127.0.0.1:5056";
    public string OllamaBaseUrl { get; init; } = "http://10.211.55.2:11434";
    public double WindowWidth { get; init; } = 1420;
    public double WindowHeight { get; init; } = 820;
    public double WindowLeft { get; init; } = double.NaN;
    public double WindowTop { get; init; } = double.NaN;
    public double LeftPaneWidth { get; init; } = 280;
    public double RightPaneWidth { get; init; } = 320;
    public double PromptPaneHeight { get; init; } = 160;
    public double DomainStoreWindowWidth { get; init; } = 1500;
    public double DomainStoreWindowHeight { get; init; } = 860;
    public double DomainStoreWindowLeft { get; init; } = double.NaN;
    public double DomainStoreWindowTop { get; init; } = double.NaN;
    public double DomainStoreLeftPaneWidth { get; init; } = 300;
    public double DomainStoreCenterPaneWidth { get; init; } = 500;
    public double DomainStoreCollectionsPaneHeight { get; init; } = 260;
    public string LastSelectedModel { get; init; } = string.Empty;

    public static DomainLinksDesktopSettings Load()
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "domainlinks-desktop.settings.json");
        if (!File.Exists(settingsPath))
        {
            return new DomainLinksDesktopSettings();
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            var settings = JsonSerializer.Deserialize<DomainLinksDesktopSettings>(json);
            if (settings is null)
            {
                return new DomainLinksDesktopSettings();
            }

            return new DomainLinksDesktopSettings
            {
                BackendBaseUrl = NormalizeUrl(settings.BackendBaseUrl, "http://127.0.0.1:5056"),
                OllamaBaseUrl = NormalizeUrl(settings.OllamaBaseUrl, "http://10.211.55.2:11434"),
                WindowWidth = settings.WindowWidth > 0 ? settings.WindowWidth : 1420,
                WindowHeight = settings.WindowHeight > 0 ? settings.WindowHeight : 820,
                WindowLeft = settings.WindowLeft,
                WindowTop = settings.WindowTop,
                LeftPaneWidth = settings.LeftPaneWidth > 0 ? settings.LeftPaneWidth : 280,
                RightPaneWidth = settings.RightPaneWidth > 0 ? settings.RightPaneWidth : 320,
                PromptPaneHeight = settings.PromptPaneHeight > 0 ? settings.PromptPaneHeight : 160,
                DomainStoreWindowWidth = settings.DomainStoreWindowWidth > 0 ? settings.DomainStoreWindowWidth : 1500,
                DomainStoreWindowHeight = settings.DomainStoreWindowHeight > 0 ? settings.DomainStoreWindowHeight : 860,
                DomainStoreWindowLeft = settings.DomainStoreWindowLeft,
                DomainStoreWindowTop = settings.DomainStoreWindowTop,
                DomainStoreLeftPaneWidth = settings.DomainStoreLeftPaneWidth > 0 ? settings.DomainStoreLeftPaneWidth : 300,
                DomainStoreCenterPaneWidth = settings.DomainStoreCenterPaneWidth > 0 ? settings.DomainStoreCenterPaneWidth : 500,
                DomainStoreCollectionsPaneHeight = settings.DomainStoreCollectionsPaneHeight > 0 ? settings.DomainStoreCollectionsPaneHeight : 260,
                LastSelectedModel = settings.LastSelectedModel ?? string.Empty,
            };
        }
        catch
        {
            return new DomainLinksDesktopSettings();
        }
    }

    public void Save()
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "domainlinks-desktop.settings.json");
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(settingsPath, json);
    }

    private static string NormalizeUrl(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim().TrimEnd('/');
    }
}
