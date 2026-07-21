using System.IO;
using System.Text.Json;

namespace DomainLinksDesktop;

internal sealed record DomainLinksDesktopSettings
{
    public const string DefaultOcrModel = "glm-ocr:bf16";
    public const string DefaultContentGenerationModel = "qwen3.5:35b-mlx";

    public string BackendBaseUrl { get; init; } = "http://127.0.0.1:5056";
    public string OllamaBaseUrl { get; init; } = "http://10.211.55.2:11434";
    public string OcrModel { get; init; } = DefaultOcrModel;
    public string ContentGenerationModel { get; init; } = DefaultContentGenerationModel;
    public bool AutoStartLocalBackend { get; init; } = true;
    public string BackendRelativeWorkingDirectory { get; init; } = "DomainLinksBackend";
    public string BackendPythonExecutable { get; init; } = ".venv\\Scripts\\python.exe";
    public string BackendStartupArguments { get; init; } = "-m uvicorn app.main:app --reload --host 127.0.0.1 --port 5056";
    public bool AutoStartSemanticEmbeddingWorker { get; init; } = true;
    public string SemanticEmbeddingWorkerArguments { get; init; } = "-m app.semantic_worker --poll-seconds 15 --batch-size 16";
    public string[] BackendFallbackUrls { get; init; } =
    [
        "http://127.0.0.1:5056",
        "http://localhost:5056",
        "http://10.211.55.2:5056",
    ];
    public string[] OllamaFallbackUrls { get; init; } =
    [
        "http://10.211.55.2:11434",
        "http://127.0.0.1:11434",
        "http://localhost:11434",
    ];
    public double WindowWidth { get; init; } = 1420;
    public double WindowHeight { get; init; } = 820;
    public double WindowLeft { get; init; } = double.NaN;
    public double WindowTop { get; init; } = double.NaN;
    public double LeftPaneWidth { get; init; } = 280;
    public double RightPaneWidth { get; init; } = 320;
    public double PromptPaneHeight { get; init; } = 160;
    public double AppUiScale { get; init; } = 1.0;
    public double DomainStoreWindowWidth { get; init; } = 1500;
    public double DomainStoreWindowHeight { get; init; } = 860;
    public double DomainStoreWindowLeft { get; init; } = double.NaN;
    public double DomainStoreWindowTop { get; init; } = double.NaN;
    public double DomainStoreLeftPaneWidth { get; init; } = 300;
    public double DomainStoreCenterPaneWidth { get; init; } = 500;
    public double DomainStoreRightPaneWidth { get; init; } = 420;
    public double DomainStoreCollectionsPaneHeight { get; init; } = 260;
    public bool DomainStoreAiWritingAssistExpanded { get; init; } = true;
    public double DomainControlsBranchPaneHeight { get; init; } = 240;
    public double DomainControlsSuggestionPaneWidth { get; init; } = 460;
    public double PolicyWorkspacePoliciesPaneWidth { get; init; } = 430;
    public double PolicyWorkspaceControlSelectionPaneHeight { get; init; } = 220;
    public double OcrViewerWindowWidth { get; init; } = 1340;
    public double OcrViewerWindowHeight { get; init; } = 860;
    public double OcrViewerWindowLeft { get; init; } = double.NaN;
    public double OcrViewerWindowTop { get; init; } = double.NaN;
    public double OcrViewerPreviewPaneWidth { get; init; } = 720;
    public double DocumentTextWindowWidth { get; init; } = 960;
    public double DocumentTextWindowHeight { get; init; } = 720;
    public double DocumentTextWindowLeft { get; init; } = double.NaN;
    public double DocumentTextWindowTop { get; init; } = double.NaN;
    public double PolicyPresentationWindowWidth { get; init; } = 1180;
    public double PolicyPresentationWindowHeight { get; init; } = 860;
    public double PolicyPresentationWindowLeft { get; init; } = double.NaN;
    public double PolicyPresentationWindowTop { get; init; } = double.NaN;
    public double TextPromptWindowWidth { get; init; } = 760;
    public double TextPromptWindowHeight { get; init; } = 260;
    public double TextPromptWindowLeft { get; init; } = double.NaN;
    public double TextPromptWindowTop { get; init; } = double.NaN;
    public string LastSelectedModel { get; init; } = string.Empty;
    public string LastSelectedRetrievalMode { get; init; } = "FullContext";

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
                OcrModel = NormalizeText(settings.OcrModel, DefaultOcrModel),
                ContentGenerationModel = NormalizeText(settings.ContentGenerationModel, DefaultContentGenerationModel),
                AutoStartLocalBackend = settings.AutoStartLocalBackend,
                BackendRelativeWorkingDirectory = NormalizePath(settings.BackendRelativeWorkingDirectory, "DomainLinksBackend"),
                BackendPythonExecutable = NormalizePath(settings.BackendPythonExecutable, ".venv\\Scripts\\python.exe"),
                BackendStartupArguments = NormalizeText(settings.BackendStartupArguments, "-m uvicorn app.main:app --reload --host 127.0.0.1 --port 5056"),
                AutoStartSemanticEmbeddingWorker = settings.AutoStartSemanticEmbeddingWorker,
                SemanticEmbeddingWorkerArguments = NormalizeText(settings.SemanticEmbeddingWorkerArguments, "-m app.semantic_worker --poll-seconds 15 --batch-size 16"),
                BackendFallbackUrls = NormalizeUrls(settings.BackendFallbackUrls, DefaultBackendFallbackUrls()),
                OllamaFallbackUrls = NormalizeUrls(settings.OllamaFallbackUrls, DefaultOllamaFallbackUrls()),
                WindowWidth = settings.WindowWidth > 0 ? settings.WindowWidth : 1420,
                WindowHeight = settings.WindowHeight > 0 ? settings.WindowHeight : 820,
                WindowLeft = settings.WindowLeft,
                WindowTop = settings.WindowTop,
                LeftPaneWidth = settings.LeftPaneWidth > 0 ? settings.LeftPaneWidth : 280,
                RightPaneWidth = settings.RightPaneWidth > 0 ? settings.RightPaneWidth : 320,
                PromptPaneHeight = settings.PromptPaneHeight > 0 ? settings.PromptPaneHeight : 160,
                AppUiScale = settings.AppUiScale > 0 ? UiScaleHelper.Clamp(settings.AppUiScale) : UiScaleHelper.DefaultScale,
                DomainStoreWindowWidth = settings.DomainStoreWindowWidth > 0 ? settings.DomainStoreWindowWidth : 1500,
                DomainStoreWindowHeight = settings.DomainStoreWindowHeight > 0 ? settings.DomainStoreWindowHeight : 860,
                DomainStoreWindowLeft = settings.DomainStoreWindowLeft,
                DomainStoreWindowTop = settings.DomainStoreWindowTop,
                DomainStoreLeftPaneWidth = settings.DomainStoreLeftPaneWidth > 0 ? settings.DomainStoreLeftPaneWidth : 300,
                DomainStoreCenterPaneWidth = settings.DomainStoreCenterPaneWidth > 0 ? settings.DomainStoreCenterPaneWidth : 500,
                DomainStoreRightPaneWidth = settings.DomainStoreRightPaneWidth > 0 ? settings.DomainStoreRightPaneWidth : 420,
                DomainStoreCollectionsPaneHeight = settings.DomainStoreCollectionsPaneHeight > 0 ? settings.DomainStoreCollectionsPaneHeight : 260,
                DomainStoreAiWritingAssistExpanded = settings.DomainStoreAiWritingAssistExpanded,
                DomainControlsBranchPaneHeight = settings.DomainControlsBranchPaneHeight > 0 ? settings.DomainControlsBranchPaneHeight : 240,
                DomainControlsSuggestionPaneWidth = settings.DomainControlsSuggestionPaneWidth > 0 ? settings.DomainControlsSuggestionPaneWidth : 460,
                PolicyWorkspacePoliciesPaneWidth = settings.PolicyWorkspacePoliciesPaneWidth > 0 ? settings.PolicyWorkspacePoliciesPaneWidth : 430,
                PolicyWorkspaceControlSelectionPaneHeight = settings.PolicyWorkspaceControlSelectionPaneHeight > 0 ? settings.PolicyWorkspaceControlSelectionPaneHeight : 220,
                OcrViewerWindowWidth = settings.OcrViewerWindowWidth > 0 ? settings.OcrViewerWindowWidth : 1340,
                OcrViewerWindowHeight = settings.OcrViewerWindowHeight > 0 ? settings.OcrViewerWindowHeight : 860,
                OcrViewerWindowLeft = settings.OcrViewerWindowLeft,
                OcrViewerWindowTop = settings.OcrViewerWindowTop,
                OcrViewerPreviewPaneWidth = settings.OcrViewerPreviewPaneWidth > 0 ? settings.OcrViewerPreviewPaneWidth : 720,
                DocumentTextWindowWidth = settings.DocumentTextWindowWidth > 0 ? settings.DocumentTextWindowWidth : 960,
                DocumentTextWindowHeight = settings.DocumentTextWindowHeight > 0 ? settings.DocumentTextWindowHeight : 720,
                DocumentTextWindowLeft = settings.DocumentTextWindowLeft,
                DocumentTextWindowTop = settings.DocumentTextWindowTop,
                PolicyPresentationWindowWidth = settings.PolicyPresentationWindowWidth > 0 ? settings.PolicyPresentationWindowWidth : 1180,
                PolicyPresentationWindowHeight = settings.PolicyPresentationWindowHeight > 0 ? settings.PolicyPresentationWindowHeight : 860,
                PolicyPresentationWindowLeft = settings.PolicyPresentationWindowLeft,
                PolicyPresentationWindowTop = settings.PolicyPresentationWindowTop,
                TextPromptWindowWidth = settings.TextPromptWindowWidth > 0 ? settings.TextPromptWindowWidth : 760,
                TextPromptWindowHeight = settings.TextPromptWindowHeight > 0 ? settings.TextPromptWindowHeight : 260,
                TextPromptWindowLeft = settings.TextPromptWindowLeft,
                TextPromptWindowTop = settings.TextPromptWindowTop,
                LastSelectedModel = settings.LastSelectedModel ?? string.Empty,
                LastSelectedRetrievalMode = string.IsNullOrWhiteSpace(settings.LastSelectedRetrievalMode) ? "FullContext" : settings.LastSelectedRetrievalMode,
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
        var json = JsonSerializer.Serialize(ToPersistedSettings(), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(settingsPath, json);
    }

