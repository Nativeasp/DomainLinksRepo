using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using MarkdownTable = Markdig.Extensions.Tables.Table;
using MarkdownTableCell = Markdig.Extensions.Tables.TableCell;
using MarkdownTableRow = Markdig.Extensions.Tables.TableRow;

namespace DomainLinksDesktop;

public partial class MainWindow : Window
{
    private sealed record OcrExtractionResult(bool Success, string Text, string ErrorMessage);
    private sealed record ChatExchangeExportItem(int ExchangeNumber, ChatMessageItem? UserMessage, ChatMessageItem? AssistantMessage);
    private const string ShellHostName = "domainlinks-shell.local";

    private static readonly JsonSerializerOptions StreamJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private DomainLinksDesktopSettings _settings = DomainLinksDesktopSettings.Load();
    private readonly HttpClient _httpClient;
    private readonly HttpClient _ollamaHttpClient;
    private readonly ObservableCollection<CollectionItem> _projectCollections = [];
    private readonly ObservableCollection<DomainItem> _capabilityDomains = [];
    private readonly ObservableCollection<ModelOptionItem> _availableModels = [];
    private readonly DispatcherTimer _contextPreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly MarkdownPipeline _markdownPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    private readonly LocalChatStore _localChatStore = new();
    private readonly List<ChatThreadItem> _selectedChatThreads = [];
    private readonly GridLength _defaultPromptInputRowHeight = new(160);
    private readonly GridLength _defaultWorkingPaneHeaderSpacerRowHeight = new(12);
    private readonly GridLength _defaultPromptSplitterRowHeight = new(8);
    private readonly GridLength _defaultWorkPanelTopSpacerRowHeight = new(12);
    private double _appUiScale;
    private bool _isUpdatingContextSelection;
    private bool _isTopPanelExpanded;
    private bool _isThreadPanelExpanded;
    private bool _isStreamingResponseActive;
    private bool _isRefreshingContextPreview;
    private CollectionItem? _activeProjectCollection;
    private ChatThreadItem? _activeChatThread;
    private ChatThreadItem? _streamingThread;
    private string? _defaultChatModel;
    private ChatBackupService? _chatBackupService;
    private ChatBackupUserIdentity? _chatBackupUser;
    private DomainStoreWindow? _domainStoreWindow;

