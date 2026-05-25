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
using Microsoft.Win32;
using MarkdownTable = Markdig.Extensions.Tables.Table;
using MarkdownTableCell = Markdig.Extensions.Tables.TableCell;
using MarkdownTableRow = Markdig.Extensions.Tables.TableRow;

namespace DomainLinksDesktop;

public partial class MainWindow : Window
{
    private sealed record OcrExtractionResult(bool Success, string Text, string ErrorMessage);
    private sealed record ChatExchangeExportItem(int ExchangeNumber, ChatMessageItem? UserMessage, ChatMessageItem? AssistantMessage);

    private static readonly JsonSerializerOptions StreamJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly DomainLinksDesktopSettings _settings = DomainLinksDesktopSettings.Load();
    private readonly HttpClient _httpClient;
    private readonly HttpClient _ollamaHttpClient;
    private readonly ObservableCollection<CollectionItem> _projectCollections = [];
    private readonly ObservableCollection<DomainItem> _knowledgeDomains = [];
    private readonly ObservableCollection<ModelOptionItem> _availableModels = [];
    private readonly MarkdownPipeline _markdownPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    private readonly LocalChatStore _localChatStore = new();
    private bool _isTopPanelExpanded;
    private bool _isStreamingResponseActive;
    private CollectionItem? _activeProjectCollection;
    private ChatThreadItem? _activeChatThread;
    private ChatThreadItem? _streamingThread;
    private string? _defaultChatModel;
    private ChatBackupService? _chatBackupService;
    private ChatBackupUserIdentity? _chatBackupUser;

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
        DomainContextTreeView.ItemsSource = _knowledgeDomains;
        ModelComboBox.ItemsSource = _availableModels;
        Loaded += MainWindow_OnLoaded;
        Closing += MainWindow_OnClosing;
        UpdateTopPanelState();
        UpdatePromptPlaceholderVisibility();
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        LeftPaneColumn.Width = new GridLength(_settings.LeftPaneWidth);
        RightPaneColumn.Width = new GridLength(_settings.RightPaneWidth);
        PromptInputRow.Height = new GridLength(_settings.PromptPaneHeight);
        await ResponseWebView.EnsureCoreWebView2Async();
        ResponseWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
        ResponseWebView.CoreWebView2.WebMessageReceived += ResponseWebView_OnWebMessageReceived;
        ShowEmptyResponseState("Response output will appear here.");
        await LoadShellAsync();
    }

    private void MainWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var saved = new DomainLinksDesktopSettings
        {
            BackendBaseUrl = _settings.BackendBaseUrl,
            OllamaBaseUrl = _settings.OllamaBaseUrl,
            WindowWidth = Width,
            WindowHeight = Height,
            WindowLeft = Left,
            WindowTop = Top,
            LeftPaneWidth = LeftPaneColumn.ActualWidth,
            RightPaneWidth = RightPaneColumn.ActualWidth,
            PromptPaneHeight = PromptInputRow.ActualHeight,
            LastSelectedModel = ModelComboBox.SelectedValue as string ?? string.Empty,
        };
        saved.Save();
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
            _knowledgeDomains.Clear();
            foreach (var domain in domains)
            {
                foreach (var collection in collections.Where(c => c.DomainCode == domain.DomainCode))
                {
                    domain.Collections.Add(collection);
                }
                if (domain.DomainType == "ProjectMemory")
                {
                    foreach (var collection in domain.Collections)
                    {
                        collection.IsExpanded = true;
                        _projectCollections.Add(collection);
                    }
                }
                else if (domain.DomainType == "Knowledge")
                {
                    _knowledgeDomains.Add(domain);
                }
            }

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
            _availableModels.Clear();
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
        var viewer = new OcrViewerWindow(_settings.OllamaBaseUrl)
        {
            Owner = this,
        };
        viewer.Show();
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

    private void ProjectRootLabel_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not CollectionItem collection)
        {
            return;
        }

        if (e.ClickCount != 2)
        {
            return;
        }

        collection.IsEditing = true;
        RefreshProjectTree();
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

    private void RenameRootMenuItem_OnClick(object sender, RoutedEventArgs e)
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
        collection.IsEditing = true;
        RefreshProjectTree();
        SelectTreeItem(collection);
    }

    private async void DeleteRootMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var collection = ResolveCollectionFromMenuSender(sender);
        if (collection is null)
        {
            return;
        }

        await DeleteRootCollectionAsync(collection);
    }

    private void RenameChildMenuItem_OnClick(object sender, RoutedEventArgs e)
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
        thread.IsEditing = true;
        RefreshProjectTree();
        SelectTreeItem(thread);
    }

    private async void DeleteChildMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var thread = ResolveThreadFromMenuSender(sender);
        if (thread is null)
        {
            return;
        }

        await DeleteChatThreadAsync(thread);
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

    private void ChildThreadLabel_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ChatThreadItem thread)
        {
            return;
        }

        if (e.ClickCount != 2)
        {
            return;
        }

        thread.IsEditing = true;
        RefreshProjectTree();
        e.Handled = true;
    }

    private void ContextCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        UpdateIncludedContextSummary();
    }

    private async void ProjectCollectionsTreeView_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        switch (e.NewValue)
        {
            case CollectionItem collection:
                ClearProjectSelectionFlags();
                collection.IsSelected = true;
                _activeProjectCollection = collection;
                _activeChatThread = null;
                await ShowProjectCollectionStateAsync(collection);
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
                    break;
                }
                await ShowChatThreadStateAsync(thread);
                break;
        }
    }

    private async void ProjectCollectionsTreeView_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Back || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        if (ProjectCollectionsTreeView.SelectedItem is not ChatThreadItem thread || thread.ParentCollection is null)
        {
            return;
        }

        e.Handled = true;
        await DeleteChatThreadAsync(thread);
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
            return;
        }

        SetCenterMode(isRootMode: true);
        CollectionHeaderTextBlock.Text = $"Collection: {activeCollection.DisplayName}";
        CollectionDetailTextBlock.Text = string.IsNullOrWhiteSpace(activeCollection.Description)
            ? $"Upload into collection code '{activeCollection.CollectionCode}' and chat against this short-memory scope."
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
        return await _httpClient.GetFromJsonAsync<List<DocumentListItem>>($"/documents?collectionCode={Uri.EscapeDataString(collectionCode)}") ?? [];
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
            var longTermCollections = _knowledgeDomains
                .SelectMany(domain => domain.Collections)
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
                    domainCode = "projects",
                    collectionCode = displayName,
                    displayName,
                    description = "New short-memory project collection.",
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

        var parent = thread.ParentCollection;
        parent.Threads.Remove(thread);
        _activeProjectCollection = parent;
        _activeChatThread = null;
        ClearProjectSelectionFlags();
        SelectTreeItem(parent);
        await PersistCollectionChatsAsync(parent);
        ShowEmptyResponseState($"Deleted thread: {thread.Title}");
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
        var includedCollections = _knowledgeDomains
            .SelectMany(domain => domain.Collections)
            .Where(collection => collection.IsIncluded)
            .Select(collection => $"{collection.DomainDisplayName} / {collection.DisplayName}")
            .ToList();

        IncludedContextTextBlock.Text = includedCollections.Count == 0
            ? "No durable domains selected."
            : $"Included durable context: {string.Join("; ", includedCollections)}";
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
        if (payload.Sources.Count == 0)
        {
            return string.Empty;
        }

        var parts = payload.Sources
            .Select(source => $"{source.CollectionDisplayName}: {source.SourceName}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return parts.Count == 0
            ? string.Empty
            : $"Sources: {string.Join("; ", parts)}";
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
                continue;
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
              padding: 18px;
              background: #f8f6f1;
              color: #263746;
            }
            .exchange {
              border: 1px solid #d8dde5;
              border-radius: 10px;
              margin: 0 0 14px 0;
              overflow: hidden;
              background: #ffffff;
              box-shadow: 0 1px 2px rgba(0,0,0,.04);
              position: relative;
            }
            .ex-header {
              background: #f3f6fa;
              color: #6e7ca0;
              padding: 9px 12px;
              font-weight: 600;
              border-bottom: 1px solid #d8dde5;
              display: flex;
              justify-content: space-between;
              align-items: center;
            }
            .ex-body {
              padding: 14px 14px 52px 14px;
            }
            .ex-body h1, .ex-body h2, .ex-body h3, .ex-body h4 {
              color: #18344a;
              margin-top: 0;
            }
            .section-label {
              font-weight: 700;
              color: #18344a;
              margin: 0 0 8px 0;
            }
            .answer-section {
              margin-top: 16px;
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
              right: 10px;
              bottom: 34px;
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
              padding: 9px 14px 8px 18px;
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
              width: 28px;
              height: 28px;
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
              width: 14px;
              height: 14px;
              stroke: currentColor;
              stroke-width: 2;
              fill: none;
              stroke-linecap: round;
              stroke-linejoin: round;
            }
            .actions {
              display: inline-flex;
              align-items: center;
              gap: 8px;
            }
            .save-btn {
              border: 1px solid #d8dde5;
              background: #ffffff;
              color: #315b73;
              border-radius: 999px;
              width: 32px;
              height: 32px;
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
              width: 18px;
              height: 18px;
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
              padding: 18px;
              background: #f8f6f1;
              color: #263746;
            }
            .exchange {
              border: 1px solid #d8dde5;
              border-radius: 10px;
              margin: 0 0 14px 0;
              overflow: hidden;
              background: #ffffff;
              box-shadow: 0 1px 2px rgba(0,0,0,.04);
            }
            .ex-header {
              background: #f3f6fa;
              color: #6e7ca0;
              padding: 9px 12px;
              font-weight: 600;
              border-bottom: 1px solid #d8dde5;
            }
            .ex-body {
              padding: 14px;
            }
            .section-label {
              font-weight: 700;
              color: #18344a;
              margin: 0 0 8px 0;
            }
            .answer-section {
              margin-top: 16px;
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

    private CollectionItem? GetActiveProjectCollection()
    {
        return (ProjectCollectionsTreeView.SelectedItem as CollectionItem)
            ?? _activeProjectCollection
            ?? _projectCollections.FirstOrDefault();
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
        RootUploadPanel.Visibility = isRootMode ? Visibility.Visible : Visibility.Collapsed;
        RootContentPanel.Visibility = isRootMode ? Visibility.Visible : Visibility.Collapsed;
        ChildResponsePanel.Visibility = isRootMode ? Visibility.Collapsed : Visibility.Visible;
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
}