    private DomainLinksDesktopSettings ToPersistedSettings()
    {
        return this with
        {
            WindowWidth = PositiveOrDefault(WindowWidth, 1420),
            WindowHeight = PositiveOrDefault(WindowHeight, 820),
            WindowLeft = FiniteOrDefault(WindowLeft, 0),
            WindowTop = FiniteOrDefault(WindowTop, 0),
            OcrModel = NormalizeText(OcrModel, DefaultOcrModel),
            ContentGenerationModel = NormalizeText(ContentGenerationModel, DefaultContentGenerationModel),
            LeftPaneWidth = PositiveOrDefault(LeftPaneWidth, 280),
            RightPaneWidth = PositiveOrDefault(RightPaneWidth, 320),
            PromptPaneHeight = PositiveOrDefault(PromptPaneHeight, 160),
            AppUiScale = UiScaleHelper.Clamp(AppUiScale),
            DomainStoreWindowWidth = PositiveOrDefault(DomainStoreWindowWidth, 1500),
            DomainStoreWindowHeight = PositiveOrDefault(DomainStoreWindowHeight, 860),
            DomainStoreWindowLeft = FiniteOrDefault(DomainStoreWindowLeft, 0),
            DomainStoreWindowTop = FiniteOrDefault(DomainStoreWindowTop, 0),
            DomainStoreLeftPaneWidth = PositiveOrDefault(DomainStoreLeftPaneWidth, 300),
            DomainStoreCenterPaneWidth = PositiveOrDefault(DomainStoreCenterPaneWidth, 500),
            DomainStoreRightPaneWidth = PositiveOrDefault(DomainStoreRightPaneWidth, 420),
            DomainStoreCollectionsPaneHeight = PositiveOrDefault(DomainStoreCollectionsPaneHeight, 260),
            DomainControlsBranchPaneHeight = PositiveOrDefault(DomainControlsBranchPaneHeight, 240),
            DomainControlsSuggestionPaneWidth = PositiveOrDefault(DomainControlsSuggestionPaneWidth, 460),
            PolicyWorkspacePoliciesPaneWidth = PositiveOrDefault(PolicyWorkspacePoliciesPaneWidth, 430),
            PolicyWorkspaceControlSelectionPaneHeight = PositiveOrDefault(PolicyWorkspaceControlSelectionPaneHeight, 220),
            OcrViewerWindowWidth = PositiveOrDefault(OcrViewerWindowWidth, 1340),
            OcrViewerWindowHeight = PositiveOrDefault(OcrViewerWindowHeight, 860),
            OcrViewerWindowLeft = FiniteOrDefault(OcrViewerWindowLeft, 0),
            OcrViewerWindowTop = FiniteOrDefault(OcrViewerWindowTop, 0),
            OcrViewerPreviewPaneWidth = PositiveOrDefault(OcrViewerPreviewPaneWidth, 720),
            DocumentTextWindowWidth = PositiveOrDefault(DocumentTextWindowWidth, 960),
            DocumentTextWindowHeight = PositiveOrDefault(DocumentTextWindowHeight, 720),
            DocumentTextWindowLeft = FiniteOrDefault(DocumentTextWindowLeft, 0),
            DocumentTextWindowTop = FiniteOrDefault(DocumentTextWindowTop, 0),
            PolicyPresentationWindowWidth = PositiveOrDefault(PolicyPresentationWindowWidth, 1180),
            PolicyPresentationWindowHeight = PositiveOrDefault(PolicyPresentationWindowHeight, 860),
            PolicyPresentationWindowLeft = FiniteOrDefault(PolicyPresentationWindowLeft, 0),
            PolicyPresentationWindowTop = FiniteOrDefault(PolicyPresentationWindowTop, 0),
            TextPromptWindowWidth = PositiveOrDefault(TextPromptWindowWidth, 760),
            TextPromptWindowHeight = PositiveOrDefault(TextPromptWindowHeight, 260),
            TextPromptWindowLeft = FiniteOrDefault(TextPromptWindowLeft, 0),
            TextPromptWindowTop = FiniteOrDefault(TextPromptWindowTop, 0),
        };
    }

    private static string NormalizeUrl(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim().TrimEnd('/');
    }

    private static string NormalizePath(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }

    private static string NormalizeText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }

    private static string[] NormalizeUrls(string[]? values, string[] fallback)
    {
        var normalized = values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized is { Length: > 0 } ? normalized : fallback;
    }

    private static string[] DefaultBackendFallbackUrls() =>
    [
        "http://127.0.0.1:5056",
        "http://localhost:5056",
        "http://10.211.55.2:5056",
    ];

    private static string[] DefaultOllamaFallbackUrls() =>
    [
        "http://10.211.55.2:11434",
        "http://127.0.0.1:11434",
        "http://localhost:11434",
    ];

    private static double PositiveOrDefault(double value, double fallback)
    {
        return double.IsFinite(value) && value > 0 ? value : fallback;
    }

    private static double FiniteOrDefault(double value, double fallback)
    {
        return double.IsFinite(value) ? value : fallback;
    }
}
