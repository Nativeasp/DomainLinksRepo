using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DomainLinksDesktop;

public partial class MainWindow : Window
{
    private readonly DomainLinksDesktopSettings _settings = DomainLinksDesktopSettings.Load();
    private readonly HttpClient _httpClient;
    private readonly HttpClient _ollamaHttpClient;
    private readonly ObservableCollection<CollectionItem> _projectCollections = [];
    private readonly ObservableCollection<DomainItem> _knowledgeDomains = [];
    private bool _isTopPanelExpanded;
    private CollectionItem? _activeProjectCollection;
    private ChatThreadItem? _activeChatThread;

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
        ProjectCollectionsTreeView.ItemsSource = _projectCollections;
        DomainContextTreeView.ItemsSource = _knowledgeDomains;
        Loaded += MainWindow_OnLoaded;
        Closing += MainWindow_OnClosing;
        UpdateTopPanelState();
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        LeftPaneColumn.Width = new GridLength(_settings.LeftPaneWidth);
        RightPaneColumn.Width = new GridLength(_settings.RightPaneWidth);
        PromptInputRow.Height = new GridLength(_settings.PromptPaneHeight);
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
        };
        saved.Save();
    }

    private async Task LoadShellAsync()
    {
        try
        {
            var health = await _httpClient.GetFromJsonAsync<Dictionary<string, object>>("/health");
            BackendStatusTextBox.Text = "Connected";
            BackendDetailTextBox.Text = await BuildStatusDetailAsync(health);

            var domains = await _httpClient.GetFromJsonAsync<List<DomainItem>>("/domains") ?? [];
            var collections = await _httpClient.GetFromJsonAsync<List<CollectionItem>>("/collections") ?? [];

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
                await ShowChatThreadStateAsync(thread);
                break;
        }
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
            ResponseTextBox.Text = "Response output will appear here.";
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
        ResponseTextBox.Text = "Asking from this root will create a new child chat thread.";
        UpdateIncludedContextSummary();
    }

    private Task ShowChatThreadStateAsync(ChatThreadItem thread)
    {
        SetCenterMode(isRootMode: false);
        CollectionHeaderTextBlock.Text = $"Chat: {thread.Title}";
        CollectionDetailTextBlock.Text = $"Continuing thread in {_activeProjectCollection?.DisplayName}. Select the root node again to start a new chat.";
        CollectionContentsListBox.ItemsSource = thread.Messages.Select(message => $"{message.Role}: {message.Content}");
        CenterModeTextBlock.Text = "Chat thread mode: asking continues this thread";
        ResponseTextBox.Text = string.Join(
            $"{Environment.NewLine}{Environment.NewLine}",
            thread.Messages.Select(message => $"{message.Role}:{Environment.NewLine}{message.Content}")
        );
        UpdateIncludedContextSummary();
        return Task.CompletedTask;
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
            ResponseTextBox.Text = "Select a project collection on the left first.";
            return;
        }

        AskButton.IsEnabled = false;
        ResponseTextBox.Text = "Thinking...";
        try
        {
            var longTermCollections = _knowledgeDomains
                .SelectMany(domain => domain.Collections)
                .Where(collection => collection.IsIncluded)
                .Select(collection => collection.CollectionCode)
                .ToList();

            var response = await _httpClient.PostAsJsonAsync(
                "/ask",
                new
                {
                    prompt = PromptTextBox.Text,
                    shortMemoryCollectionCode = shortMemoryCollection.CollectionCode,
                    longTermCollectionCodes = longTermCollections,
                    history = _activeChatThread?.Messages.SelectMany(message =>
                        new[]
                        {
                            new { role = message.Role.ToLowerInvariant(), content = message.Content }
                        }
                    ).ToList() ?? [],
                }
            );
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<AskResponse>();
            if (payload is null)
            {
                ResponseTextBox.Text = "Backend returned no response body.";
                return;
            }

            var thread = _activeChatThread;
            if (thread is null)
            {
                thread = new ChatThreadItem
                {
                    Title = string.IsNullOrWhiteSpace(payload.Title) ? "Untitled response" : payload.Title,
                    ParentCollection = shortMemoryCollection,
                    IsSelected = true,
                };
                ClearProjectSelectionFlags();
                shortMemoryCollection.IsExpanded = true;
                shortMemoryCollection.Threads.Add(thread);
                _activeChatThread = thread;
                shortMemoryCollection.IsSelected = false;
                thread.IsEditing = false;
            }
            else if (thread.Title == "New Chat" && !string.IsNullOrWhiteSpace(payload.Title))
            {
                thread.Title = payload.Title;
                thread.IsEditing = false;
            }

            thread.Messages.Add(
                new ChatMessageItem
                {
                    Role = "User",
                    Content = PromptTextBox.Text,
                }
            );
            thread.Messages.Add(
                new ChatMessageItem
                {
                    Role = "Assistant",
                    Content = payload.Answer,
                }
            );

            var sourceLines = payload.Sources.Count == 0
                ? "No sources returned."
                : string.Join(
                    Environment.NewLine,
                    payload.Sources.Select(source => $"- {source.CollectionDisplayName}: {source.SourceName}")
                );

            ResponseTextBox.Text = $"{payload.Answer}{Environment.NewLine}{Environment.NewLine}Sources:{Environment.NewLine}{sourceLines}";
            CollectionContentsListBox.ItemsSource = thread.Messages.Select(message => $"{message.Role}: {message.Content}");
            PromptTextBox.Text = string.Empty;
            ClearProjectSelectionFlags();
            shortMemoryCollection.IsExpanded = true;
            thread.IsSelected = true;
        }
        catch (Exception ex)
        {
            ResponseTextBox.Text = ex.Message;
        }
        finally
        {
            AskButton.IsEnabled = true;
        }
    }

    private void ClearPromptButton_OnClick(object sender, RoutedEventArgs e)
    {
        PromptTextBox.Text = string.Empty;
        ResponseTextBox.Text = "Response output will appear here.";
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
                ResponseTextBox.Text = "Root creation returned no collection.";
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
            ResponseTextBox.Text = $"Created new project root: {created.DisplayName}";
        }
        catch (Exception ex)
        {
            ResponseTextBox.Text = $"Root create failed:{Environment.NewLine}{ex.Message}";
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
            ResponseTextBox.Text = "Select a root collection to delete.";
            return;
        }

        try
        {
            var response = await _httpClient.DeleteAsync($"/collections/{Uri.EscapeDataString(collection.CollectionCode)}");
            response.EnsureSuccessStatusCode();
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

            ResponseTextBox.Text = $"Deleted root collection: {collection.DisplayName}";
        }
        catch (Exception ex)
        {
            ResponseTextBox.Text = $"Root delete failed:{Environment.NewLine}{ex.Message}";
        }
    }

    private async void AddChildHeaderButton_OnClick(object sender, RoutedEventArgs e)
    {
        var parent = _activeProjectCollection ?? GetActiveProjectCollection();
        if (parent is null)
        {
            ResponseTextBox.Text = "Select a root collection first.";
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
    }

    private async void DeleteChildHeaderButton_OnClick(object sender, RoutedEventArgs e)
    {
        var thread = _activeChatThread;
        if (thread?.ParentCollection is null)
        {
            ResponseTextBox.Text = "Select a child chat to delete it.";
            return;
        }

        var parent = thread.ParentCollection;
        parent.Threads.Remove(thread);
        _activeProjectCollection = parent;
        _activeChatThread = null;
        ClearProjectSelectionFlags();
        SelectTreeItem(parent);
        ResponseTextBox.Text = $"Deleted thread: {thread.Title}";
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
    }

    private async void DeleteChildThreadButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ChatThreadItem thread || thread.ParentCollection is null)
        {
            return;
        }

        var parent = thread.ParentCollection;
        parent.Threads.Remove(thread);
        _activeProjectCollection = parent;
        _activeChatThread = null;
        ClearProjectSelectionFlags();
        SelectTreeItem(parent);
        ResponseTextBox.Text = $"Deleted thread: {thread.Title}";
    }

    private async void UploadButton_OnClick(object sender, RoutedEventArgs e)
    {
        var activeCollection = GetActiveProjectCollection();
        if (activeCollection is null)
        {
            ResponseTextBox.Text = "Select a project collection on the left before uploading.";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Upload Text Into Project Collection",
            Filter = "Supported files|*.txt;*.md;*.pdf|All files|*.*",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != true)
        {
            ResponseTextBox.Text = "Upload cancelled.";
            return;
        }

        UploadButton.IsEnabled = false;
        try
        {
            HttpResponseMessage response;
            if (string.Equals(Path.GetExtension(dialog.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                using var form = new MultipartFormDataContent();
                await using var fileStream = File.OpenRead(dialog.FileName);
                using var fileContent = new StreamContent(fileStream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                form.Add(fileContent, "file", Path.GetFileName(dialog.FileName));
                response = await _httpClient.PostAsync(
                    $"/documents/pdf?collectionCode={Uri.EscapeDataString(activeCollection.CollectionCode)}",
                    form
                );
            }
            else
            {
                var bodyText = await File.ReadAllTextAsync(dialog.FileName);
                response = await _httpClient.PostAsJsonAsync(
                    "/documents/text",
                    new
                    {
                        collectionCode = activeCollection.CollectionCode,
                        sourceName = Path.GetFileName(dialog.FileName),
                        bodyText,
                        sourceType = "file_upload",
                    }
                );
            }
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadAsStringAsync();
            ResponseTextBox.Text = $"Upload complete into {activeCollection.DisplayName}.{Environment.NewLine}{Environment.NewLine}{payload}";
            await ShowProjectCollectionStateAsync(activeCollection);
        }
        catch (Exception ex)
        {
            ResponseTextBox.Text = $"Upload failed:{Environment.NewLine}{ex.Message}";
        }
        finally
        {
            UploadButton.IsEnabled = true;
        }
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
            ResponseTextBox.Text = "Select a project collection first.";
            return;
        }

        try
        {
            var response = await _httpClient.DeleteAsync($"/documents/{Uri.EscapeDataString(document.DocumentId)}");
            response.EnsureSuccessStatusCode();
            await ShowProjectCollectionStateAsync(activeCollection);
            ResponseTextBox.Text = $"Deleted document: {document.SourceName}";
        }
        catch (Exception ex)
        {
            ResponseTextBox.Text = $"Document delete failed:{Environment.NewLine}{ex.Message}";
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
            ResponseTextBox.Text = $"Chunk load failed:{Environment.NewLine}{ex.Message}";
        }
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
            ResponseTextBox.Text = "Select a project collection first.";
            return;
        }

        try
        {
            var response = await _httpClient.DeleteAsync($"/content-units/{Uri.EscapeDataString(chunk.ContentUnitId)}");
            response.EnsureSuccessStatusCode();
            await ShowProjectCollectionStateAsync(activeCollection);
            ResponseTextBox.Text = $"Deleted chunk {chunk.UnitOrdinal}.";
        }
        catch (Exception ex)
        {
            ResponseTextBox.Text = $"Chunk delete failed:{Environment.NewLine}{ex.Message}";
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
            ResponseTextBox.Text = "Root name cannot be blank.";
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
            RefreshProjectTree();
        }
        catch (Exception ex)
        {
            collection.IsEditing = false;
            RefreshProjectTree();
            ResponseTextBox.Text = $"Rename failed:{Environment.NewLine}{ex.Message}";
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

    private void ChildNameEditor_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ChatThreadItem thread)
        {
            thread.IsEditing = false;
            RefreshProjectTree();
        }
    }

    private void ChildNameEditor_OnKeyDown(object sender, KeyEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ChatThreadItem thread)
        {
            return;
        }

        if (e.Key is Key.Enter or Key.Escape)
        {
            thread.IsEditing = false;
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
