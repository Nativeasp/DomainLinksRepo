using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace DomainLinksDesktop;

public partial class DomainLinksBrainWindow : Window
{
    private const string BrainHostName = "domainlinks-brain.local";
    private readonly BrainLaunchContext _launchContext;
    private readonly DomainLinksDesktopSettings _settings;
    private readonly HttpClient _httpClient;
    private string? _pendingGraphJson;
    private bool _canvasReady;

    public DomainLinksBrainWindow()
        : this(BrainLaunchContext.InformationManagement)
    {
    }

    public DomainLinksBrainWindow(BrainLaunchContext launchContext)
    {
        InitializeComponent();
        _launchContext = launchContext ?? throw new ArgumentNullException(nameof(launchContext));
        _settings = DomainLinksDesktopSettings.Load();
        _httpClient = new HttpClient { BaseAddress = new Uri(_settings.BackendBaseUrl) };
        Title = string.IsNullOrWhiteSpace(launchContext.DisplayName)
            ? "DomainLinks Brain"
            : $"DomainLinks Brain — {launchContext.DisplayName}";
        UiScaleHelper.ApplyWindowScale(this, UiScaleHelper.Clamp(_settings.AppUiScale));
        Loaded += DomainLinksBrainWindow_OnLoaded;
        Closed += (_, _) => _httpClient.Dispose();
    }

    private async void DomainLinksBrainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await BackendAutoStarter.EnsureBackendIsAvailableAsync(_settings);
            LoadingDetailText.Text = $"Loading {_launchContext.DisplayName ?? _launchContext.Identifier}";
            await BrainWebView.EnsureCoreWebView2Async();
            UiScaleHelper.ApplyWebViewScale(BrainWebView, UiScaleHelper.Clamp(_settings.AppUiScale));
            BrainWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            BrainWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            BrainWebView.CoreWebView2.WebMessageReceived += BrainWebView_OnWebMessageReceived;
            var assetsPath = Path.Combine(AppContext.BaseDirectory, "WebShell", "Brain");
            BrainWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                BrainHostName,
                assetsPath,
                CoreWebView2HostResourceAccessKind.DenyCors);
            BrainWebView.NavigationCompleted += BrainWebView_OnNavigationCompleted;
            BrainWebView.Source = new Uri($"https://{BrainHostName}/brain.html");
            await LoadGraphAsync();
        }
        catch (Exception ex)
        {
            ShowLoadError(ex.Message);
        }
    }

    private async Task LoadGraphAsync()
    {
        var url = "/brain/graph"
            + $"?scopeKind={Uri.EscapeDataString(_launchContext.ScopeKindValue)}"
            + $"&scopeId={Uri.EscapeDataString(_launchContext.Identifier)}"
            + $"&includeDescendants={_launchContext.IncludeDescendants.ToString().ToLowerInvariant()}";
        using var response = await _httpClient.GetAsync(url);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ReadApiError(json, response.ReasonPhrase));
        }

        _pendingGraphJson = json;
        await SendPendingGraphAsync();
    }

    private async void BrainWebView_OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            ShowLoadError($"The Brain canvas could not load ({e.WebErrorStatus}).");
            return;
        }
        await Task.CompletedTask;
    }

    private async Task SendPendingGraphAsync()
    {
        if (!_canvasReady || _pendingGraphJson is null || BrainWebView.CoreWebView2 is null)
        {
            return;
        }
        BrainWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "graph-loaded",
            payload = JsonSerializer.Deserialize<JsonElement>(_pendingGraphJson),
            requestedFocusNodeId = _launchContext.FocusNodeId,
        }));
        _pendingGraphJson = null;
        LoadingPanel.Visibility = Visibility.Collapsed;
        await SendSemanticStatusAsync();
    }

    private async void BrainWebView_OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var message = JsonDocument.Parse(e.WebMessageAsJson);
            var root = message.RootElement;
            var type = root.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : null;
            if (type == "ready")
            {
                _canvasReady = true;
                await SendPendingGraphAsync();
            }
            else if (type == "expand-document" && root.TryGetProperty("documentId", out var idValue))
            {
                await ExpandDocumentAsync(idValue.GetString() ?? string.Empty);
            }
            else if (type == "embedding-command" && root.TryGetProperty("mode", out var modeValue))
            {
                await QueueSemanticEmbeddingsAsync(modeValue.GetString() ?? "pending");
            }
            else if (type == "close")
            {
                Close();
            }
        }
        catch (Exception ex)
        {
            PostError(ex.Message);
        }
    }

    private async Task ExpandDocumentAsync(string documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            return;
        }
        using var response = await _httpClient.GetAsync($"/brain/documents/{Uri.EscapeDataString(documentId)}/content-units");
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ReadApiError(json, response.ReasonPhrase));
        }
        BrainWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "document-expanded",
            payload = JsonSerializer.Deserialize<JsonElement>(json),
        }));
    }

    private async Task QueueSemanticEmbeddingsAsync(string mode)
    {
        using var response = await _httpClient.PostAsync(
            $"/semantic-embeddings/queue?mode={Uri.EscapeDataString(mode)}", null);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ReadApiError(json, response.ReasonPhrase));
        }
        await SendSemanticStatusAsync();
    }

    private async Task SendSemanticStatusAsync()
    {
        using var response = await _httpClient.GetAsync("/semantic-embeddings/status");
        if (!response.IsSuccessStatusCode)
        {
            return;
        }
        var json = await response.Content.ReadAsStringAsync();
        BrainWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "embedding-status",
            payload = JsonSerializer.Deserialize<JsonElement>(json),
        }));
    }

    private void PostError(string message) => BrainWebView.CoreWebView2?.PostWebMessageAsJson(
        JsonSerializer.Serialize(new { type = "error", message }));

    private void ShowLoadError(string message)
    {
        LoadingPanel.Visibility = Visibility.Visible;
        LoadingDetailText.Text = message;
    }

    private static string ReadApiError(string json, string? fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("detail", out var detail))
            {
                return detail.GetString() ?? fallback ?? "Brain request failed.";
            }
        }
        catch
        {
            // Fall through to the HTTP status text.
        }
        return fallback ?? "Brain request failed.";
    }
}