    public MainWindow()
    {
        InitializeComponent();
        Width = _settings.WindowWidth;
        Height = _settings.WindowHeight;
        if (!double.IsNaN(_settings.WindowLeft))
        {
            Left = _settings.WindowLeft;
        }
        if (!double.IsNaN(_settings.WindowTop))
        {
            Top = _settings.WindowTop;
        }
        _appUiScale = UiScaleHelper.Clamp(_settings.AppUiScale);
        UiScaleHelper.ApplyWindowScale(this, _appUiScale);
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_settings.BackendBaseUrl)
        };
        _ollamaHttpClient = new HttpClient
        {
            BaseAddress = new Uri(_settings.OllamaBaseUrl)
        };
        _chatBackupService = new ChatBackupService(_httpClient);
        ProjectCollectionsTreeView.ItemsSource = _projectCollections;
        DomainContextTreeView.ItemsSource = _capabilityDomains;
        ModelComboBox.ItemsSource = _availableModels;
        _contextPreviewTimer.Tick += ContextPreviewTimer_OnTick;
        Loaded += MainWindow_OnLoaded;
        Closing += MainWindow_OnClosing;
        PreviewKeyDown += MainWindow_OnPreviewKeyDown;
        UpdateTopPanelState();
        UpdateThreadPanelState();
        UpdateContextOptionState();
        UpdatePromptPlaceholderVisibility();
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        LeftPaneColumn.Width = new GridLength(_settings.LeftPaneWidth);
        RightPaneColumn.Width = new GridLength(_settings.RightPaneWidth);
        PromptInputRow.Height = new GridLength(_settings.PromptPaneHeight);
        await EnsureWebViewReadyAsync(ShellMenuWebView);
        UiScaleHelper.ApplyWebViewScale(ShellMenuWebView, _appUiScale);
        ShellMenuWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
        ShellMenuWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        ShellMenuWebView.CoreWebView2.WebMessageReceived += ShellMenuWebView_OnWebMessageReceived;
        ShellMenuWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            ShellHostName,
            AppContext.BaseDirectory,
            CoreWebView2HostResourceAccessKind.DenyCors);
        ShellMenuWebView.Source = new Uri($"https://{ShellHostName}/WebShell/menu-host.html");
        await EnsureWebViewReadyAsync(ResponseWebView);
        UiScaleHelper.ApplyWebViewScale(ResponseWebView, _appUiScale);
        ResponseWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
        ResponseWebView.CoreWebView2.WebMessageReceived += ResponseWebView_OnWebMessageReceived;
        ShowEmptyResponseState("Response output will appear here.");
        await BackendAutoStarter.EnsureBackendIsAvailableAsync(_settings);
        await ResolveConfiguredServiceUrlsAsync();
        OpenDomainStore();
        await LoadShellAsync();
        ScheduleContextPreviewRefresh();
    }

    private async Task ResolveConfiguredServiceUrlsAsync()
    {
        var backendBaseUrl = await NetworkEndpointResolver.ResolveHttpBaseUrlAsync(
            _settings.BackendBaseUrl,
            _settings.BackendFallbackUrls,
            "/health");
        var ollamaBaseUrl = await NetworkEndpointResolver.ResolveHttpBaseUrlAsync(
            _settings.OllamaBaseUrl,
            _settings.OllamaFallbackUrls,
            "/api/tags");

        _settings = _settings with
        {
            BackendBaseUrl = backendBaseUrl,
            OllamaBaseUrl = ollamaBaseUrl,
        };
        _httpClient.BaseAddress = new Uri(_settings.BackendBaseUrl);
        _ollamaHttpClient.BaseAddress = new Uri(_settings.OllamaBaseUrl);
    }

    private void MainWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var saved = DomainLinksDesktopSettings.Load() with
        {
            BackendBaseUrl = _settings.BackendBaseUrl,
            OllamaBaseUrl = _settings.OllamaBaseUrl,
            BackendFallbackUrls = _settings.BackendFallbackUrls,
            OllamaFallbackUrls = _settings.OllamaFallbackUrls,
            WindowWidth = Width,
            WindowHeight = Height,
            WindowLeft = Left,
            WindowTop = Top,
            LeftPaneWidth = LeftPaneColumn.ActualWidth,
            RightPaneWidth = RightPaneColumn.ActualWidth,
            PromptPaneHeight = PromptInputRow.ActualHeight,
            AppUiScale = _appUiScale,
            LastSelectedModel = ModelComboBox.SelectedValue as string ?? string.Empty,
        };
        saved.Save();
    }

    private static async Task EnsureWebViewReadyAsync(WebView2 webView, int maxAttempts = 4)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await webView.EnsureCoreWebView2Async();
                return;
            }
            catch (COMException ex) when ((uint)ex.HResult == 0x800700AA && attempt < maxAttempts)
            {
                await Task.Delay(150 * attempt);
            }
        }

        await webView.EnsureCoreWebView2Async();
    }

    private async Task LoadShellAsync()
    {
        try
        {
            var health = await _httpClient.GetFromJsonAsync<Dictionary<string, object>>("/health");
            var config = await _httpClient.GetFromJsonAsync<BackendConfigResponse>("/config");
            _defaultChatModel = config?.OllamaChatModel;
            BackendStatusTextBox.Text = "Connected";
            BackendDetailTextBox.Text = await BuildStatusDetailAsync(health);
            await LoadAvailableModelsAsync();

            var domains = await _httpClient.GetFromJsonAsync<List<DomainItem>>("/domains") ?? [];
            var collections = await _httpClient.GetFromJsonAsync<List<CollectionItem>>("/collections") ?? [];
            _chatBackupUser = _chatBackupService?.ResolveCurrentUser();

            _projectCollections.Clear();
            _capabilityDomains.Clear();
            BuildDomainTrees(domains, collections);

            await RestoreChatsFromBackupIfNeededAsync();
            RestoreLocalProjectChats();

            if (_projectCollections.Count > 0)
            {
                SelectTreeItem(_projectCollections[0]);
            }
            else
            {
                await ShowProjectCollectionStateAsync(null);
            }
        }
        catch (Exception ex)
        {
            BackendStatusTextBox.Text = "Unavailable";
            BackendDetailTextBox.Text = $"Backend URL: {_settings.BackendBaseUrl}\nBackend: {ex.Message}\nOllama URL: {_settings.OllamaBaseUrl}\nOllama: {await GetOllamaStatusAsync()}";
            await LoadAvailableModelsAsync();

            _projectCollections.Clear();
            _capabilityDomains.Clear();
            RestoreLocalProjectChats();
            if (_projectCollections.Count > 0)
            {
                var firstThread = _projectCollections[0].Threads.FirstOrDefault();
                SelectTreeItem(firstThread is not null ? firstThread : _projectCollections[0]);
            }
            else
            {
                await ShowProjectCollectionStateAsync(null);
            }
        }
    }

    private async Task LoadAvailableModelsAsync()
    {
        _availableModels.Clear();

        try
        {
            var payload = await _ollamaHttpClient.GetFromJsonAsync<OllamaTagsResponse>("/api/tags");
            if (payload?.Models is null || payload.Models.Count == 0)
            {
                return;
            }

            foreach (var model in payload.Models
                         .Where(item => !IsEmbeddingModel(item.Name))
                         .OrderBy(item => item.Size)
                         .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                _availableModels.Add(
                    new ModelOptionItem
                    {
                        Name = model.Name,
                        SizeBytes = model.Size,
                        DisplayText = $"{model.Name} ({FormatModelSize(model.Size)})",
                    }
                );
            }

            var preferred = _availableModels.FirstOrDefault(item =>
                string.Equals(item.Name, _settings.LastSelectedModel, StringComparison.OrdinalIgnoreCase))
                ?? _availableModels.FirstOrDefault(item =>
                string.Equals(item.Name, _defaultChatModel, StringComparison.OrdinalIgnoreCase));
            ModelComboBox.SelectedItem = preferred ?? _availableModels.FirstOrDefault();
        }
        catch
        {
            // Keep the app usable even if the model list cannot be loaded.
        }
    }

    private bool IsEmbeddingModel(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return false;
        }

        var normalized = modelName.Trim();
        if (string.Equals(normalized, _defaultChatModel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(normalized, "nomic-embed-text:v1.5", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalized.Contains("embed", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("embedding", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatModelSize(long sizeBytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = sizeBytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        var decimals = size >= 10 || unitIndex == 0 ? 0 : 1;
        var format = decimals == 0 ? "F0" : "F1";
        return $"{size.ToString(format, CultureInfo.InvariantCulture)} {units[unitIndex]}";
    }

    private void OpenOcrViewerMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        OpenOcrViewer();
    }

    private void OpenOcrViewer()
    {
        var viewer = new OcrViewerWindow(_settings.OllamaBaseUrl)
        {
            Owner = this,
        };
        viewer.Show();
    }

    private void ShellMenuWebView_OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeElement) ||
                !string.Equals(typeElement.GetString(), "shell-action", StringComparison.OrdinalIgnoreCase) ||
                !root.TryGetProperty("action", out var actionElement))
            {
                return;
            }

            HandleShellMenuAction(actionElement.GetString() ?? string.Empty);
        }
        catch
        {
            // Ignore malformed menu messages to keep the shell resilient.
        }
    }

    private void HandleShellMenuAction(string action)
    {
        switch (action)
        {
            case "open-ocr-viewer":
                OpenOcrViewer();
                break;
            case "show-projects":
                ShowProjectsFromShell();
                break;
            case "focus-knowledge":
                DomainContextTreeView.Focus();
                ShowEmptyResponseState("Capability Domains focused. Use the right-side checkboxes to include long-term context.");
                break;
            case "open-domain-store":
                OpenDomainStore();
                break;
            case "open-brain":
                OpenBrain();
                break;
            case "focus-prompt":
                PromptTextBox.Focus();
                PromptTextBox.SelectAll();
                break;
            case "new-project":
                AddRootButton_OnClick(AddRootButton, new RoutedEventArgs(Button.ClickEvent, AddRootButton));
                break;
            case "new-chat":
                AddChildHeaderButton_OnClick(AddChildHeaderButton, new RoutedEventArgs(Button.ClickEvent, AddChildHeaderButton));
                break;
            case "open-controls-report":
                OpenControlsReportInBrowser();
                break;
            case "open-policy-draft":
                OpenPolicyDraftInBrowser();
                break;
            case "open-llm-traces":
                OpenLlmTracesInBrowser();
                break;
            case "save-thread":
                SaveThreadButton_OnClick(SaveThreadButton, new RoutedEventArgs(Button.ClickEvent, SaveThreadButton));
                break;
            case "toggle-status":
                TopPanelToggleButton_OnClick(TopPanelToggleButton, new RoutedEventArgs(Button.ClickEvent, TopPanelToggleButton));
                break;
            case "ask":
                AskButton_OnClick(AskButton, new RoutedEventArgs(Button.ClickEvent, AskButton));
                break;
        }
    }

    private void ShowProjectsFromShell()
    {
        var target = _activeProjectCollection ?? _projectCollections.FirstOrDefault();
        if (target is null)
        {
            ShowEmptyResponseState("No project collection is available yet.");
            return;
        }

        SelectTreeItem(target);
        ProjectCollectionsTreeView.Focus();
    }

    private void OpenDomainStore()
    {
        if (_domainStoreWindow is not null)
        {
            if (_domainStoreWindow.IsVisible)
            {
                _domainStoreWindow.Activate();
                _domainStoreWindow.Focus();
                return;
            }

            _domainStoreWindow = null;
        }

        var latestSettings = DomainLinksDesktopSettings.Load() with
        {
            BackendBaseUrl = _settings.BackendBaseUrl,
            OllamaBaseUrl = _settings.OllamaBaseUrl,
            BackendFallbackUrls = _settings.BackendFallbackUrls,
            OllamaFallbackUrls = _settings.OllamaFallbackUrls,
        };

        _domainStoreWindow = new DomainStoreWindow(latestSettings)
        {
            Owner = this,
        };
        _domainStoreWindow.Closed += (_, _) => _domainStoreWindow = null;
        _domainStoreWindow.Show();
        _domainStoreWindow.Activate();
    }

    private void OpenControlsReportInBrowser()
    {
        try
        {
            var baseUrl = (_settings.BackendBaseUrl ?? string.Empty).TrimEnd('/');
            var reportUrl = $"{baseUrl}/reports/controls-smart";
            Process.Start(new ProcessStartInfo
            {
                FileName = reportUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ShowEmptyResponseState($"Could not open controls report.{Environment.NewLine}{ex.Message}");
        }
    }

    private void OpenBrain()
    {
        var window = new DomainLinksBrainWindow
        {
            Owner = this,
        };
        window.Show();
        window.Activate();
    }

    private void OpenPolicyDraftInBrowser()
    {
        try
        {
            var baseUrl = (_settings.BackendBaseUrl ?? string.Empty).TrimEnd('/');
            var modelName = (ModelComboBox.SelectedValue as string)
                ?? (ModelComboBox.SelectedItem as ModelOptionItem)?.Name
                ?? _defaultChatModel
                ?? "llama3.1:8b";
            var reportUrl =
                $"{baseUrl}/reports/policy-draft?" +
                $"&templatePath={Uri.EscapeDataString("Policy/Policy-Template-1.01.md")}" +
                $"&model={Uri.EscapeDataString(modelName)}";
            Process.Start(new ProcessStartInfo
            {
                FileName = reportUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ShowEmptyResponseState($"Could not open policy draft.{Environment.NewLine}{ex.Message}");
        }
    }

    private void OpenLlmTracesInBrowser()
    {
        try
        {
            var baseUrl = (_settings.BackendBaseUrl ?? string.Empty).TrimEnd('/');
            var reportUrl = $"{baseUrl}/debug/llm-traces";
            Process.Start(new ProcessStartInfo
            {
                FileName = reportUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ShowEmptyResponseState($"Could not open LLM traces.{Environment.NewLine}{ex.Message}");
        }
    }

    private async Task<string> BuildStatusDetailAsync(Dictionary<string, object>? health)
    {
        if (health is null)
        {
            return $"Backend URL: {_settings.BackendBaseUrl}\nBackend: no content\nOllama URL: {_settings.OllamaBaseUrl}\nOllama: {await GetOllamaStatusAsync()}";
        }

        var provider = health.GetValueOrDefault("default_provider")?.ToString() ?? "unknown";
        var databaseName = health.GetValueOrDefault("sql_database")?.ToString() ?? "unknown";
        var databaseSummary = "unknown";

        if (health.TryGetValue("database", out var databaseValue))
        {
            databaseSummary = ParseDatabaseStatus(databaseValue);
        }

        var ollamaSummary = await GetOllamaStatusAsync();
        return $"Backend URL: {_settings.BackendBaseUrl}\nProvider: {provider}\nDatabase: {databaseName} ({databaseSummary})\nOllama URL: {_settings.OllamaBaseUrl}\nOllama: {ollamaSummary}";
    }

    private static string ParseDatabaseStatus(object? databaseValue)
    {
        if (databaseValue is null)
        {
            return "unknown";
        }

        if (databaseValue is JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.Object)
            {
                if (json.TryGetProperty("reachable", out var reachableElement) &&
                    reachableElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    var reachable = reachableElement.GetBoolean();
                    if (reachable)
                    {
                        return "reachable";
                    }

                    if (json.TryGetProperty("error", out var errorElement))
                    {
                        return errorElement.GetString() ?? "unreachable";
                    }

                    return "unreachable";
                }
            }
        }

        return databaseValue.ToString() ?? "unknown";
    }

    private async Task<string> GetOllamaStatusAsync()
    {
        try
        {
            using var response = await _ollamaHttpClient.GetAsync("/api/tags");
            return response.IsSuccessStatusCode
                ? "reachable"
                : $"HTTP {(int)response.StatusCode}";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private async void ProjectRootLabel_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not CollectionItem collection)
        {
            return;
        }

        if (e.ClickCount != 2)
        {
            return;
        }

        await PromptRenameCollectionAsync(collection);
        e.Handled = true;
    }

    private void TreeNode_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        switch (element.DataContext)
        {
            case CollectionItem collection:
                ClearProjectSelectionFlags();
                collection.IsSelected = true;
                _activeProjectCollection = collection;
                _activeChatThread = null;
                break;
            case ChatThreadItem thread:
                ClearProjectSelectionFlags();
                thread.IsSelected = true;
                _activeChatThread = thread;
                _activeProjectCollection = thread.ParentCollection;
                break;
            default:
                return;
        }

        if (GetTreeViewItem(ProjectCollectionsTreeView, element.DataContext) is { } container)
        {
            container.IsSelected = true;
            container.Focus();
        }
    }

    private async void RenameRootMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var collection = ResolveCollectionFromMenuSender(sender);
        if (collection is null)
        {
            return;
        }

        ClearProjectSelectionFlags();
        collection.IsSelected = true;
        _activeProjectCollection = collection;
        _activeChatThread = null;
        SelectTreeItem(collection);
        await PromptRenameCollectionAsync(collection);
    }

    private async void DeleteRootMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var collection = ResolveCollectionFromMenuSender(sender);
        if (collection is null)
        {
            return;
        }

        ClearProjectSelectionFlags();
        collection.IsSelected = true;
        _activeProjectCollection = collection;
        _activeChatThread = null;
        SelectTreeItem(collection);
        await DeleteRootCollectionAsync(collection);
    }

    private async void RenameChildMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var thread = ResolveThreadFromMenuSender(sender);
        if (thread is null)
        {
            return;
        }

        ClearProjectSelectionFlags();
        thread.IsSelected = true;
        _activeChatThread = thread;
        _activeProjectCollection = thread.ParentCollection;
        SelectTreeItem(thread);
        await PromptRenameThreadAsync(thread);
    }

    private async void MergeSelectedChatsMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var thread = ResolveThreadFromMenuSender(sender);
        if (thread is null)
        {
            return;
        }

        if (!_selectedChatThreads.Contains(thread))
        {
            SetSingleMultiSelection(thread);
        }

        await MergeSelectedChatsAsync();
    }

    private async void DeleteChildMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var thread = ResolveThreadFromMenuSender(sender);
        if (thread is null)
        {
            return;
        }

        ClearProjectSelectionFlags();
        thread.IsSelected = true;
        _activeChatThread = thread;
        _activeProjectCollection = thread.ParentCollection;
        SelectTreeItem(thread);
        await DeleteChatThreadAsync(thread);
    }

    private void ChildThreadNode_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ChatThreadItem thread)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            SetSingleMultiSelection(thread);
            return;
        }

        ToggleMultiSelection(thread);
        e.Handled = true;

        if (GetTreeViewItem(ProjectCollectionsTreeView, thread) is { } container)
        {
            container.IsSelected = true;
            container.Focus();
        }
    }

    private async void AiRenameRootMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var collection = ResolveCollectionFromMenuSender(sender);
        if (collection is null)
        {
            return;
        }

        try
        {
            ShowEmptyResponseState($"Generating project name for {collection.DisplayName}...");
            var suggestedName = await GenerateCollectionNameAsync(collection);
            if (string.IsNullOrWhiteSpace(suggestedName))
            {
                ShowEmptyResponseState("AI rename returned no project name.");
                return;
            }

            collection.DisplayName = suggestedName;
            await CommitRootRenameAsync(collection);
            ActivateProjectCollection(collection);
            ShowEmptyResponseState($"Renamed project to: {collection.DisplayName}");
        }
        catch (Exception ex)
        {
            ShowEmptyResponseState($"AI rename failed:{Environment.NewLine}{ex.Message}");
        }
    }

    private async void AiRenameChildMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var thread = ResolveThreadFromMenuSender(sender);
        if (thread is null)
        {
            return;
        }

        try
        {
            ShowEmptyResponseState($"Generating chat title for {thread.Title}...");
            var suggestedName = await GenerateThreadTitleAsync(thread);
            if (string.IsNullOrWhiteSpace(suggestedName))
            {
                ShowEmptyResponseState("AI rename returned no chat title.");
                return;
            }

            thread.Title = suggestedName;
            if (thread.ParentCollection is not null)
            {
                await PersistCollectionChatsAsync(thread.ParentCollection, pushBackup: false);
            }

            ActivateChatThread(thread);
            if (ReferenceEquals(_activeChatThread, thread))
            {
                CollectionHeaderTextBlock.Text = $"Chat: {thread.Title}";
            }

            RefreshProjectTree();
            ShowEmptyResponseState($"Renamed chat to: {thread.Title}");
        }
        catch (Exception ex)
        {
            ShowEmptyResponseState($"AI rename failed:{Environment.NewLine}{ex.Message}");
        }
    }

    private static CollectionItem? ResolveCollectionFromMenuSender(object sender)
    {
        return (sender as MenuItem)?.CommandParameter as CollectionItem
            ?? (sender as FrameworkElement)?.DataContext as CollectionItem;
    }

    private static ChatThreadItem? ResolveThreadFromMenuSender(object sender)
    {
        return (sender as MenuItem)?.CommandParameter as ChatThreadItem
            ?? (sender as FrameworkElement)?.DataContext as ChatThreadItem;
    }

    private async void ChildThreadLabel_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ChatThreadItem thread)
        {
            return;
        }

        if (e.ClickCount != 2)
        {
            return;
        }

        await PromptRenameThreadAsync(thread);
        e.Handled = true;
    }

    private void ContextCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingContextSelection)
        {
            return;
        }

        if ((sender as FrameworkElement)?.DataContext is DomainItem domain)
        {
            ApplyDomainSelection(domain, domain.IsIncluded == true);
            RefreshAncestorSelectionStates(domain.ParentDomain);
        }
        else if ((sender as FrameworkElement)?.DataContext is CollectionItem collection)
        {
            RefreshAncestorSelectionStates(collection.ParentDomain);
        }

        UpdateIncludedContextSummary();
        ScheduleContextPreviewRefresh();
    }

    private async void ProjectCollectionsTreeView_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        switch (e.NewValue)
        {
            case CollectionItem collection:
                ClearMultiSelection();
                ClearProjectSelectionFlags();
                collection.IsSelected = true;
                _activeProjectCollection = collection;
                _activeChatThread = null;
                await ShowProjectCollectionStateAsync(collection);
                ScheduleContextPreviewRefresh();
                break;
            case ChatThreadItem thread:
                ClearProjectSelectionFlags();
                thread.IsSelected = true;
                _activeChatThread = thread;
                _activeProjectCollection = thread.ParentCollection;
                if (_isStreamingResponseActive && ReferenceEquals(_streamingThread, thread))
                {
                    SetCenterMode(isRootMode: false);
                    CollectionHeaderTextBlock.Text = $"Chat: {thread.Title}";
                    CollectionDetailTextBlock.Text = $"Continuing thread in {_activeProjectCollection?.DisplayName}. Select the root node again to start a new chat.";
                    CollectionContentsListBox.ItemsSource = thread.Messages.Select(message => $"{message.Role}: {message.Content}");
                    CenterModeTextBlock.Text = "Chat thread mode: asking continues this thread";
                    UpdateIncludedContextSummary();
                    ScheduleContextPreviewRefresh();
                    break;
                }
                await ShowChatThreadStateAsync(thread);
                ScheduleContextPreviewRefresh();
                break;
        }
    }

    private async void ProjectCollectionsTreeView_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Delete or Key.Back))
        {
            return;
        }

        var handled = await TryDeleteSelectedProjectTreeNodeFromKeyboardAsync();
        e.Handled = handled;
    }

    private async void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (e.Key is Key.OemPlus or Key.Add)
            {
                AdjustUiScale(UiScaleHelper.ScaleStep);
                e.Handled = true;
                return;
            }

            if (e.Key is Key.OemMinus or Key.Subtract)
            {
                AdjustUiScale(-UiScaleHelper.ScaleStep);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.D0 || e.Key == Key.NumPad0)
            {
                ResetUiScale();
                e.Handled = true;
                return;
            }
        }

        if (e.Key is not (Key.Delete or Key.Back))
        {
            return;
        }

        var handled = await TryDeleteSelectedProjectTreeNodeFromKeyboardAsync();
        e.Handled = handled;
    }

    private async Task ShowProjectCollectionStateAsync(CollectionItem? collection)
    {
        var activeCollection = collection ?? _projectCollections.FirstOrDefault();
        if (activeCollection is null)
        {
            CollectionHeaderTextBlock.Text = "Project Collection";
            CollectionDetailTextBlock.Text = "No project collection is available yet.";
            CollectionContentsListBox.ItemsSource = null;
            CenterModeTextBlock.Text = "Project collection mode";
            ShowEmptyResponseState("Response output will appear here.");
            SetCenterMode(isRootMode: true);
            ContextBudgetTextBlock.Text = "Context budget will appear here once there is a selected collection.";
            return;
        }

        SetCenterMode(isRootMode: true);
        CollectionHeaderTextBlock.Text = $"Collection: {activeCollection.DisplayName}";
        CollectionDetailTextBlock.Text = string.IsNullOrWhiteSpace(activeCollection.Description)
            ? $"Upload into collection code '{activeCollection.CollectionCode}' and chat against this Workspace Memory scope."
            : activeCollection.Description;
        var documents = await LoadDocumentsAsync(activeCollection.CollectionCode);
        CollectionContentsListBox.ItemsSource = documents.Count == 0
            ? []
            : documents;
        CenterModeTextBlock.Text = "Project collection mode: prompt + upload + optional long-term domain context";
        if (ResponseWebView.Source is null)
        {
            ShowEmptyResponseState("Asking from this root will create a new child chat thread.");
        }
        UpdateIncludedContextSummary();
        ScheduleContextPreviewRefresh();
    }

    private async Task ShowChatThreadStateAsync(ChatThreadItem thread)
    {
        SetCenterMode(isRootMode: false);
        CollectionHeaderTextBlock.Text = $"Chat: {thread.Title}";
        CollectionDetailTextBlock.Text = $"Continuing thread in {_activeProjectCollection?.DisplayName}. Select the root node again to start a new chat.";
        CollectionContentsListBox.ItemsSource = thread.Messages.Select(message => $"{message.Role}: {message.Content}");
        CenterModeTextBlock.Text = "Chat thread mode: asking continues this thread";
        RenderChatExchanges(thread);
        UpdateIncludedContextSummary();
        ScheduleContextPreviewRefresh();
        await ScrollToLastExchangeAsync();
    }

    private async void SaveThreadButton_OnClick(object sender, RoutedEventArgs e)
    {
        var thread = _activeChatThread;
        if (thread is null)
        {
            SetUploadFeedback("Select a chat thread first.", Brushes.Firebrick);
            return;
        }

        try
        {
            await SaveThreadExportAsync(thread);
            SetUploadFeedback($"Saved thread {thread.Title}.", Brushes.DarkGreen);
        }
        catch (Exception ex)
        {
            SetUploadFeedback($"Save failed: {ex.Message}", Brushes.Firebrick);
        }
    }

    private async Task<List<DocumentListItem>> LoadDocumentsAsync(string collectionCode)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<List<DocumentListItem>>($"/documents?collectionCode={Uri.EscapeDataString(collectionCode)}") ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task<List<ContentUnitListItem>> LoadDocumentChunksAsync(string documentId)
    {
        return await _httpClient.GetFromJsonAsync<List<ContentUnitListItem>>($"/documents/{Uri.EscapeDataString(documentId)}/chunks") ?? [];
    }

    private async void AskButton_OnClick(object sender, RoutedEventArgs e)
    {
        var shortMemoryCollection = _activeProjectCollection ?? GetActiveProjectCollection();
        if (shortMemoryCollection is null)
        {
            ShowEmptyResponseState("Select a project collection on the left first.");
            return;
        }

        AskButton.IsEnabled = false;
        ShowEmptyResponseState("Thinking...");
        try
        {
            var longTermCollections = EnumerateCapabilityCollections(_capabilityDomains)
                .Where(collection => collection.IsIncluded)
                .Select(collection => collection.CollectionCode)
                .ToList();

            var promptText = PromptTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(promptText))
            {
                ShowEmptyResponseState("Type a prompt first.");
                return;
            }

            var provisionalThreadTitle = BuildTemporaryThreadTitle(promptText);

            var selectedModel = ModelComboBox.SelectedValue as string;
            var selectedRetrievalMode = GetSelectedRetrievalMode();
            var startedAtUtc = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();

            var thread = _activeChatThread;
            var createdNewThread = false;
            if (thread is null)
            {
                thread = new ChatThreadItem
                {
                    Title = provisionalThreadTitle,
                    ParentCollection = shortMemoryCollection,
                    IsSelected = true,
                };
                ClearProjectSelectionFlags();
                shortMemoryCollection.IsExpanded = true;
                shortMemoryCollection.Threads.Add(thread);
                _activeChatThread = thread;
                shortMemoryCollection.IsSelected = false;
                thread.IsEditing = false;
                createdNewThread = true;
            }

            thread.Messages.Add(
                new ChatMessageItem
                {
                    Role = "User",
                    Content = promptText,
                    CreatedAtUtc = startedAtUtc,
                }
            );
            var pendingAssistantMessage = new ChatMessageItem
            {
                Role = "Assistant",
                Content = string.Empty,
                CreatedAtUtc = startedAtUtc,
            };
            thread.Messages.Add(pendingAssistantMessage);

            CollectionContentsListBox.ItemsSource = thread.Messages.Select(message => $"{message.Role}: {message.Content}");
            ClearProjectSelectionFlags();
            shortMemoryCollection.IsExpanded = true;
            thread.IsSelected = true;
            if (createdNewThread)
            {
                SelectTreeItem(thread);
            }

            if (ReferenceEquals(_activeChatThread, thread))
            {
                CollectionHeaderTextBlock.Text = $"Chat: {thread.Title}";
            }

            _isStreamingResponseActive = true;
            _streamingThread = thread;
            await NavigateResponseHtmlAsync(BuildStreamingThreadHtml(thread));
            await ScrollToLastExchangeAsync();

            var requestBody = new
            {
                prompt = promptText,
                shortMemoryCollectionCode = shortMemoryCollection.CollectionCode,
                longTermCollectionCodes = longTermCollections,
                retrievalMode = selectedRetrievalMode,
                selectedDomainCode = ResolveSelectedDomainCodeForChatContext(),
                includeDocuments = IncludeDocumentsCheckBox.IsChecked == true,
                includeRag = IncludeRagCheckBox.IsChecked == true,
                includePolicies = IncludePoliciesCheckBox.IsChecked == true,
                includeDomainContext = IncludeDomainContextCheckBox.IsChecked == true,
                includeControls = IncludeControlsCheckBox.IsChecked == true,
                model = selectedModel,
                history = thread.Messages
                    .Take(Math.Max(0, thread.Messages.Count - 2))
                    .Select(message => new { role = message.Role.ToLowerInvariant(), content = message.Content })
                    .ToList(),
            };

            using var streamRequest = new HttpRequestMessage(HttpMethod.Post, "/ask/stream")
            {
                Content = JsonContent.Create(requestBody)
            };
            using var response = await _httpClient.SendAsync(
                streamRequest,
                HttpCompletionOption.ResponseHeadersRead
            );
            string? askFailureMessage = null;
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                using var fallbackResponse = await _httpClient.PostAsJsonAsync("/ask", requestBody);
                fallbackResponse.EnsureSuccessStatusCode();

                var payload = await fallbackResponse.Content.ReadFromJsonAsync<AskResponse>();
                if (payload is null)
                {
                    askFailureMessage = "Backend returned no response body.";
                }
                else
                {
                    pendingAssistantMessage.Content = payload.Answer;
                    pendingAssistantMessage.SupplementalText = BuildSourceSummary(payload);
                    pendingAssistantMessage.Stats = BuildMessageStats(payload.Metrics, selectedModel, startedAtUtc, stopwatch.Elapsed);
                }
            }
            else
            {
                response.EnsureSuccessStatusCode();

                await using var responseStream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(responseStream);

                while (true)
                {
                    var line = await reader.ReadLineAsync();
                    if (line is null)
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var streamEvent = JsonSerializer.Deserialize<AskStreamEvent>(line, StreamJsonOptions);
                    if (streamEvent is null)
                    {
                        continue;
                    }

                    if (string.Equals(streamEvent.Type, "delta", StringComparison.OrdinalIgnoreCase))
                    {
                        pendingAssistantMessage.Content += streamEvent.Delta;
                        await AppendStreamingChunkAsync(streamEvent.Delta);
                        continue;
                    }

                    if (string.Equals(streamEvent.Type, "title", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (string.Equals(streamEvent.Type, "final", StringComparison.OrdinalIgnoreCase))
                    {
                        pendingAssistantMessage.Content = streamEvent.Answer;
                        pendingAssistantMessage.SupplementalText = BuildSourceSummary(
                            new AskResponse
                            {
                                Answer = streamEvent.Answer,
                                Sources = streamEvent.Sources,
                                RetrievalMode = streamEvent.RetrievalMode,
                                RetrievalWarning = streamEvent.RetrievalWarning,
                            }
                        );
                        pendingAssistantMessage.Stats = BuildMessageStats(streamEvent.Metrics, selectedModel, startedAtUtc, stopwatch.Elapsed);
                        break;
                    }

                    if (string.Equals(streamEvent.Type, "error", StringComparison.OrdinalIgnoreCase))
                    {
                        askFailureMessage = string.IsNullOrWhiteSpace(streamEvent.Error)
                            ? "The backend returned an empty error."
                            : streamEvent.Error;
                        break;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(askFailureMessage))
            {
                pendingAssistantMessage.Content = askFailureMessage;
            }
            else if (createdNewThread || string.Equals(thread.Title, provisionalThreadTitle, StringComparison.Ordinal))
            {
                var generatedThreadTitle = await GenerateThreadTitleAsync(thread);
                if (!string.IsNullOrWhiteSpace(generatedThreadTitle))
                {
                    thread.Title = generatedThreadTitle;
                    if (ReferenceEquals(_activeChatThread, thread))
                    {
                        CollectionHeaderTextBlock.Text = $"Chat: {thread.Title}";
                    }
                }
            }

            CollectionContentsListBox.ItemsSource = thread.Messages.Select(message => $"{message.Role}: {message.Content}");
            PromptTextBox.Text = string.Empty;
            UpdatePromptPlaceholderVisibility();
            await PersistCollectionChatsAsync(shortMemoryCollection);
            await ShowChatThreadStateAsync(thread);
        }
        catch (Exception ex)
        {
            ShowEmptyResponseState(ex.Message);
        }
        finally
        {
            _isStreamingResponseActive = false;
            _streamingThread = null;
            AskButton.IsEnabled = true;
        }
    }

    private void ClearPromptButton_OnClick(object sender, RoutedEventArgs e)
    {
        PromptTextBox.Text = string.Empty;
        UpdatePromptPlaceholderVisibility();
    }

    private async void AddRootButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var displayName = $"New Project {DateTime.Now:HHmmss}";
            var response = await _httpClient.PostAsJsonAsync(
                "/collections",
                new
                {
                    domainCode = "workspace-memory",
                    collectionCode = displayName,
                    displayName,
                    description = "New Workspace Memory collection.",
                }
            );
            response.EnsureSuccessStatusCode();
            var created = await response.Content.ReadFromJsonAsync<CollectionItem>();
            if (created is null)
            {
                ShowEmptyResponseState("Root creation returned no collection.");
                return;
            }

            created.IsExpanded = true;
            created.IsEditing = true;
            _projectCollections.Add(created);
            ClearProjectSelectionFlags();
            created.IsSelected = true;
            _activeProjectCollection = created;
            _activeChatThread = null;
            SelectTreeItem(created);
            ShowEmptyResponseState($"Created new project root: {created.DisplayName}");
        }
        catch (Exception ex)
        {
            ShowEmptyResponseState($"Root create failed:{Environment.NewLine}{ex.Message}");
        }
    }

    private async void DeleteRootButton_OnClick(object sender, RoutedEventArgs e)
    {
        var collection =
            ProjectCollectionsTreeView.SelectedItem as CollectionItem
            ?? (ProjectCollectionsTreeView.SelectedItem as ChatThreadItem)?.ParentCollection
            ?? _activeProjectCollection
            ?? _activeChatThread?.ParentCollection
            ?? GetActiveProjectCollection();
        if (collection is null)
        {
            ShowEmptyResponseState("Select a root collection to delete.");
            return;
        }

        await DeleteRootCollectionAsync(collection);
    }

    private async Task DeleteRootCollectionAsync(CollectionItem collection)
    {
        var confirmation = MessageBox.Show(
            this,
            $"Delete project '{collection.DisplayName}' and all of its chats?",
            "Delete Project",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var response = await _httpClient.DeleteAsync($"/collections/{Uri.EscapeDataString(collection.CollectionCode)}");
            response.EnsureSuccessStatusCode();
            _localChatStore.DeleteCollection(collection.CollectionCode);
            if (_chatBackupService is not null && _chatBackupUser is not null)
            {
                try
                {
                    await _chatBackupService.DeleteBackupAsync(_chatBackupUser, collection.CollectionCode, collection.DisplayName);
                }
                catch
                {
                    // Local delete still stands even if remote backup cleanup fails.
                }
            }
            _projectCollections.Remove(collection);
            _activeProjectCollection = null;
            _activeChatThread = null;
            ClearProjectSelectionFlags();

            if (_projectCollections.Count > 0)
            {
                SelectTreeItem(_projectCollections[0]);
            }
            else
            {
                await ShowProjectCollectionStateAsync(null);
            }

            ShowEmptyResponseState($"Deleted root collection: {collection.DisplayName}");
        }
        catch (Exception ex)
        {
            ShowEmptyResponseState($"Root delete failed:{Environment.NewLine}{ex.Message}");
        }
    }

    private async Task PromptRenameCollectionAsync(CollectionItem collection)
    {
        var prompt = new TextPromptWindow(
            "Rename Project",
            "Enter a new project name.",
            collection.DisplayName,
            "This renames the project collection shown in the tree.")
        {
            Owner = this
        };

        if (prompt.ShowDialog() != true)
        {
            return;
        }

        var originalName = collection.DisplayName;
        collection.DisplayName = prompt.ResultText.Trim();
        await CommitRootRenameAsync(collection);
        ActivateProjectCollection(collection);

        if (ReferenceEquals(_activeProjectCollection, collection))
        {
            CollectionHeaderTextBlock.Text = $"Collection: {collection.DisplayName}";
        }

        if (!string.Equals(collection.DisplayName, originalName, StringComparison.Ordinal))
        {
            ShowEmptyResponseState($"Renamed project to: {collection.DisplayName}");
        }
    }

    private async void AddChildHeaderButton_OnClick(object sender, RoutedEventArgs e)
    {
        var parent = _activeProjectCollection ?? GetActiveProjectCollection();
        if (parent is null)
        {
            ShowEmptyResponseState("Select a root collection first.");
            return;
        }

        var newThread = new ChatThreadItem
        {
            Title = "New Chat",
            ParentCollection = parent,
            IsSelected = true,
            IsEditing = true,
        };

        ClearProjectSelectionFlags();
        parent.IsExpanded = true;
        parent.Threads.Add(newThread);
        _activeProjectCollection = parent;
        _activeChatThread = newThread;
        SelectTreeItem(newThread);
        await PersistCollectionChatsAsync(parent, pushBackup: false);
    }

    private async void DeleteChildHeaderButton_OnClick(object sender, RoutedEventArgs e)
    {
        var thread = _activeChatThread;
        if (thread?.ParentCollection is null)
        {
            ShowEmptyResponseState("Select a child chat to delete it.");
            return;
        }

        await DeleteChatThreadAsync(thread);
    }

    private async void AddChildThreadButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ChatThreadItem thread || thread.ParentCollection is null)
        {
            return;
        }

        var newThread = new ChatThreadItem
        {
            Title = "New Chat",
            ParentCollection = thread.ParentCollection,
            IsSelected = true,
            IsEditing = true,
        };
        ClearProjectSelectionFlags();
        thread.ParentCollection.IsExpanded = true;
        thread.ParentCollection.Threads.Add(newThread);
        _activeProjectCollection = thread.ParentCollection;
        _activeChatThread = newThread;
        SelectTreeItem(newThread);
        await PersistCollectionChatsAsync(thread.ParentCollection, pushBackup: false);
    }

    private async void DeleteChildThreadButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ChatThreadItem thread || thread.ParentCollection is null)
        {
            return;
        }

        await DeleteChatThreadAsync(thread);
    }

    private async Task DeleteChatThreadAsync(ChatThreadItem thread)
    {
        if (thread.ParentCollection is null)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"Delete chat '{thread.Title}' from project '{thread.ParentCollection.DisplayName}'?",
            "Delete Chat",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        var parent = thread.ParentCollection;
        _selectedChatThreads.Remove(thread);
        parent.Threads.Remove(thread);
        _activeProjectCollection = parent;
        _activeChatThread = null;
        ClearProjectSelectionFlags();
        SelectTreeItem(parent);
        await PersistCollectionChatsAsync(parent);
        ShowEmptyResponseState($"Deleted thread: {thread.Title}");
    }

    private async Task PromptRenameThreadAsync(ChatThreadItem thread)
    {
        var prompt = new TextPromptWindow(
            "Rename Chat",
            "Enter a new chat name.",
            thread.Title,
            "This renames the selected chat thread.")
        {
            Owner = this
        };

        if (prompt.ShowDialog() != true)
        {
            return;
        }

        var originalTitle = thread.Title;
        thread.Title = prompt.ResultText.Trim();
        if (thread.ParentCollection is not null)
        {
            await PersistCollectionChatsAsync(thread.ParentCollection, pushBackup: false);
        }

        ActivateChatThread(thread);
        if (ReferenceEquals(_activeChatThread, thread))
        {
            CollectionHeaderTextBlock.Text = $"Chat: {thread.Title}";
        }

        RefreshProjectTree();
        if (!string.Equals(thread.Title, originalTitle, StringComparison.Ordinal))
        {
            ShowEmptyResponseState($"Renamed chat to: {thread.Title}");
        }
    }

    private async Task MergeSelectedChatsAsync()
    {
        var threads = _selectedChatThreads
            .Where(thread => thread.ParentCollection is not null)
            .Distinct()
            .ToList();

        if (threads.Count < 2)
        {
            ShowEmptyResponseState("Ctrl+select at least two chats in the same project, then merge them.");
            return;
        }

        var target = threads[0];
        var parent = target.ParentCollection;
        if (parent is null || threads.Any(thread => !ReferenceEquals(thread.ParentCollection, parent)))
        {
            ShowEmptyResponseState("Only chats from the same project can be merged together.");
            return;
        }

        var mergeTitles = string.Join(", ", threads.Skip(1).Select(thread => thread.Title));
        var confirmation = MessageBox.Show(
            this,
            $"Merge {threads.Count} chats into '{target.Title}'?{Environment.NewLine}{Environment.NewLine}Merged in order: {string.Join(" -> ", threads.Select(thread => thread.Title))}{Environment.NewLine}{Environment.NewLine}These chats will be removed after their messages are appended.",
            "Merge Chats",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (var thread in threads.Skip(1))
        {
            foreach (var message in thread.Messages)
            {
                target.Messages.Add(CloneChatMessage(message));
            }
        }

        foreach (var thread in threads.Skip(1).ToList())
        {
            parent.Threads.Remove(thread);
        }

        ClearMultiSelection();
        ActivateChatThread(target);
        await PersistCollectionChatsAsync(parent);
        await ShowChatThreadStateAsync(target);
        SetUploadFeedback($"Merged {threads.Count} chats into '{target.Title}'.", Brushes.DarkGreen);
    }

    private async void UploadButton_OnClick(object sender, RoutedEventArgs e)
    {
        var activeCollection = GetActiveProjectCollection();
        if (activeCollection is null)
        {
            ShowEmptyResponseState("Select a project collection on the left before uploading.");
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Upload Document Into Project Collection",
            Filter = "Supported files|*.txt;*.md;*.pdf;*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.webp|All files|*.*",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != true)
        {
            ShowEmptyResponseState("Upload cancelled.");
            return;
        }

        UploadButton.IsEnabled = false;
        SetUploadFeedback("Preparing upload...");
        try
        {
            HttpResponseMessage response;
            var extension = Path.GetExtension(dialog.FileName);
            if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                response = await UploadPdfDocumentAsync(activeCollection.CollectionCode, dialog.FileName);
            }
            else if (IsImageUpload(extension))
            {
                SetUploadFeedback("Running OCR on image...");
                var ocrResult = await ExtractTextFromDocumentWithOcrAsync(dialog.FileName);
                if (!ocrResult.Success)
                {
                    ShowUploadFailure(ocrResult.ErrorMessage);
                    return;
                }

                SetUploadFeedback("Saving extracted text...");
                response = await UploadTextDocumentAsync(
                    activeCollection.CollectionCode,
                    Path.GetFileName(dialog.FileName),
                    ocrResult.Text,
                    "image_upload_ocr"
                );
            }
            else
            {
                SetUploadFeedback("Reading text file...");
                var bodyText = await File.ReadAllTextAsync(dialog.FileName);
                SetUploadFeedback("Saving text document...");
                response = await UploadTextDocumentAsync(
                    activeCollection.CollectionCode,
                    Path.GetFileName(dialog.FileName),
                    bodyText,
                    "file_upload"
                );
            }
            if (!response.IsSuccessStatusCode)
            {
                var errorDetail = await ReadErrorDetailAsync(response);
                ShowUploadFailure(errorDetail);
                return;
            }

            await ShowProjectCollectionStateAsync(activeCollection);
            SetUploadFeedback($"Upload complete into {activeCollection.DisplayName}.", Brushes.DarkGreen);
        }
        catch (Exception ex)
        {
            SetUploadFeedback($"Upload failed: {ex.Message}", Brushes.Firebrick);
        }
        finally
        {
            UploadButton.IsEnabled = true;
        }
    }

    private static bool IsImageUpload(string? extension)
    {
        return extension is not null && extension.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tif" or ".tiff" or ".webp";
    }

    private async Task<HttpResponseMessage> UploadPdfDocumentAsync(string collectionCode, string filePath)
    {
        SetUploadFeedback("Uploading PDF...");
        using var form = new MultipartFormDataContent();
        await using var fileStream = File.OpenRead(filePath);
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", Path.GetFileName(filePath));

        var response = await _httpClient.PostAsync(
            $"/documents/pdf?collectionCode={Uri.EscapeDataString(collectionCode)}",
            form
        );
        if (response.IsSuccessStatusCode || response.StatusCode != HttpStatusCode.BadRequest)
        {
            return response;
        }

        var errorBody = await response.Content.ReadAsStringAsync();
        if (!errorBody.Contains("No usable text was extracted from the PDF.", StringComparison.OrdinalIgnoreCase))
        {
            return response;
        }

        SetUploadFeedback("PDF has no text layer. Running OCR...");
        var ocrResult = await ExtractTextFromDocumentWithOcrAsync(filePath);
        if (!ocrResult.Success)
        {
            response.Dispose();
            return CreateUploadErrorResponse(ocrResult.ErrorMessage);
        }

        SetUploadFeedback("OCR complete. Saving extracted text...");
        response.Dispose();
        return await UploadTextDocumentAsync(collectionCode, Path.GetFileName(filePath), ocrResult.Text, "pdf_upload_ocr");
    }

    private async Task<HttpResponseMessage> UploadTextDocumentAsync(
        string collectionCode,
        string sourceName,
        string bodyText,
        string sourceType)
    {
        return await _httpClient.PostAsJsonAsync(
            "/documents/text",
            new
            {
                collectionCode,
                sourceName,
                bodyText,
                sourceType,
            }
        );
    }

    private static async Task<OcrExtractionResult> ExtractTextFromDocumentWithOcrAsync(string filePath)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "ExtractDocumentText.ps1");
        if (!File.Exists(scriptPath))
        {
            return new OcrExtractionResult(false, string.Empty, $"OCR helper script was not found: {scriptPath}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-FilePath");
        startInfo.ArgumentList.Add(filePath);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var standardOutput = (await standardOutputTask).Trim();
        var standardError = (await standardErrorTask).Trim();
        if (!string.IsNullOrWhiteSpace(standardOutput))
        {
            return new OcrExtractionResult(true, standardOutput, string.Empty);
        }

        if (process.ExitCode != 0)
        {
            return new OcrExtractionResult(
                false,
                string.Empty,
                string.IsNullOrWhiteSpace(standardError)
                    ? "Windows OCR failed for that document."
                    : standardError);
        }

        if (string.IsNullOrWhiteSpace(standardOutput))
        {
            return new OcrExtractionResult(false, string.Empty, "OCR could not detect readable text in that document.");
        }

        return new OcrExtractionResult(true, standardOutput, string.Empty);
    }

    private static HttpResponseMessage CreateUploadErrorResponse(string message)
    {
        return new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new { detail = message }),
        };
    }

    private void ShowUploadFailure(string message)
    {
        SetUploadFeedback($"Upload failed: {message}", Brushes.Firebrick);
    }

    private static async Task<string> ReadErrorDetailAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"Upload request failed with HTTP {(int)response.StatusCode}.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("detail", out var detailElement))
            {
                var detail = detailElement.GetString();
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    return detail;
                }
            }
        }
        catch
        {
            // Fall back to the raw response body when it is not JSON.
        }

        return body;
    }

    private void SetUploadFeedback(string message, Brush? foreground = null)
    {
        if (UploadStatusTextBlock is null)
        {
            return;
        }

        UploadStatusTextBlock.Text = message;
        UploadStatusTextBlock.Foreground = foreground ?? new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString("#5B6770"));
        UploadStatusTextBlock.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private async void DeleteDocumentButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DocumentListItem document)
        {
            return;
        }

        var activeCollection = GetActiveProjectCollection();
        if (activeCollection is null)
        {
            ShowEmptyResponseState("Select a project collection first.");
            return;
        }

        try
        {
            var response = await _httpClient.DeleteAsync($"/documents/{Uri.EscapeDataString(document.DocumentId)}");
            response.EnsureSuccessStatusCode();
            await ShowProjectCollectionStateAsync(activeCollection);
            ShowEmptyResponseState($"Deleted document: {document.SourceName}");
        }
        catch (Exception ex)
        {
            ShowEmptyResponseState($"Document delete failed:{Environment.NewLine}{ex.Message}");
        }
    }

    private async void CopyDocumentTextButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DocumentListItem document)
        {
            return;
        }

        try
        {
            SetUploadFeedback($"Loading text for {document.SourceName}...");
            var fullText = await GetDocumentFullTextAsync(document);
            if (string.IsNullOrWhiteSpace(fullText))
            {
                SetUploadFeedback($"No extracted text is available for {document.SourceName}.", Brushes.Firebrick);
                return;
            }

            Clipboard.SetText(fullText);
            SetUploadFeedback($"Copied full text for {document.SourceName}.", Brushes.DarkGreen);
        }
        catch (Exception ex)
        {
            SetUploadFeedback($"Copy failed: {ex.Message}", Brushes.Firebrick);
        }
    }

    private async void SaveDocumentTextButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DocumentListItem document)
        {
            return;
        }

        try
        {
            SetUploadFeedback($"Loading text for {document.SourceName}...");
            var fullText = await GetDocumentFullTextAsync(document);
            if (string.IsNullOrWhiteSpace(fullText))
            {
                SetUploadFeedback($"No extracted text is available for {document.SourceName}.", Brushes.Firebrick);
                return;
            }

            var suggestedFileName = Path.GetFileNameWithoutExtension(document.SourceName);
            if (string.IsNullOrWhiteSpace(suggestedFileName))
            {
                suggestedFileName = "document-text";
            }

            var dialog = new SaveFileDialog
            {
                Title = "Save Extracted Document Text",
                FileName = $"{suggestedFileName}.txt",
                Filter = "Text files|*.txt|All files|*.*",
                AddExtension = true,
                DefaultExt = ".txt",
                OverwritePrompt = true,
            };

            if (dialog.ShowDialog(this) != true)
            {
                SetUploadFeedback("Save cancelled.");
                return;
            }

            await File.WriteAllTextAsync(dialog.FileName, fullText);
            SetUploadFeedback($"Saved full text for {document.SourceName}.", Brushes.DarkGreen);
        }
        catch (Exception ex)
        {
            SetUploadFeedback($"Save failed: {ex.Message}", Brushes.Firebrick);
        }
    }

    private async void DocumentExpander_OnExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Expander ||
            sender is not Expander expander ||
            expander.DataContext is not DocumentListItem document ||
            document.Chunks.Count > 0)
        {
            return;
        }

        try
        {
            var chunks = await LoadDocumentChunksAsync(document.DocumentId);
            document.Chunks.Clear();
            foreach (var chunk in chunks)
            {
                document.Chunks.Add(chunk);
            }
        }
        catch (Exception ex)
        {
            ShowEmptyResponseState($"Chunk load failed:{Environment.NewLine}{ex.Message}");
        }
    }

    private async Task<string> GetDocumentFullTextAsync(DocumentListItem document)
    {
        if (document.Chunks.Count == 0)
        {
            var chunks = await LoadDocumentChunksAsync(document.DocumentId);
            document.Chunks.Clear();
            foreach (var chunk in chunks)
            {
                document.Chunks.Add(chunk);
            }
        }

        return string.Join(
            $"{Environment.NewLine}{Environment.NewLine}",
            document.Chunks
                .OrderBy(chunk => chunk.UnitOrdinal)
                .Select(chunk => chunk.BodyText?.Trim())
                .Where(bodyText => !string.IsNullOrWhiteSpace(bodyText)));
    }

    private async void DeleteChunkButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ContentUnitListItem chunk)
        {
            return;
        }

        var activeCollection = GetActiveProjectCollection();
        if (activeCollection is null)
        {
            ShowEmptyResponseState("Select a project collection first.");
            return;
        }

        try
        {
            var response = await _httpClient.DeleteAsync($"/content-units/{Uri.EscapeDataString(chunk.ContentUnitId)}");
            response.EnsureSuccessStatusCode();
            await ShowProjectCollectionStateAsync(activeCollection);
            ShowEmptyResponseState($"Deleted chunk {chunk.UnitOrdinal}.");
        }
        catch (Exception ex)
        {
            ShowEmptyResponseState($"Chunk delete failed:{Environment.NewLine}{ex.Message}");
        }
    }

    private void RootContentScrollViewer_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (RootContentScrollViewer is null)
        {
            return;
        }

        RootContentScrollViewer.ScrollToVerticalOffset(
            RootContentScrollViewer.VerticalOffset - (e.Delta / 3.0)
        );
        e.Handled = true;
    }

    private void ChunkScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - (e.Delta / 3.0));
        e.Handled = true;
    }

    private void UpdateIncludedContextSummary()
    {
        if (IncludedContextTextBlock is null)
        {
            return;
        }

        var includedCollections = EnumerateCapabilityCollections(_capabilityDomains)
            .Where(collection => collection.IsIncluded)
            .Select(collection => $"{collection.DomainDisplayName} / {collection.DisplayName}")
            .ToList();
        var enabledSources = GetEnabledContextSources();
        var sourceSummary = enabledSources.Count == 0
            ? "No context sources selected"
            : $"Context sources: {string.Join(", ", enabledSources)}";

        IncludedContextTextBlock.Text = includedCollections.Count == 0
            ? sourceSummary
            : $"{sourceSummary}. Included capability context: {string.Join("; ", includedCollections)}";
    }

    private void BuildDomainTrees(List<DomainItem> domains, List<CollectionItem> collections)
    {
        var domainLookup = domains.ToDictionary(domain => domain.DomainId, StringComparer.OrdinalIgnoreCase);

        foreach (var domain in domains)
        {
            domain.ParentDomain = null;
            domain.ChildDomains.Clear();
            domain.Collections.Clear();
            domain.TreeChildren.Clear();
            domain.IsExpanded = false;
            domain.IsIncluded = false;
        }

        foreach (var collection in collections)
        {
            collection.ParentDomain = null;
            var domain = domains.FirstOrDefault(item =>
                string.Equals(item.DomainCode, collection.DomainCode, StringComparison.OrdinalIgnoreCase));
            if (domain is null)
            {
                continue;
            }

            collection.ParentDomain = domain;
            domain.Collections.Add(collection);
        }

        foreach (var domain in domains)
        {
            if (!string.IsNullOrWhiteSpace(domain.DomainParentId)
                && domainLookup.TryGetValue(domain.DomainParentId, out var parentDomain))
            {
                domain.ParentDomain = parentDomain;
                parentDomain.ChildDomains.Add(domain);
            }
        }

        foreach (var domain in domains)
        {
            foreach (var childDomain in domain.ChildDomains
                         .OrderBy(item => item.DisplayOrder)
                         .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                domain.TreeChildren.Add(childDomain);
            }

            foreach (var collection in domain.Collections.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                domain.TreeChildren.Add(collection);
            }
        }

        foreach (var group in domains
                     .Where(domain =>
                         !string.Equals(domain.DomainCode, "workspace-memory", StringComparison.OrdinalIgnoreCase)
                         && string.IsNullOrWhiteSpace(domain.DomainParentId))
                     .GroupBy(domain => string.IsNullOrWhiteSpace(domain.DomainType) ? "Unclassified" : domain.DomainType)
                     .OrderBy(group => GetDomainTypeSortBucket(group.Key))
                     .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var groupNode = CreateDomainTypeGroup(group.Key);
            foreach (var rootDomain in group
                         .OrderBy(domain => domain.DisplayOrder)
                         .ThenBy(domain => domain.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                rootDomain.ParentDomain = groupNode;
                groupNode.ChildDomains.Add(rootDomain);
                groupNode.TreeChildren.Add(rootDomain);
            }

            _capabilityDomains.Add(groupNode);
        }

        foreach (var domain in _capabilityDomains)
        {
            RefreshDomainSelectionState(domain);
        }

        foreach (var collection in collections
                     .Where(collection => string.Equals(collection.DomainCode, "workspace-memory", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(collection => collection.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            collection.IsExpanded = true;
            _projectCollections.Add(collection);
        }
    }

    private static DomainItem CreateDomainTypeGroup(string domainType)
    {
        return new DomainItem
        {
            DomainCode = $"domain-type-{SlugifyForGroup(domainType)}",
            DisplayName = domainType,
            DomainType = domainType,
            IsExpanded = true,
            IsGroup = true,
        };
    }

    private static int GetDomainTypeSortBucket(string domainType)
    {
        return domainType.Trim().ToUpperInvariant() switch
        {
            "EXECUTIVE" => 10,
            "CORPORATE" => 20,
            "SERVICE" => 30,
            "PERSONAL" => 40,
            _ => 99,
        };
    }

    private static string SlugifyForGroup(string value)
    {
        var slug = Regex.Replace(value.Trim().ToLowerInvariant(), @"[^\w\s-]+", string.Empty);
        slug = Regex.Replace(slug, @"\s+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "unclassified" : slug;
    }

    private void ApplyDomainSelection(DomainItem domain, bool isIncluded)
    {
        _isUpdatingContextSelection = true;
        try
        {
            foreach (var currentDomain in EnumerateDomainTree(domain))
            {
                currentDomain.IsIncluded = isIncluded;
                foreach (var collection in currentDomain.Collections)
                {
                    collection.IsIncluded = isIncluded;
                }
            }
        }
        finally
        {
            _isUpdatingContextSelection = false;
        }
    }

    private void RefreshAncestorSelectionStates(DomainItem? domain)
    {
        _isUpdatingContextSelection = true;
        try
        {
            while (domain is not null)
            {
                RefreshDomainSelectionState(domain);
                domain = domain.ParentDomain;
            }
        }
        finally
        {
            _isUpdatingContextSelection = false;
        }
    }

    private void RefreshDomainSelectionState(DomainItem domain)
    {
        var childDomainStates = domain.ChildDomains
            .Select(child => child.IsIncluded)
            .ToList();
        var collectionStates = domain.Collections
            .Select(collection => collection.IsIncluded ? (bool?)true : false)
            .ToList();
        var combinedStates = childDomainStates.Concat(collectionStates).ToList();

        domain.IsIncluded = combinedStates.Count switch
        {
            0 => false,
            _ when combinedStates.All(state => state == true) => true,
            _ when combinedStates.All(state => state == false) => false,
            _ => null,
        };
    }

    private static IEnumerable<DomainItem> EnumerateDomainTree(DomainItem domain)
    {
        yield return domain;

        foreach (var childDomain in domain.ChildDomains)
        {
            foreach (var descendant in EnumerateDomainTree(childDomain))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<CollectionItem> EnumerateCapabilityCollections(IEnumerable<DomainItem> domains)
    {
        foreach (var domain in domains)
        {
            foreach (var collection in domain.Collections)
            {
                yield return collection;
            }

            foreach (var collection in EnumerateCapabilityCollections(domain.ChildDomains))
            {
                yield return collection;
            }
        }
    }

    private void ShowEmptyResponseState(string message)
    {
        ResponseWebView.NavigateToString(BuildEmptyResponseHtml(message));
    }

    private void RenderChatExchanges(ChatThreadItem thread)
    {
        ResponseWebView.NavigateToString(BuildThreadHtml(thread));
    }

    private async Task NavigateResponseHtmlAsync(string html)
    {
        if (ResponseWebView.CoreWebView2 is null)
        {
            ResponseWebView.NavigateToString(html);
            await Task.Delay(50);
            return;
        }

        var navigationCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            ResponseWebView.NavigationCompleted -= Handler;
            navigationCompleted.TrySetResult();
        }

        ResponseWebView.NavigationCompleted += Handler;
        ResponseWebView.NavigateToString(html);
        await Task.WhenAny(navigationCompleted.Task, Task.Delay(500));
    }

    private async Task AppendStreamingChunkAsync(string chunk)
    {
        if (ResponseWebView.CoreWebView2 is null)
        {
            return;
        }

        var script = $"appendLine({JsonSerializer.Serialize(chunk)});";
        try
        {
            await ResponseWebView.ExecuteScriptAsync(script);
        }
        catch
        {
            await Task.Delay(50);
            try
            {
                await ResponseWebView.ExecuteScriptAsync(script);
            }
            catch
            {
                // Ignore chunk paint failures and let final render recover the full answer.
            }
        }
    }

    private Task ScrollToLastExchangeAsync()
    {
        return Task.CompletedTask;
    }

    private static string BuildSourceSummary(AskResponse payload)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(payload.RetrievalMode))
        {
            parts.Add($"Retrieval: {FormatRetrievalModeLabel(payload.RetrievalMode)}");
        }
        if (!string.IsNullOrWhiteSpace(payload.RetrievalWarning))
        {
            parts.Add($"Note: {payload.RetrievalWarning.Trim()}");
        }
        parts.AddRange(
            payload.Sources
                .Select(source =>
                {
                    var tokenSuffix = source.TokenCount > 0 ? $" ({source.TokenCount} tokens)" : string.Empty;
                    return $"{source.CollectionDisplayName}: {source.SourceName}{tokenSuffix}";
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(source => $"Source: {source}")
        );

        return parts.Count == 0
            ? string.Empty
            : string.Join("; ", parts);
    }

    private static string FormatRetrievalModeLabel(string? retrievalMode)
    {
        return (retrievalMode ?? string.Empty).Trim() switch
        {
            "NoDocuments" => "No Documents",
            "DocumentsOnly" => "Full Documents",
            "RagOnly" => "Semantic RAG",
            "DocumentsAndRag" => "Documents + RAG",
            "Disabled" => "Documents Off",
            "VectorRag" => "Vector RAG",
            "Hybrid" => "Hybrid",
            _ => "Full Context",
        };
    }

    private string GetSelectedRetrievalMode()
    {
        if (IncludeDocumentsCheckBox is null || IncludeRagCheckBox is null)
        {
            return "Hybrid";
        }

        if (IncludeDocumentsCheckBox.IsChecked != true)
        {
            return "Disabled";
        }

        return IncludeRagCheckBox.IsChecked == true ? "Hybrid" : "FullContext";
    }

    private List<string> GetEnabledContextSources()
    {
        var enabled = new List<string>();
        if (IncludeDocumentsCheckBox is null
            || IncludeRagCheckBox is null
            || IncludePoliciesCheckBox is null
            || IncludeDomainContextCheckBox is null
            || IncludeControlsCheckBox is null)
        {
            enabled.Add("Documents + RAG");
            enabled.Add("Policies");
            enabled.Add("Domain Context");
            enabled.Add("Controls");
            return enabled;
        }

        if (IncludeDocumentsCheckBox.IsChecked == true)
        {
            enabled.Add(IncludeRagCheckBox.IsChecked == true ? "Documents + RAG" : "Documents");
        }

        if (IncludePoliciesCheckBox.IsChecked == true)
        {
            enabled.Add("Policies");
        }

        if (IncludeDomainContextCheckBox.IsChecked == true)
        {
            enabled.Add("Domain Context");
        }

        if (IncludeControlsCheckBox.IsChecked == true)
        {
            enabled.Add("Controls");
        }

        return enabled;
    }

    private static ChatResponseStats BuildMessageStats(
        AskResponseMetrics? metrics,
        string? selectedModel,
        DateTimeOffset startedAtUtc,
        TimeSpan elapsed)
    {
        return new ChatResponseStats
        {
            ModelName = string.IsNullOrWhiteSpace(metrics?.ModelName) ? selectedModel ?? string.Empty : metrics.ModelName,
            TotalTokens = metrics?.TotalTokens ?? 0,
            PromptTokens = metrics?.PromptTokens ?? 0,
            CompletionTokens = metrics?.CompletionTokens ?? 0,
            DurationSeconds = metrics is { DurationSeconds: > 0 } ? metrics.DurationSeconds : elapsed.TotalSeconds,
            TokensPerSecond = metrics?.TokensPerSecond ?? 0,
            CreatedAtUtc = metrics?.CreatedAtUtc ?? startedAtUtc,
        };
    }

    private async Task PersistCollectionChatsAsync(CollectionItem collection, bool pushBackup = true)
    {
        if (collection.Threads.Count == 0)
        {
            _localChatStore.DeleteCollection(collection.CollectionCode);
            if (pushBackup && _chatBackupService is not null && _chatBackupUser is not null)
            {
                try
                {
                    await _chatBackupService.DeleteBackupAsync(_chatBackupUser, collection.CollectionCode, collection.DisplayName);
                }
                catch
                {
                    // Keep local deletion authoritative even if the backup service is unavailable.
                }
            }
            return;
        }

        var snapshot = _localChatStore.SaveCollection(collection);
        if (!pushBackup || _chatBackupService is null || _chatBackupUser is null)
        {
            return;
        }

        try
        {
            await _chatBackupService.BackupAsync(_chatBackupUser, snapshot);
        }
        catch
        {
            // Local chat files remain the primary working copy if remote backup is temporarily unavailable.
        }
    }

    private async void ResponseWebView_OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("action", out var actionElement))
            {
                return;
            }

            var action = actionElement.GetString();
            if (!root.TryGetProperty("exchangeNumber", out var exchangeElement))
            {
                return;
            }

            var exchangeNumber = exchangeElement.GetInt32();
            if (string.Equals(action, "delete_exchange", StringComparison.OrdinalIgnoreCase))
            {
                await DeleteExchangeAsync(exchangeNumber);
                return;
            }

            if (string.Equals(action, "save_exchange", StringComparison.OrdinalIgnoreCase))
            {
                await SaveExchangeAsync(exchangeNumber);
            }
        }
        catch
        {
            // Ignore malformed webview messages.
        }
    }

    private async Task DeleteExchangeAsync(int exchangeNumber)
    {
        var thread = _activeChatThread;
        if (thread is null || exchangeNumber <= 0)
        {
            return;
        }

        var pairIndex = exchangeNumber - 1;
        var messageIndex = pairIndex * 2;
        if (messageIndex >= thread.Messages.Count)
        {
            return;
        }

        var removedCount = 0;
        if (messageIndex < thread.Messages.Count &&
            string.Equals(thread.Messages[messageIndex].Role, "User", StringComparison.OrdinalIgnoreCase))
        {
            thread.Messages.RemoveAt(messageIndex);
            removedCount++;
        }

        if (messageIndex < thread.Messages.Count &&
            string.Equals(thread.Messages[messageIndex].Role, "Assistant", StringComparison.OrdinalIgnoreCase))
        {
            thread.Messages.RemoveAt(messageIndex);
            removedCount++;
        }

        CollectionContentsListBox.ItemsSource = thread.Messages.Select(message => $"{message.Role}: {message.Content}");
        _activeChatThread = thread;
        _activeProjectCollection = thread.ParentCollection;
        if (thread.ParentCollection is not null)
        {
            await PersistCollectionChatsAsync(thread.ParentCollection);
        }

        if (!ReferenceEquals(ProjectCollectionsTreeView.SelectedItem, thread))
        {
            SelectTreeItem(thread);
        }

        if (thread.Messages.Count == 0)
        {
            thread.IsSelected = true;
            ShowEmptyResponseState("This chat is now empty.");
        }
        else
        {
            thread.IsSelected = true;
            await ShowChatThreadStateAsync(thread);
        }
    }

    private async Task SaveExchangeAsync(int exchangeNumber)
    {
        var thread = _activeChatThread;
        if (thread is null)
        {
            return;
        }

        var exchanges = BuildExchangeExportItems(thread);
        var exchange = exchanges.FirstOrDefault(item => item.ExchangeNumber == exchangeNumber);
        if (exchange is null)
        {
            return;
        }

        try
        {
            await SaveChatExportAsync(
                $"Exchange {exchange.ExchangeNumber} - {thread.Title}",
                BuildExportDocumentTitle(thread, exchange),
                [exchange]);
            SetUploadFeedback($"Saved exchange {exchange.ExchangeNumber}.", Brushes.DarkGreen);
        }
        catch (Exception ex)
        {
            SetUploadFeedback($"Save failed: {ex.Message}", Brushes.Firebrick);
        }
    }

    private async Task RestoreChatsFromBackupIfNeededAsync()
    {
        if (_chatBackupService is null || _chatBackupUser is null || _localChatStore.HasLocalChatFiles())
        {
            return;
        }

        try
        {
            var availability = await _chatBackupService.CheckAvailabilityAsync(_chatBackupUser);
            if (!availability.HasBackups)
            {
                return;
            }

            var shouldRestore = MessageBox.Show(
                "We found backed-up chats for your account but no local chat files on this PC. Restore them now?",
                "Restore Chats",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (shouldRestore != MessageBoxResult.Yes)
            {
                return;
            }

            var restoredFiles = await _chatBackupService.RestoreAsync(_chatBackupUser);
            _localChatStore.RestoreFiles(restoredFiles);
        }
        catch
        {
            // Keep startup resilient if the optional backup service is unavailable.
        }
    }

    private void RestoreLocalProjectChats()
    {
        var states = _localChatStore.LoadAll();
        if (states.Count == 0)
        {
            return;
        }

        var collectionsByCode = _projectCollections.ToDictionary(collection => collection.CollectionCode, StringComparer.OrdinalIgnoreCase);
        foreach (var state in states)
        {
            if (!collectionsByCode.TryGetValue(state.RootCollectionCode, out var collection))
            {
                collection = new CollectionItem
                {
                    CollectionCode = state.RootCollectionCode,
                    DisplayName = string.IsNullOrWhiteSpace(state.RootDisplayName) ? state.RootCollectionCode : state.RootDisplayName,
                    Description = "Local chat history loaded while the backend is unavailable.",
                    DomainCode = "local",
                    DomainDisplayName = "Local chats",
                };
                _projectCollections.Add(collection);
                collectionsByCode[state.RootCollectionCode] = collection;
            }

            collection.Threads.Clear();
            foreach (var savedThread in state.Threads)
            {
                var thread = new ChatThreadItem
                {
                    Title = string.IsNullOrWhiteSpace(savedThread.Title) ? "Untitled response" : savedThread.Title,
                    ParentCollection = collection,
                };

                foreach (var savedMessage in savedThread.Messages)
                {
                    thread.Messages.Add(
                        new ChatMessageItem
                        {
                            Role = savedMessage.Role,
                            Content = savedMessage.Content,
                            SupplementalText = savedMessage.SupplementalText,
                            CreatedAtUtc = savedMessage.CreatedAtUtc,
                            Stats = savedMessage.Stats,
                        }
                    );
                }

                collection.Threads.Add(thread);
            }
        }
    }

    private string BuildEmptyResponseHtml(string message)
    {
        return $$"""
        <html>
        <head>
          <meta charset="UTF-8">
          <style>
            body {
              font-family: "Segoe UI", sans-serif;
              margin: 0;
              padding: 24px;
              background: #f8f6f1;
              color: #66737d;
            }
            .empty {
              border: 1px solid #e2dbcf;
              border-radius: 8px;
              background: #ffffff;
              padding: 18px;
              font-size: 15px;
            }
          </style>
        </head>
        <body>
          <div class="empty">{{WebUtility.HtmlEncode(message)}}</div>
        </body>
        </html>
        """;
    }

    private string BuildThreadHtml(ChatThreadItem thread)
    {
        var messages = thread.Messages.ToList();
        var exchangeNumber = 0;
        var html = new System.Text.StringBuilder();
        html.AppendLine("""
        <html>
        <head>
          <meta charset="UTF-8">
          <style>
            body {
              font-family: "Segoe UI", sans-serif;
              margin: 0;
              padding: 10px;
              background: #f8f6f1;
              color: #263746;
            }
            .exchange {
              border: 1px solid #d8dde5;
              border-radius: 10px;
              margin: 0 0 8px 0;
              overflow: hidden;
              background: #ffffff;
              box-shadow: 0 1px 2px rgba(0,0,0,.04);
              position: relative;
            }
            .ex-header {
              background: #f3f6fa;
              color: #6e7ca0;
              padding: 6px 9px;
              font-weight: 600;
              border-bottom: 1px solid #d8dde5;
              display: flex;
              justify-content: space-between;
              align-items: center;
            }
            .ex-body {
              padding: 9px 10px 42px 10px;
            }
            .ex-body h1, .ex-body h2, .ex-body h3, .ex-body h4 {
              color: #18344a;
              margin-top: 0;
            }
            .section-label {
              font-weight: 700;
              color: #18344a;
              margin: 0 0 6px 0;
            }
            .answer-section {
              margin-top: 12px;
            }
            pre {
              background: #f5f7fa;
              border: 1px solid #d8dde5;
              border-radius: 8px;
              padding: 10px;
              overflow-x: auto;
            }
            .mermaid-host {
              background: #f8fafc;
              border: 1px solid #d8dde5;
              border-radius: 8px;
              padding: 12px;
              overflow-x: auto;
              margin: 10px 0;
            }
            .mermaid {
              min-width: fit-content;
            }
            code {
              font-family: Consolas, "Courier New", monospace;
            }
            table {
              border-collapse: collapse;
              width: 100%;
              margin: 10px 0;
            }
            th, td {
              border: 1px solid #d8dde5;
              padding: 8px;
              text-align: left;
              vertical-align: top;
            }
            th {
              background: #f3f6fa;
            }
            .info-row {
              position: absolute;
              right: 8px;
              bottom: 30px;
              z-index: 3;
            }
            .info-toggle {
              border: 1px solid #d8dde5;
              background: #ffffff;
              color: #496579;
              border-radius: 999px;
              width: 24px;
              height: 24px;
              display: inline-flex;
              align-items: center;
              justify-content: center;
              padding: 0;
              font-size: 13px;
              font-weight: 700;
              cursor: pointer;
              user-select: none;
              box-shadow: 0 2px 6px rgba(0,0,0,.14);
            }
            .info-toggle:hover {
              background: #f3f6fa;
            }
            .info-panel {
              display: none;
              position: absolute;
              right: 0;
              bottom: 30px;
              width: 320px;
              padding: 10px 12px;
              border: 1px solid #d8dde5;
              border-radius: 8px;
              background: #f8fafc;
              box-shadow: 0 4px 14px rgba(0,0,0,.12);
            }
            .info-panel.open {
              display: block;
            }
            .info-line {
              font-size: 12px;
              color: #5b6770;
            }
            .info-line + .info-line {
              margin-top: 6px;
            }
            .stats {
              position: absolute;
              left: 0;
              right: 0;
              bottom: 0;
              padding: 7px 10px 6px 12px;
              font-size: 12px;
              color: #5f6f8d;
              background: #e8edf6;
              border-top: 1px solid #d8dde5;
            }
            .delete-btn {
              border: 1px solid #d8dde5;
              background: #ffffff;
              color: #7c2430;
              border-radius: 999px;
              width: 26px;
              height: 26px;
              display: inline-flex;
              align-items: center;
              justify-content: center;
              padding: 0;
              font-size: 0;
              cursor: pointer;
            }
            .delete-btn:hover {
              background: #fff3f4;
            }
            .delete-btn svg {
              width: 13px;
              height: 13px;
              stroke: currentColor;
              stroke-width: 2;
              fill: none;
              stroke-linecap: round;
              stroke-linejoin: round;
            }
            .actions {
              display: inline-flex;
              align-items: center;
              gap: 5px;
            }
            .save-btn {
              border: 1px solid #d8dde5;
              background: #ffffff;
              color: #315b73;
              border-radius: 999px;
              width: 26px;
              height: 26px;
              display: inline-flex;
              align-items: center;
              justify-content: center;
              padding: 0;
              font-size: 0;
              cursor: pointer;
            }
            .save-btn:hover {
              background: #f3f6fa;
            }
            .save-btn svg {
              width: 15px;
              height: 15px;
              stroke: currentColor;
              stroke-width: 1.8;
              fill: none;
              stroke-linecap: round;
              stroke-linejoin: round;
            }
          </style>
        </head>
        <body>
        """);

        for (var index = 0; index < messages.Count;)
        {
            var userMessage = index < messages.Count && string.Equals(messages[index].Role, "User", StringComparison.OrdinalIgnoreCase)
                ? messages[index++]
                : null;
            var assistantMessage = index < messages.Count && string.Equals(messages[index].Role, "Assistant", StringComparison.OrdinalIgnoreCase)
                ? messages[index++]
                : null;
            exchangeNumber++;

            html.AppendLine($"""<div class="exchange" id="ex-{exchangeNumber}">""");
            html.AppendLine($"""<div class="ex-header"><span>Exchange {exchangeNumber}</span><span class="actions"><button class="save-btn" onclick="saveExchange({exchangeNumber})" title="Save exchange" aria-label="Save exchange"><svg viewBox="0 0 20 20" aria-hidden="true"><rect x="4" y="3" width="9" height="11" rx="1.5" ry="1.5"></rect><rect x="7" y="5" width="9" height="11" rx="1.5" ry="1.5"></rect><path d="M11.5 8.5v4.5"></path><path d="M9.5 11l2 2 2-2"></path><path d="M8 15.5h7"></path></svg></button><button class="delete-btn" onclick="deleteExchange({exchangeNumber})" title="Delete exchange" aria-label="Delete exchange"><svg viewBox="0 0 16 16" aria-hidden="true"><path d="M3 4.5h10"/><path d="M6 4.5V3.5h4v1"/><path d="M5 6.5v5"/><path d="M8 6.5v5"/><path d="M11 6.5v5"/><path d="M4 4.5l.5 8.5h7L12 4.5"/></svg></button></span></div>""");
            html.AppendLine("""<div class="ex-body">""");
            html.AppendLine("""<div class="section-label">Question</div>""");
            html.AppendLine(Markdown.ToHtml(userMessage?.Content ?? string.Empty, _markdownPipeline));
            html.AppendLine("""<div class="answer-section">""");
            html.AppendLine("""<div class="section-label">Answer</div>""");
            html.AppendLine(Markdown.ToHtml(assistantMessage?.Content ?? string.Empty, _markdownPipeline));
            var statsText = BuildStatsText(assistantMessage?.Stats);
            if (!string.IsNullOrWhiteSpace(statsText))
            {
                html.AppendLine($"""<div class="stats">{WebUtility.HtmlEncode(statsText)}</div>""");
            }
            var infoHtml = BuildInfoPanelHtml(exchangeNumber, assistantMessage);
            if (!string.IsNullOrWhiteSpace(infoHtml))
            {
                html.AppendLine(infoHtml);
            }
            html.AppendLine("""</div>""");
            html.AppendLine("""</div>""");
            html.AppendLine("""</div>""");
        }

        if (exchangeNumber == 0)
        {
            html.AppendLine($"""<div class="exchange"><div class="ex-body">{WebUtility.HtmlEncode("Response output will appear here.")}</div></div>""");
        }

        html.AppendLine("<script>");
        html.AppendLine("function deleteExchange(exchangeNumber) {");
        html.AppendLine("  window.chrome?.webview?.postMessage({ action: 'delete_exchange', exchangeNumber: exchangeNumber });");
        html.AppendLine("}");
        html.AppendLine("function saveExchange(exchangeNumber) {");
        html.AppendLine("  window.chrome?.webview?.postMessage({ action: 'save_exchange', exchangeNumber: exchangeNumber });");
        html.AppendLine("}");
        html.AppendLine("function toggleInfo(exchangeNumber) {");
        html.AppendLine("  var panel = document.getElementById('info-' + exchangeNumber);");
        html.AppendLine("  if (!panel) return;");
        html.AppendLine("  panel.classList.toggle('open');");
        html.AppendLine("}");
        html.AppendLine("setTimeout(function(){ var a = document.getElementById('ex-" + exchangeNumber + "'); if (a) { a.scrollIntoView({ behavior: 'auto', block: 'start' }); } }, 40);");
        html.AppendLine("</script>");
        html.AppendLine("</body></html>");
        return html.ToString();
    }

    private string BuildStreamingThreadHtml(ChatThreadItem thread)
    {
        var messages = thread.Messages.ToList();
        var exchangeNumber = 0;
        var html = new System.Text.StringBuilder();
        html.AppendLine("""
        <html>
        <head>
          <meta charset="UTF-8">
          <style>
            body {
              font-family: "Segoe UI", sans-serif;
              margin: 0;
              padding: 10px;
              background: #f8f6f1;
              color: #263746;
            }
            .exchange {
              border: 1px solid #d8dde5;
              border-radius: 10px;
              margin: 0 0 8px 0;
              overflow: hidden;
              background: #ffffff;
              box-shadow: 0 1px 2px rgba(0,0,0,.04);
            }
            .ex-header {
              background: #f3f6fa;
              color: #6e7ca0;
              padding: 6px 9px;
              font-weight: 600;
              border-bottom: 1px solid #d8dde5;
            }
            .ex-body {
              padding: 9px 10px;
            }
            .section-label {
              font-weight: 700;
              color: #18344a;
              margin: 0 0 6px 0;
            }
            .answer-section {
              margin-top: 12px;
            }
            .question-copy {
              white-space: pre-wrap;
            }
            #streamingAnswer {
              white-space: pre-wrap;
              font-family: Consolas, "Courier New", monospace;
              background: #f5f7fa;
              border: 1px solid #d8dde5;
              border-radius: 8px;
              padding: 10px;
              min-height: 48px;
              color: #263746;
            }
            .thinking {
              color: #7a8791;
              font-style: italic;
              display: inline-block;
              animation: breathingPulse 1.8s ease-in-out infinite;
              transform-origin: center;
            }
            @keyframes breathingPulse {
              0% {
                opacity: 0.42;
                transform: scale(0.985);
              }
              50% {
                opacity: 1;
                transform: scale(1.015);
              }
              100% {
                opacity: 0.42;
                transform: scale(0.985);
              }
            }
          </style>
        </head>
        <body>
        """);

        for (var index = 0; index < messages.Count;)
        {
            var userMessage = index < messages.Count && string.Equals(messages[index].Role, "User", StringComparison.OrdinalIgnoreCase)
                ? messages[index++]
                : null;
            var assistantMessage = index < messages.Count && string.Equals(messages[index].Role, "Assistant", StringComparison.OrdinalIgnoreCase)
                ? messages[index++]
                : null;
            exchangeNumber++;
            var isLatest = index >= messages.Count;

            html.AppendLine($"""<div class="exchange" id="ex-{exchangeNumber}">""");
            html.AppendLine($"""<div class="ex-header">Exchange {exchangeNumber}</div>""");
            html.AppendLine("""<div class="ex-body">""");
            html.AppendLine("""<div class="section-label">Question</div>""");
            html.AppendLine($"""<div class="question-copy">{WebUtility.HtmlEncode(userMessage?.Content ?? string.Empty)}</div>""");
            html.AppendLine("""<div class="answer-section">""");
            html.AppendLine("""<div class="section-label">Answer</div>""");

            if (isLatest)
            {
                html.AppendLine("""<div id="streamingAnswer"><span class="thinking">Thinking...</span></div>""");
            }
            else
            {
                html.AppendLine(Markdown.ToHtml(assistantMessage?.Content ?? string.Empty, _markdownPipeline));
            }

            html.AppendLine("""</div>""");
            html.AppendLine("""</div>""");
            html.AppendLine("""</div>""");
        }

        html.AppendLine("""
        <script>
        function appendLine(delta) {
          var host = document.getElementById('streamingAnswer');
          if (!host) return;
          if (host.dataset.started !== '1') {
            host.dataset.started = '1';
            host.textContent = '';
          }
          host.textContent += delta;
          host.scrollIntoView({ behavior: 'auto', block: 'end' });
          window.scrollTo(0, document.body.scrollHeight);
        }
        setTimeout(function() {
          var items = document.querySelectorAll('.exchange');
          if (items.length > 0) {
            items[items.length - 1].scrollIntoView({ behavior: 'auto', block: 'start' });
          }
        }, 40);
        </script>
        """);
        html.AppendLine("</body></html>");
        return html.ToString();
    }

    private static string EscapeForJavaScript(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("</", "<\\/");
    }

    private static string BuildInfoPanelHtml(int exchangeNumber, ChatMessageItem? assistantMessage)
    {
        var sourcesText = assistantMessage?.SupplementalText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sourcesText))
        {
            return string.Empty;
        }

        var html = new System.Text.StringBuilder();
        html.AppendLine($"""<div class="info-row"><button class="info-toggle" type="button" onclick="toggleInfo({exchangeNumber})" title="Show response info" aria-label="Show response info">i</button></div>""");
        html.AppendLine($"""<div class="info-panel" id="info-{exchangeNumber}">""");
        html.AppendLine($"""<div class="info-line">{WebUtility.HtmlEncode(sourcesText)}</div>""");
        html.AppendLine("""</div>""");
        return html.ToString();
    }

    private static string BuildStatsText(ChatResponseStats? stats)
    {
        if (stats is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(stats.ModelName))
        {
            parts.Add($"Model: {stats.ModelName}");
        }
        if (stats.DurationSeconds > 0)
        {
            parts.Add($"Time: {stats.DurationSeconds:F2}s");
        }
        if (stats.TokensPerSecond > 0)
        {
            parts.Add($"TPS: {stats.TokensPerSecond:F1}");
        }
        if (stats.CreatedAtUtc.HasValue)
        {
            parts.Add(stats.CreatedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        }
        if (stats.PromptTokens > 0 || stats.CompletionTokens > 0)
        {
            parts.Add($"T: {stats.PromptTokens}-{stats.CompletionTokens}");
        }

        return string.Join(" | ", parts);
    }

    private static string PrepareMarkdownForDisplay(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return markdown;
        }

        return Regex.Replace(
            markdown,
            @"```diagram-spec\s*(?<json>[\s\S]*?)```",
            match =>
            {
                var json = match.Groups["json"].Value;
                return TryConvertDiagramSpecToMermaidMarkdown(json, out var mermaidMarkdown)
                    ? mermaidMarkdown
                    : match.Value;
            },
            RegexOptions.IgnoreCase);
    }

    private static bool TryConvertDiagramSpecToMermaidMarkdown(string json, out string mermaidMarkdown)
    {
        mermaidMarkdown = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var direction = "TD";
            if (root.TryGetProperty("direction", out var directionElement))
            {
                var candidate = (directionElement.GetString() ?? string.Empty).Trim().ToUpperInvariant();
                if (candidate is "TD" or "TB" or "LR" or "RL")
                {
                    direction = candidate;
                }
            }

            var nodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("nodes", out var nodesElement) && nodesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var node in nodesElement.EnumerateArray())
                {
                    if (!node.TryGetProperty("id", out var idElement))
                    {
                        continue;
                    }

                    var id = SanitizeDiagramId(idElement.GetString());
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    var label = node.TryGetProperty("label", out var labelElement)
                        ? SanitizeDiagramLabel(labelElement.GetString())
                        : id;
                    nodes[id] = string.IsNullOrWhiteSpace(label) ? id : label;
                }
            }

            if (nodes.Count == 0)
            {
                return false;
            }

            var groupedNodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mermaid = new StringBuilder();
            mermaid.AppendLine("```mermaid");
            mermaid.AppendLine($"flowchart {direction}");

            if (root.TryGetProperty("groups", out var groupsElement) && groupsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var group in groupsElement.EnumerateArray())
                {
                    var rawGroupId = group.TryGetProperty("id", out var groupIdElement)
                        ? groupIdElement.GetString()
                        : null;
                    var groupId = SanitizeDiagramId(rawGroupId);
                    if (string.IsNullOrWhiteSpace(groupId))
                    {
                        continue;
                    }

                    var groupLabel = group.TryGetProperty("label", out var groupLabelElement)
                        ? SanitizeDiagramLabel(groupLabelElement.GetString())
                        : groupId;
                    mermaid.AppendLine($"    subgraph {groupId}[\"{EscapeMermaidLabel(groupLabel)}\"]");

                    if (group.TryGetProperty("nodeIds", out var nodeIdsElement) && nodeIdsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var nodeIdElement in nodeIdsElement.EnumerateArray())
                        {
                            var nodeId = SanitizeDiagramId(nodeIdElement.GetString());
                            if (string.IsNullOrWhiteSpace(nodeId) || !nodes.TryGetValue(nodeId, out var nodeLabel))
                            {
                                continue;
                            }

                            groupedNodeIds.Add(nodeId);
                            mermaid.AppendLine($"        {nodeId}[\"{EscapeMermaidLabel(nodeLabel)}\"]");
                        }
                    }

                    mermaid.AppendLine("    end");
                }
            }

            foreach (var node in nodes)
            {
                if (groupedNodeIds.Contains(node.Key))
                {
                    continue;
                }

                mermaid.AppendLine($"    {node.Key}[\"{EscapeMermaidLabel(node.Value)}\"]");
            }

            if (root.TryGetProperty("edges", out var edgesElement) && edgesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var edge in edgesElement.EnumerateArray())
                {
                    var from = edge.TryGetProperty("from", out var fromElement)
                        ? SanitizeDiagramId(fromElement.GetString())
                        : string.Empty;
                    var to = edge.TryGetProperty("to", out var toElement)
                        ? SanitizeDiagramId(toElement.GetString())
                        : string.Empty;
                    if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                    {
                        continue;
                    }

                    if (!nodes.ContainsKey(from) || !nodes.ContainsKey(to))
                    {
                        continue;
                    }

                    if (edge.TryGetProperty("label", out var edgeLabelElement))
                    {
                        var edgeLabel = SanitizeDiagramLabel(edgeLabelElement.GetString());
                        if (!string.IsNullOrWhiteSpace(edgeLabel))
                        {
                            mermaid.AppendLine($"    {from} -->|{EscapeMermaidLabel(edgeLabel)}| {to}");
                            continue;
                        }
                    }

                    mermaid.AppendLine($"    {from} --> {to}");
                }
            }

            mermaid.AppendLine("```");
            mermaidMarkdown = mermaid.ToString();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string SanitizeDiagramId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var id = Regex.Replace(value.Trim(), @"[^A-Za-z0-9_]", "_");
        id = Regex.Replace(id, @"_+", "_").Trim('_');
        if (string.IsNullOrWhiteSpace(id))
        {
            return string.Empty;
        }

        if (char.IsDigit(id[0]))
        {
            id = "N_" + id;
        }

        return id;
    }

    private static string SanitizeDiagramLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var label = value.Trim();
        label = Regex.Replace(label, @"\s+", " ");
        label = Regex.Replace(label, @"[^\x20-\x7E]", " ");
        return Regex.Replace(label, @"\s+", " ").Trim();
    }

    private static string EscapeMermaidLabel(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "/")
            .Replace("\"", "'");
    }

    private static string BuildTemporaryThreadTitle(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return "New Chat";
        }

        var cleaned = Regex.Replace(prompt, @"[^\w\s-]", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return "New Chat";
        }

        var words = cleaned
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(3);
        var title = string.Join(" ", words).Trim();
        return string.IsNullOrWhiteSpace(title) ? "New Chat" : $"{title} ...";
    }

    private static string NormalizeAiNodeTitle(string raw, string fallback)
    {
        var title = (raw ?? string.Empty).Trim();
        title = Regex.Replace(title, "^['\"]+|['\"]+$", string.Empty);
        title = Regex.Replace(title, @"[*_`#~]+", string.Empty);
        title = Regex.Replace(title, @"^[\-\u2022\*\d\.\)\(:\s]+", string.Empty);
        title = Regex.Replace(title, @"[:;,\.\!\?\-–—\s]+$", string.Empty);
        title = Regex.Replace(title, @"\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return fallback;
        }

        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 3)
        {
            title = string.Join(" ", words.Take(3));
        }

        if (title.Length > 24)
        {
            title = title[..24].TrimEnd();
        }

        return string.IsNullOrWhiteSpace(title) ? fallback : title;
    }

    private static string NormalizeAiNodeTitleStrict(string raw, string fallback)
    {
        var title = (raw ?? string.Empty).Trim();
        var boldMatch = Regex.Match(title, @"\*\*(.+?)\*\*");
        if (boldMatch.Success)
        {
            title = boldMatch.Groups[1].Value;
        }
        else if (title.Contains(':'))
        {
            title = title[(title.LastIndexOf(':') + 1)..];
        }

        title = Regex.Replace(title, @"[*_`#~]", " ");
        var words = Regex.Matches(title, @"[\p{L}\p{N}]+(?:[-'][\p{L}\p{N}]+)?")
            .Select(match => match.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        while (words.Count > 0 &&
               (string.Equals(words[0], "AI", StringComparison.OrdinalIgnoreCase)
                || string.Equals(words[0], "Okay", StringComparison.OrdinalIgnoreCase)
                || string.Equals(words[0], "Title", StringComparison.OrdinalIgnoreCase)))
        {
            words.RemoveAt(0);
        }

        var normalized = string.Join(" ", words.Take(3)).Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        var fallbackWords = Regex.Matches(fallback ?? string.Empty, @"[\p{L}\p{N}]+(?:[-'][\p{L}\p{N}]+)?")
            .Select(match => match.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(3);
        var fallbackTitle = string.Join(" ", fallbackWords).Trim();
        return string.IsNullOrWhiteSpace(fallbackTitle) ? "New Chat" : fallbackTitle;
    }

    private async Task<string> GenerateThreadTitleAsync(ChatThreadItem thread)
    {
        var transcript = string.Join(
            "\n",
            thread.Messages
                .Take(8)
                .Select(message => $"{message.Role}: {TrimForAiRename(message.Content, 240)}"));

        var prompt = """
Write a concise chat title for this conversation.
Use at most 3 words.
Return only the title, with no quotes, punctuation, or explanation.

Conversation:
""" + "\n" + transcript;

        var responseText = await GenerateShortOllamaTextAsync(prompt);
        return NormalizeAiNodeTitleStrict(responseText, thread.Title);
    }

    private async Task<string> GenerateCollectionNameAsync(CollectionItem collection)
    {
        var documents = await LoadDocumentsAsync(collection.CollectionCode);
        var documentNames = documents.Count == 0
            ? "No uploaded documents."
            : string.Join(", ", documents.Take(6).Select(document => document.SourceName));
        var threadTitles = collection.Threads.Count == 0
            ? "No chats yet."
            : string.Join(", ", collection.Threads.Take(6).Select(thread => thread.Title));

        var prompt = $"""
Write a concise project collection name.
Use at most 3 words.
Return only the name, with no quotes, punctuation, or explanation.

Current name: {collection.DisplayName}
Description: {collection.Description}
Documents: {documentNames}
Chats: {threadTitles}
""";

        var responseText = await GenerateShortOllamaTextAsync(prompt);
        return NormalizeAiNodeTitleStrict(responseText, collection.DisplayName);
    }

    private async Task<string> GenerateShortOllamaTextAsync(string prompt)
    {
        var model = ModelComboBox.SelectedValue as string;
        if (string.IsNullOrWhiteSpace(model))
        {
            model = _defaultChatModel;
        }

        using var response = await _ollamaHttpClient.PostAsJsonAsync(
            "/api/generate",
            new
            {
                model,
                prompt,
                stream = false,
            });
        response.EnsureSuccessStatusCode();

        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return payload?.RootElement.TryGetProperty("response", out var responseProperty) == true
            ? responseProperty.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string TrimForAiRename(string value, int maxLength)
    {
        var text = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength].TrimEnd() + "...";
    }

    private async Task SaveThreadExportAsync(ChatThreadItem thread)
    {
        var exchanges = BuildExchangeExportItems(thread);
        await SaveChatExportAsync(
            thread.Title,
            BuildExportDocumentTitle(thread, null),
            exchanges);
    }

    private async Task SaveChatExportAsync(
        string baseFileName,
        string documentTitle,
        List<ChatExchangeExportItem> exchanges)
    {
        if (exchanges.Count == 0)
        {
            throw new InvalidOperationException("There is no prompt/response content to save.");
        }

        var safeBaseFileName = MakeSafeFileName(baseFileName);
        var dialog = new SaveFileDialog
        {
            Title = "Save Chat Export",
            FileName = safeBaseFileName,
            Filter = "Word Document|*.docx|HTML File|*.html",
            AddExtension = true,
            DefaultExt = ".docx",
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            SetUploadFeedback("Save cancelled.");
            return;
        }

        var extension = Path.GetExtension(dialog.FileName);
        if (string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase))
        {
            var html = BuildThreadExportHtml(documentTitle, exchanges);
            await File.WriteAllTextAsync(dialog.FileName, html);
            return;
        }

        SaveThreadExportDocx(dialog.FileName, documentTitle, exchanges);
    }

    private List<ChatExchangeExportItem> BuildExchangeExportItems(ChatThreadItem thread)
    {
        var messages = thread.Messages.ToList();
        var exchanges = new List<ChatExchangeExportItem>();
        var exchangeNumber = 0;

        for (var index = 0; index < messages.Count;)
        {
            var userMessage = index < messages.Count && string.Equals(messages[index].Role, "User", StringComparison.OrdinalIgnoreCase)
                ? messages[index++]
                : null;
            var assistantMessage = index < messages.Count && string.Equals(messages[index].Role, "Assistant", StringComparison.OrdinalIgnoreCase)
                ? messages[index++]
                : null;
            exchangeNumber++;
            exchanges.Add(new ChatExchangeExportItem(exchangeNumber, userMessage, assistantMessage));
        }

        return exchanges;
    }

    private static string BuildExportDocumentTitle(ChatThreadItem thread, ChatExchangeExportItem? exchange)
    {
        return exchange is null
            ? $"Thread Export: {thread.Title}"
            : $"Exchange {exchange.ExchangeNumber}: {thread.Title}";
    }

    private string BuildThreadExportHtml(string title, List<ChatExchangeExportItem> exchanges)
    {
        var html = new System.Text.StringBuilder();
        html.AppendLine("""
        <html>
        <head>
          <meta charset="UTF-8">
          <style>
            body { font-family: "Segoe UI", Arial, sans-serif; margin: 28px; color: #243746; }
            h1 { color: #18344a; margin-bottom: 18px; }
            .exchange { border: 1px solid #d8dde5; border-radius: 8px; margin-bottom: 18px; overflow: hidden; }
            .header { background: #f3f6fa; padding: 10px 14px; font-weight: 700; color: #53647a; }
            .body { padding: 14px; }
            .label { font-weight: 700; color: #18344a; margin: 0 0 8px 0; }
            .answer { margin-top: 16px; }
            .meta { margin-top: 10px; font-size: 12px; color: #5f6f8d; }
            pre { background: #f5f7fa; border: 1px solid #d8dde5; border-radius: 8px; padding: 10px; overflow-x: auto; }
            .mermaid-host { background: #f8fafc; border: 1px solid #d8dde5; border-radius: 8px; padding: 12px; overflow-x: auto; margin: 10px 0; }
            .mermaid { min-width: fit-content; }
            code { font-family: Consolas, "Courier New", monospace; }
          </style>
        </head>
        <body>
        """);
        html.AppendLine($"<h1>{WebUtility.HtmlEncode(title)}</h1>");

        foreach (var exchange in exchanges)
        {
            html.AppendLine($"""<div class="exchange"><div class="header">Exchange {exchange.ExchangeNumber}</div><div class="body">""");
            html.AppendLine("""<div class="label">Question</div>""");
            html.AppendLine(Markdown.ToHtml(exchange.UserMessage?.Content ?? string.Empty, _markdownPipeline));
            html.AppendLine("""<div class="answer"><div class="label">Answer</div>""");
            html.AppendLine(Markdown.ToHtml(exchange.AssistantMessage?.Content ?? string.Empty, _markdownPipeline));

            var metaParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(exchange.AssistantMessage?.SupplementalText))
            {
                metaParts.Add(exchange.AssistantMessage.SupplementalText);
            }

            var statsText = BuildStatsText(exchange.AssistantMessage?.Stats);
            if (!string.IsNullOrWhiteSpace(statsText))
            {
                metaParts.Add(statsText);
            }

            if (metaParts.Count > 0)
            {
                html.AppendLine($"""<div class="meta">{WebUtility.HtmlEncode(string.Join(" | ", metaParts))}</div>""");
            }

            html.AppendLine("""</div></div></div>""");
        }

        html.AppendLine("</body></html>");
        return html.ToString();
    }

    private static void SaveThreadExportDocx(string filePath, string title, List<ChatExchangeExportItem> exchanges)
    {
        using var document = WordprocessingDocument.Create(filePath, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = new Body();
        mainPart.Document.Append(body);

        body.Append(CreateParagraph(title, "Title"));

        foreach (var exchange in exchanges)
        {
            body.Append(CreateParagraph($"Exchange {exchange.ExchangeNumber}", "Heading1"));
            body.Append(CreateParagraph("Question", "Heading2"));
            AppendMarkdownToBody(body, exchange.UserMessage?.Content ?? string.Empty);
            body.Append(CreateParagraph("Answer", "Heading2"));
            AppendMarkdownToBody(body, exchange.AssistantMessage?.Content ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(exchange.AssistantMessage?.SupplementalText))
            {
                body.Append(CreateParagraph(exchange.AssistantMessage.SupplementalText));
            }

            var statsText = BuildStatsText(exchange.AssistantMessage?.Stats);
            if (!string.IsNullOrWhiteSpace(statsText))
            {
                body.Append(CreateParagraph(statsText));
            }
        }

        mainPart.Document.Save();
    }

    private static Paragraph CreateParagraph(string text, string? styleId = null)
    {
        var paragraph = new Paragraph();
        if (!string.IsNullOrWhiteSpace(styleId))
        {
            paragraph.Append(new ParagraphProperties(new ParagraphStyleId { Val = styleId }));
        }

        paragraph.Append(new Run(new Text(text ?? string.Empty) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }));
        return paragraph;
    }

    private static void AppendMarkdownToBody(Body body, string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            body.Append(CreateParagraph(string.Empty));
            return;
        }

        var document = Markdown.Parse(markdown, new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());
        foreach (var block in document)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    body.Append(CreateInlineParagraph(
                        heading.Inline,
                        heading.Level <= 1 ? "Heading1" : heading.Level == 2 ? "Heading2" : "Heading3"));
                    break;
                case ParagraphBlock paragraph:
                    body.Append(CreateInlineParagraph(paragraph.Inline));
                    break;
                case FencedCodeBlock fencedCode:
                    AppendCodeBlock(body, fencedCode);
                    break;
                case CodeBlock codeBlock:
                    AppendCodeBlock(body, codeBlock);
                    break;
                case ListBlock listBlock:
                    AppendListBlock(body, listBlock);
                    break;
                case QuoteBlock quoteBlock:
                    AppendQuoteBlock(body, quoteBlock);
                    break;
                case MarkdownTable table:
                    AppendTableBlock(body, table);
                    break;
                default:
                    if (block is LeafBlock leafBlock)
                    {
                        var text = leafBlock.Lines.ToString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            body.Append(CreateParagraph(text.Trim()));
                        }
                    }
                    break;
            }
        }
    }

    private static Paragraph CreateInlineParagraph(ContainerInline? inline, string? styleId = null)
    {
        var paragraph = new Paragraph();
        if (!string.IsNullOrWhiteSpace(styleId))
        {
            paragraph.Append(new ParagraphProperties(new ParagraphStyleId { Val = styleId }));
        }

        if (inline is null)
        {
            paragraph.Append(new Run(new Text(string.Empty)));
            return paragraph;
        }

        AppendInlineRuns(paragraph, inline);
        if (!paragraph.Elements<Run>().Any())
        {
            paragraph.Append(new Run(new Text(string.Empty)));
        }

        return paragraph;
    }

    private static void AppendInlineRuns(Paragraph paragraph, ContainerInline container, RunProperties? inheritedProperties = null)
    {
        var current = container.FirstChild;
        while (current is not null)
        {
            switch (current)
            {
                case LiteralInline literal:
                    paragraph.Append(CreateRun(literal.Content.ToString(), inheritedProperties));
                    break;
                case LineBreakInline:
                    paragraph.Append(new Run(new Break()));
                    break;
                case CodeInline code:
                    paragraph.Append(CreateRun(code.Content, MergeRunProperties(inheritedProperties, monospace: true)));
                    break;
                case EmphasisInline emphasis:
                    var emphasisProperties = MergeRunProperties(
                        inheritedProperties,
                        bold: emphasis.DelimiterChar == '*' && emphasis.DelimiterCount >= 2,
                        italic: emphasis.DelimiterCount == 1 || emphasis.DelimiterChar == '_');
                    AppendInlineRuns(paragraph, emphasis, emphasisProperties);
                    break;
                case LinkInline link when !link.IsImage:
                    AppendInlineRuns(paragraph, link, inheritedProperties);
                    break;
                case ContainerInline nested:
                    AppendInlineRuns(paragraph, nested, inheritedProperties);
                    break;
            }

            current = current.NextSibling;
        }
    }

    private static Run CreateRun(string text, RunProperties? properties = null)
    {
        var run = new Run();
        if (properties is not null)
        {
            run.Append((RunProperties)properties.CloneNode(true));
        }

        run.Append(new Text(text ?? string.Empty) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve });
        return run;
    }

    private static RunProperties MergeRunProperties(
        RunProperties? baseProperties,
        bool bold = false,
        bool italic = false,
        bool monospace = false)
    {
        var properties = baseProperties is null
            ? new RunProperties()
            : (RunProperties)baseProperties.CloneNode(true);

        if (bold)
        {
            properties.Bold = new Bold();
        }

        if (italic)
        {
            properties.Italic = new Italic();
        }

        if (monospace)
        {
            properties.RunFonts = new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" };
        }

        return properties;
    }

    private static void AppendCodeBlock(Body body, CodeBlock codeBlock)
    {
        var codeText = codeBlock.Lines.ToString() ?? string.Empty;
        var properties = new RunProperties(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" });
        var paragraph = new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { Before = "120", After = "120" }));
        paragraph.Append(CreateRun(codeText.TrimEnd(), properties));
        body.Append(paragraph);
    }

    private static void AppendListBlock(Body body, ListBlock listBlock)
    {
        var index = 1;
        foreach (var item in listBlock.OfType<ListItemBlock>())
        {
            foreach (var subBlock in item)
            {
                var prefix = listBlock.IsOrdered ? $"{index}. " : "- ";
                switch (subBlock)
                {
                    case ParagraphBlock paragraph:
                        var prefixed = new Paragraph();
                        prefixed.Append(CreateRun(prefix));
                        if (paragraph.Inline is not null)
                        {
                            AppendInlineRuns(prefixed, paragraph.Inline);
                        }
                        body.Append(prefixed);
                        break;
                    default:
                        if (subBlock is LeafBlock leafBlock)
                        {
                            var text = leafBlock.Lines.ToString();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                body.Append(CreateParagraph(prefix + text.Trim()));
                            }
                        }
                        break;
                }
            }

            index++;
        }
    }

    private static void AppendQuoteBlock(Body body, QuoteBlock quoteBlock)
    {
        foreach (var subBlock in quoteBlock)
        {
            switch (subBlock)
            {
                case ParagraphBlock paragraph:
                    var quote = new Paragraph(
                        new ParagraphProperties(new Indentation { Left = "720" }));
                    quote.Append(CreateRun("> "));
                    if (paragraph.Inline is not null)
                    {
                        AppendInlineRuns(quote, paragraph.Inline);
                        }
                        body.Append(quote);
                        break;
                    default:
                    if (subBlock is LeafBlock leafBlock)
                    {
                        var text = leafBlock.Lines.ToString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            body.Append(CreateParagraph("> " + text.Trim()));
                        }
                    }
                    break;
            }
        }
    }

    private static void AppendTableBlock(Body body, MarkdownTable table)
    {
        var wordTable = new DocumentFormat.OpenXml.Wordprocessing.Table();
        var properties = new TableProperties(
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 8 },
                new BottomBorder { Val = BorderValues.Single, Size = 8 },
                new LeftBorder { Val = BorderValues.Single, Size = 8 },
                new RightBorder { Val = BorderValues.Single, Size = 8 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 6 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 6 }),
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct });
        wordTable.Append(properties);

        var rowIndex = 0;
        foreach (var row in table.OfType<MarkdownTableRow>())
        {
            var wordRow = new DocumentFormat.OpenXml.Wordprocessing.TableRow();
            var isHeader = rowIndex == 0;

            foreach (var cell in row.OfType<MarkdownTableCell>())
            {
                var wordCell = new DocumentFormat.OpenXml.Wordprocessing.TableCell();
                var cellProperties = new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Auto });
                if (isHeader)
                {
                    cellProperties.Append(new Shading
                    {
                        Val = ShadingPatternValues.Clear,
                        Fill = "F3F6FA"
                    });
                }

                wordCell.Append(cellProperties);
                AppendTableCellContent(wordCell, cell);
                if (!wordCell.Elements<Paragraph>().Any())
                {
                    wordCell.Append(new Paragraph(new Run(new Text(string.Empty))));
                }

                wordRow.Append(wordCell);
            }

            wordTable.Append(wordRow);
            rowIndex++;
        }

        body.Append(wordTable);
        body.Append(CreateParagraph(string.Empty));
    }

    private static void AppendTableCellContent(DocumentFormat.OpenXml.Wordprocessing.TableCell wordCell, MarkdownTableCell cell)
    {
        foreach (var block in cell)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    wordCell.Append(CreateInlineParagraph(paragraph.Inline));
                    break;
                case LeafBlock leafBlock:
                    var text = leafBlock.Lines.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        wordCell.Append(CreateParagraph(text.Trim()));
                    }
                    break;
            }
        }
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((value ?? "chat-export").Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "chat-export" : cleaned;
    }

    private void PromptTextBox_OnFocusChanged(object sender, RoutedEventArgs e)
    {
        UpdatePromptPlaceholderVisibility();
    }

    private void PromptTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePromptPlaceholderVisibility();
        ScheduleContextPreviewRefresh();
    }

    private void ContextOptionCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        UpdateContextOptionState();
        UpdateIncludedContextSummary();
        ScheduleContextPreviewRefresh();
    }

    private void ContextOptionsPopup_OnClosed(object sender, EventArgs e)
    {
        if (ContextOptionsToggleButton is not null)
        {
            ContextOptionsToggleButton.IsChecked = false;
        }
    }

    private void UpdateContextOptionState()
    {
        if (IncludeDocumentsCheckBox is null || IncludeRagCheckBox is null)
        {
            return;
        }
    }

    private void ContextPreviewTimer_OnTick(object? sender, EventArgs e)
    {
        _contextPreviewTimer.Stop();
        _ = RefreshContextPreviewAsync();
    }

    private void PromptTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            AskButton_OnClick(AskButton, new RoutedEventArgs(Button.ClickEvent, AskButton));
        }
    }

    private void UpdatePromptPlaceholderVisibility()
    {
        if (PromptPlaceholderTextBlock is null || PromptTextBox is null)
        {
            return;
        }

        PromptPlaceholderTextBlock.Visibility =
            PromptTextBox.IsKeyboardFocused || !string.IsNullOrWhiteSpace(PromptTextBox.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
    }

    private void ScheduleContextPreviewRefresh()
    {
        if (!IsLoaded)
        {
            return;
        }

        _contextPreviewTimer.Stop();
        _contextPreviewTimer.Start();
    }

    private async Task RefreshContextPreviewAsync()
    {
        if (_isRefreshingContextPreview)
        {
            return;
        }

        var shortMemoryCollection = _activeProjectCollection ?? GetActiveProjectCollection();
        var promptText = PromptTextBox.Text.Trim();
        if (shortMemoryCollection is null)
        {
            ContextBudgetTextBlock.Text = "Context budget will appear here once there is a selected collection.";
            return;
        }

        if (string.IsNullOrWhiteSpace(promptText))
        {
            ContextBudgetTextBlock.Text = "Context budget will appear here once there is a prompt and a selected collection.";
            return;
        }

        try
        {
            _isRefreshingContextPreview = true;
            ContextBudgetTextBlock.Text = "Estimating context budget...";

            var longTermCollections = EnumerateCapabilityCollections(_capabilityDomains)
                .Where(collection => collection.IsIncluded)
                .Select(collection => collection.CollectionCode)
                .ToList();
            var retrievalMode = GetSelectedRetrievalMode();

            using var response = await _httpClient.PostAsJsonAsync(
                "/ask/context-preview",
                new
                {
                    prompt = promptText,
                    shortMemoryCollectionCode = shortMemoryCollection.CollectionCode,
                    longTermCollectionCodes = longTermCollections,
                    retrievalMode,
                    selectedDomainCode = ResolveSelectedDomainCodeForChatContext(),
                    includeDocuments = IncludeDocumentsCheckBox.IsChecked == true,
                    includeRag = IncludeRagCheckBox.IsChecked == true,
                    includePolicies = IncludePoliciesCheckBox.IsChecked == true,
                    includeDomainContext = IncludeDomainContextCheckBox.IsChecked == true,
                    includeControls = IncludeControlsCheckBox.IsChecked == true,
                    history = Array.Empty<object>(),
                });
            response.EnsureSuccessStatusCode();
            var preview = await response.Content.ReadFromJsonAsync<ContextPreviewResponse>();
            if (preview is null)
            {
                ContextBudgetTextBlock.Text = "Context budget preview returned no data.";
                return;
            }

            var summary = $"{FormatRetrievalModeLabel(preview.RetrievalMode)}: {preview.ContextUnitCount} units, {preview.ContextTokenCount} chunk tokens, {preview.SourceCount} sources, {preview.UsedCollectionCodes.Count} collections.";
            if (!string.IsNullOrWhiteSpace(preview.RetrievalWarning))
            {
                summary = $"{summary} Note: {preview.RetrievalWarning.Trim()}";
            }
            ContextBudgetTextBlock.Text = summary;
        }
        catch (Exception ex)
        {
            ContextBudgetTextBlock.Text = $"Context budget unavailable: {ex.Message}";
        }
        finally
        {
            _isRefreshingContextPreview = false;
        }
    }

    private CollectionItem? GetActiveProjectCollection()
    {
        return (ProjectCollectionsTreeView.SelectedItem as CollectionItem)
            ?? _activeProjectCollection
            ?? _projectCollections.FirstOrDefault();
    }

    private string? ResolveSelectedDomainCodeForChatContext()
    {
        static bool IsUsableDomainCode(string? domainCode)
        {
            return !string.IsNullOrWhiteSpace(domainCode)
                && !string.Equals(domainCode, "workspace-memory", StringComparison.OrdinalIgnoreCase)
                && !domainCode.StartsWith("domain-type-", StringComparison.OrdinalIgnoreCase);
        }

        string? selectedTreeDomainCode = ProjectCollectionsTreeView.SelectedItem switch
        {
            CollectionItem collection when IsUsableDomainCode(collection.ParentDomain?.DomainCode) => collection.ParentDomain?.DomainCode,
            CollectionItem collection when IsUsableDomainCode(collection.DomainCode) => collection.DomainCode,
            ChatThreadItem thread when IsUsableDomainCode(thread.ParentCollection?.ParentDomain?.DomainCode) => thread.ParentCollection?.ParentDomain?.DomainCode,
            ChatThreadItem thread when IsUsableDomainCode(thread.ParentCollection?.DomainCode) => thread.ParentCollection?.DomainCode,
            _ => null,
        };
        if (IsUsableDomainCode(selectedTreeDomainCode))
        {
            return selectedTreeDomainCode;
        }

        var includedDomains = _capabilityDomains
            .SelectMany(EnumerateDomainTree)
            .Where(domain => domain.IsIncluded == true && !domain.IsGroup && IsUsableDomainCode(domain.DomainCode))
            .Select(domain => domain.DomainCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (includedDomains.Count == 1)
        {
            return includedDomains[0];
        }

        var activeCollection = GetActiveProjectCollection();
        if (IsUsableDomainCode(activeCollection?.ParentDomain?.DomainCode))
        {
            return activeCollection?.ParentDomain?.DomainCode;
        }

        if (IsUsableDomainCode(activeCollection?.DomainCode))
        {
            return activeCollection?.DomainCode;
        }

        return null;
    }

    private async Task<bool> TryDeleteSelectedProjectTreeNodeFromKeyboardAsync()
    {
        if (Keyboard.FocusedElement is TextBox || Keyboard.Modifiers is not (ModifierKeys.None or ModifierKeys.Control))
        {
            return false;
        }

        switch (ProjectCollectionsTreeView.SelectedItem)
        {
            case ChatThreadItem thread when thread.ParentCollection is not null:
                await DeleteChatThreadAsync(thread);
                return true;
            case CollectionItem collection:
                await DeleteRootCollectionAsync(collection);
                return true;
            default:
                return false;
        }
    }

    private void ActivateProjectCollection(CollectionItem collection)
    {
        ClearMultiSelection();
        ClearProjectSelectionFlags();
        collection.IsSelected = true;
        _activeProjectCollection = collection;
        _activeChatThread = null;
        SelectTreeItem(collection);
    }

    private void ActivateChatThread(ChatThreadItem thread)
    {
        SetSingleMultiSelection(thread);
        ClearProjectSelectionFlags();
        thread.IsSelected = true;
        _activeChatThread = thread;
        _activeProjectCollection = thread.ParentCollection;
        if (thread.ParentCollection is not null)
        {
            thread.ParentCollection.IsExpanded = true;
        }
        SelectTreeItem(thread);
    }

    private void ClearProjectSelectionFlags()
    {
        foreach (var collection in _projectCollections)
        {
            collection.IsSelected = false;
            foreach (var thread in collection.Threads)
            {
                thread.IsSelected = false;
            }
        }
    }

    private void ClearMultiSelection()
    {
        foreach (var thread in _selectedChatThreads)
        {
            thread.IsMultiSelected = false;
        }

        _selectedChatThreads.Clear();
    }

    private void SetSingleMultiSelection(ChatThreadItem thread)
    {
        ClearMultiSelection();
        thread.IsMultiSelected = true;
        _selectedChatThreads.Add(thread);
    }

    private void ToggleMultiSelection(ChatThreadItem thread)
    {
        if (_selectedChatThreads.Contains(thread))
        {
            thread.IsMultiSelected = false;
            _selectedChatThreads.Remove(thread);
            return;
        }

        if (_selectedChatThreads.Count > 0 &&
            !ReferenceEquals(_selectedChatThreads[0].ParentCollection, thread.ParentCollection))
        {
            ClearMultiSelection();
        }

        thread.IsMultiSelected = true;
        _selectedChatThreads.Add(thread);
    }

    private static ChatMessageItem CloneChatMessage(ChatMessageItem message)
    {
        return new ChatMessageItem
        {
            Role = message.Role,
            Content = message.Content,
            SupplementalText = message.SupplementalText,
            CreatedAtUtc = message.CreatedAtUtc,
            Stats = message.Stats is null
                ? null
                : new ChatResponseStats
                {
                    ModelName = message.Stats.ModelName,
                    TotalTokens = message.Stats.TotalTokens,
                    PromptTokens = message.Stats.PromptTokens,
                    CompletionTokens = message.Stats.CompletionTokens,
                    DurationSeconds = message.Stats.DurationSeconds,
                    TokensPerSecond = message.Stats.TokensPerSecond,
                    CreatedAtUtc = message.Stats.CreatedAtUtc,
                }
        };
    }

    private async void RootNameEditor_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CollectionItem collection)
        {
            await CommitRootRenameAsync(collection);
        }
    }

    private async void RootNameEditor_OnKeyDown(object sender, KeyEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not CollectionItem collection)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            await CommitRootRenameAsync(collection);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            collection.IsEditing = false;
            RefreshProjectTree();
            e.Handled = true;
        }
    }

    private async Task CommitRootRenameAsync(CollectionItem collection)
    {
        var newName = (collection.DisplayName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            ShowEmptyResponseState("Root name cannot be blank.");
            return;
        }

        try
        {
            var response = await _httpClient.PutAsJsonAsync(
                $"/collections/{Uri.EscapeDataString(collection.CollectionCode)}",
                new
                {
                    displayName = newName,
                    description = collection.Description,
                }
            );
            response.EnsureSuccessStatusCode();

            var updated = await response.Content.ReadFromJsonAsync<CollectionItem>();
            if (updated is not null)
            {
                collection.DisplayName = updated.DisplayName;
                collection.Description = updated.Description;
            }

            collection.IsEditing = false;
            await PersistCollectionChatsAsync(collection);
            RefreshProjectTree();
        }
        catch (Exception ex)
        {
            collection.IsEditing = false;
            RefreshProjectTree();
            ShowEmptyResponseState($"Rename failed:{Environment.NewLine}{ex.Message}");
        }
    }

    private void RefreshProjectTree()
    {
        CollectionViewSource.GetDefaultView(ProjectCollectionsTreeView.ItemsSource)?.Refresh();
    }

    private void SetCenterMode(bool isRootMode)
    {
        if (isRootMode && _isThreadPanelExpanded)
        {
            _isThreadPanelExpanded = false;
        }

        RootUploadPanel.Visibility = isRootMode ? Visibility.Visible : Visibility.Collapsed;
        RootContentPanel.Visibility = isRootMode ? Visibility.Visible : Visibility.Collapsed;
        ChildResponsePanel.Visibility = isRootMode ? Visibility.Collapsed : Visibility.Visible;
        UpdateThreadPanelState();
    }

    private void RootNameEditor_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox editor &&
            editor.DataContext is CollectionItem { IsEditing: true })
        {
            editor.Focus();
            editor.SelectAll();
        }
    }

    private void ChildNameEditor_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox editor &&
            editor.DataContext is ChatThreadItem { IsEditing: true })
        {
            editor.Focus();
            editor.SelectAll();
        }
    }

    private async void ChildNameEditor_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ChatThreadItem thread)
        {
            thread.IsEditing = false;
            if (thread.ParentCollection is not null)
            {
                await PersistCollectionChatsAsync(thread.ParentCollection, pushBackup: false);
            }
            RefreshProjectTree();
        }
    }

    private async void ChildNameEditor_OnKeyDown(object sender, KeyEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ChatThreadItem thread)
        {
            return;
        }

        if (e.Key is Key.Enter or Key.Escape)
        {
            thread.IsEditing = false;
            if (thread.ParentCollection is not null)
            {
                await PersistCollectionChatsAsync(thread.ParentCollection, pushBackup: false);
            }
            RefreshProjectTree();
            e.Handled = true;
        }
    }

    private void TopPanelToggleButton_OnClick(object sender, RoutedEventArgs e)
    {
        _isTopPanelExpanded = !_isTopPanelExpanded;
        UpdateTopPanelState();
    }

    private void DecreaseUiScaleButton_OnClick(object sender, RoutedEventArgs e)
    {
        AdjustUiScale(-UiScaleHelper.ScaleStep);
    }

    private void ResetUiScaleButton_OnClick(object sender, RoutedEventArgs e)
    {
        ResetUiScale();
    }

    private void IncreaseUiScaleButton_OnClick(object sender, RoutedEventArgs e)
    {
        AdjustUiScale(UiScaleHelper.ScaleStep);
    }

    private void ThreadPanelExpandButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ChildResponsePanel.Visibility != Visibility.Visible)
        {
            return;
        }

        _isThreadPanelExpanded = !_isThreadPanelExpanded;
        UpdateThreadPanelState();
    }

    private void UpdateTopPanelState()
    {
        if (!IsInitialized)
        {
            return;
        }

        BackendStatusTextBox.Visibility = _isTopPanelExpanded ? Visibility.Visible : Visibility.Collapsed;
        BackendDetailTextBox.Visibility = _isTopPanelExpanded ? Visibility.Visible : Visibility.Collapsed;
        BackendDetailHost.Visibility = _isTopPanelExpanded ? Visibility.Visible : Visibility.Collapsed;
        BackendDetailHost.Height = _isTopPanelExpanded ? double.NaN : 0;
        TopPanelToggleIcon.Data = Geometry.Parse(
            _isTopPanelExpanded
                ? "M 2 10 L 7 4 L 12 10"
                : "M 2 4 L 7 10 L 12 4"
        );
    }

    private void AdjustUiScale(double delta)
    {
        ApplyUiScale(_appUiScale + delta);
    }

    private void ResetUiScale()
    {
        ApplyUiScale(UiScaleHelper.DefaultScale);
    }

    private void ApplyUiScale(double scale)
    {
        var clampedScale = UiScaleHelper.Clamp(scale);
        if (Math.Abs(clampedScale - _appUiScale) < 0.001)
        {
            return;
        }

        _appUiScale = clampedScale;
        _settings = _settings with { AppUiScale = _appUiScale };
        UiScaleHelper.ApplyWindowScale(this, _appUiScale);
        UiScaleHelper.ApplyWebViewScale(ShellMenuWebView, _appUiScale);
        UiScaleHelper.ApplyWebViewScale(ResponseWebView, _appUiScale);
        _settings.Save();
        SetUploadFeedback($"App font size: {Math.Round(_appUiScale * 100)}%", Brushes.DarkGreen);
    }

    private void UpdateThreadPanelState()
    {
        if (!IsInitialized)
        {
            return;
        }

        var canExpandThreadPanel = ChildResponsePanel.Visibility == Visibility.Visible;
        if (!canExpandThreadPanel)
        {
            _isThreadPanelExpanded = false;
        }

        ThreadPanelExpandButton.Visibility = canExpandThreadPanel ? Visibility.Visible : Visibility.Collapsed;
        ThreadPanelExpandButton.ToolTip = _isThreadPanelExpanded ? "Restore thread panel" : "Expand thread panel";

        WorkingPaneHeaderBar.Visibility = _isThreadPanelExpanded ? Visibility.Collapsed : Visibility.Visible;
        PromptInputPanel.Visibility = _isThreadPanelExpanded ? Visibility.Collapsed : Visibility.Visible;
        PromptPaneSplitter.Visibility = _isThreadPanelExpanded ? Visibility.Collapsed : Visibility.Visible;
        IncludedContextTextBlock.Visibility = _isThreadPanelExpanded ? Visibility.Collapsed : Visibility.Visible;

        WorkingPaneHeaderSpacerRow.Height = _isThreadPanelExpanded ? new GridLength(0) : _defaultWorkingPaneHeaderSpacerRowHeight;
        PromptInputRow.MinHeight = _isThreadPanelExpanded ? 0 : 120;
        PromptInputRow.Height = _isThreadPanelExpanded ? new GridLength(0) : _defaultPromptInputRowHeight;
        PromptSplitterRow.Height = _isThreadPanelExpanded ? new GridLength(0) : _defaultPromptSplitterRowHeight;
        WorkPanelTopSpacerRow.Height = _isThreadPanelExpanded ? new GridLength(0) : _defaultWorkPanelTopSpacerRowHeight;

        Grid.SetRow(WorkPanel, _isThreadPanelExpanded ? 2 : 5);
        Grid.SetRowSpan(WorkPanel, _isThreadPanelExpanded ? 4 : 1);

        ThreadPanelHeader.Visibility = _isThreadPanelExpanded ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetRow(ThreadPanelContentHost, _isThreadPanelExpanded ? 0 : 2);
        Grid.SetRowSpan(ThreadPanelContentHost, _isThreadPanelExpanded ? 5 : 1);
        Grid.SetRow(ChildResponsePanel, _isThreadPanelExpanded ? 0 : 2);
        Grid.SetRowSpan(ChildResponsePanel, _isThreadPanelExpanded ? 3 : 1);

        ThreadPanelExpandIcon.Data = Geometry.Parse(
            _isThreadPanelExpanded
                ? "M 4 4 H 8 V 8 M 12 8 V 4 H 16 M 16 12 H 12 V 16 M 8 16 V 12 H 4"
                : "M 4 8 V 4 H 8 M 12 4 H 16 V 8 M 16 12 V 16 H 12 M 8 16 H 4 V 12"
        );
    }

    private void SelectTreeItem(object item)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                var container = GetTreeViewItem(ProjectCollectionsTreeView, item);
                if (container is null)
                {
                    return;
                }

                container.IsSelected = true;
                container.Focus();
            })
        );
    }

    private void BeginInlineRename(object item)
    {
        RefreshProjectTree();
        SelectTreeItem(item);
        FocusInlineRenameEditor(item, DispatcherPriority.Loaded);
        FocusInlineRenameEditor(item, DispatcherPriority.ContextIdle);
        FocusInlineRenameEditor(item, DispatcherPriority.ApplicationIdle);
    }

    private void FocusInlineRenameEditor(object item, DispatcherPriority priority)
    {
        Dispatcher.BeginInvoke(
            priority,
            new Action(() =>
            {
                var container = GetTreeViewItem(ProjectCollectionsTreeView, item);
                if (container is null)
                {
                    return;
                }

                var editor = FindDescendant<TextBox>(container, textBox =>
                    ReferenceEquals(textBox.Tag, item) && textBox.Visibility == Visibility.Visible);
                if (editor is null)
                {
                    return;
                }

                editor.Focus();
                Keyboard.Focus(editor);
                editor.SelectAll();
            }));
    }

    private static TreeViewItem? GetTreeViewItem(ItemsControl parent, object item)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem direct)
        {
            return direct;
        }

        foreach (var parentItem in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(parentItem) is not TreeViewItem container)
            {
                continue;
            }

            container.IsExpanded = true;
            container.UpdateLayout();
            var child = GetTreeViewItem(container, item);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject parent, Func<T, bool>? predicate = null)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed && (predicate is null || predicate(typed)))
            {
                return typed;
            }

            var nested = FindDescendant(child, predicate);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
