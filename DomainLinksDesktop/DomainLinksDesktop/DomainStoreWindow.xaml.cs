using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace DomainLinksDesktop;

public partial class DomainStoreWindow : Window
{
    private readonly DomainLinksDesktopSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ObservableCollection<DomainItem> _sharedRootDomains = [];
    private readonly ObservableCollection<DomainItem> _clientRootDomains = [];
    private readonly List<DomainItem> _allRootDomains = [];
    private readonly ObservableCollection<DomainTypeItem> _domainTypes = [];
    private readonly ObservableCollection<DomainOrientationItem> _domainOrientations = [];
    private DomainItem? _selectedDomain;
    private string _lastAssistResponse = string.Empty;
    private DomainItem? _pendingDragDomain;
    private Point _dragStartPoint;
    private bool _isReorderingRoots;

    internal DomainStoreWindow(DomainLinksDesktopSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        Width = settings.DomainStoreWindowWidth;
        Height = settings.DomainStoreWindowHeight;
        if (!double.IsNaN(settings.DomainStoreWindowLeft))
        {
            Left = settings.DomainStoreWindowLeft;
        }
        if (!double.IsNaN(settings.DomainStoreWindowTop))
        {
            Top = settings.DomainStoreWindowTop;
        }
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(settings.BackendBaseUrl)
        };

        SharedDomainTreeView.ItemsSource = _sharedRootDomains;
        ClientDomainTreeView.ItemsSource = _clientRootDomains;
        DomainTypeComboBox.ItemsSource = _domainTypes;
        Loaded += DomainStoreWindow_OnLoaded;
        Closing += DomainStoreWindow_OnClosing;
    }

    private async void DomainStoreWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        DomainTreeColumn.Width = new GridLength(_settings.DomainStoreLeftPaneWidth);
        SummaryColumn.Width = new GridLength(_settings.DomainStoreCenterPaneWidth);
        CollectionsSectionRow.Height = new GridLength(_settings.DomainStoreCollectionsPaneHeight);
        await ReloadAsync();
    }

    private void DomainStoreWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        var saved = new DomainLinksDesktopSettings
        {
            BackendBaseUrl = _settings.BackendBaseUrl,
            OllamaBaseUrl = _settings.OllamaBaseUrl,
            WindowWidth = _settings.WindowWidth,
            WindowHeight = _settings.WindowHeight,
            WindowLeft = _settings.WindowLeft,
            WindowTop = _settings.WindowTop,
            LeftPaneWidth = _settings.LeftPaneWidth,
            RightPaneWidth = _settings.RightPaneWidth,
            PromptPaneHeight = _settings.PromptPaneHeight,
            DomainStoreWindowWidth = Width,
            DomainStoreWindowHeight = Height,
            DomainStoreWindowLeft = Left,
            DomainStoreWindowTop = Top,
            DomainStoreLeftPaneWidth = DomainTreeColumn.ActualWidth,
            DomainStoreCenterPaneWidth = SummaryColumn.ActualWidth,
            DomainStoreCollectionsPaneHeight = CollectionsSectionRow.ActualHeight,
            LastSelectedModel = _settings.LastSelectedModel,
        };
        saved.Save();
    }

    private async Task ReloadAsync(string? domainCodeToSelect = null, string? collectionCodeToSelect = null)
    {
        try
        {
            StatusTextBlock.Text = "Loading domains...";
            var domains = await _httpClient.GetFromJsonAsync<List<DomainItem>>("/domains") ?? [];
            var collections = await _httpClient.GetFromJsonAsync<List<CollectionItem>>("/collections") ?? [];
            var domainTypes = await _httpClient.GetFromJsonAsync<List<DomainTypeItem>>("/domain-types") ?? [];
            var domainOrientations = await _httpClient.GetFromJsonAsync<List<DomainOrientationItem>>("/domain-orientations") ?? [];

            _domainTypes.Clear();
            foreach (var domainType in domainTypes
                         .OrderBy(GetDomainTypeSortBucket)
                         .ThenBy(item => item.DisplayOrder)
                         .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                _domainTypes.Add(domainType);
            }

            _domainOrientations.Clear();
            foreach (var domainOrientation in domainOrientations.OrderBy(item => item.DisplayOrder).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                _domainOrientations.Add(domainOrientation);
            }

            BuildDomainTree(domains, collections);

            var selectedDomain = FindDomainByCode(domainCodeToSelect)
                ?? _selectedDomain?.SourceDomain
                ?? _selectedDomain
                ?? _sharedRootDomains.FirstOrDefault()
                ?? _clientRootDomains.FirstOrDefault();
            if (selectedDomain is not null)
            {
                SelectDomain(selectedDomain);
            }
            else
            {
                ClearEditor();
            }

            if (!string.IsNullOrWhiteSpace(collectionCodeToSelect))
            {
                var selectedCollection = CollectionsListBox.Items.OfType<CollectionItem>()
                    .FirstOrDefault(item => string.Equals(item.CollectionCode, collectionCodeToSelect, StringComparison.OrdinalIgnoreCase));
                if (selectedCollection is not null)
                {
                    CollectionsListBox.SelectedItem = selectedCollection;
                }
            }

            StatusTextBlock.Text = "Domain store loaded.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Load failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Domain Store", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BuildDomainTree(List<DomainItem> domains, List<CollectionItem> collections)
    {
        _sharedRootDomains.Clear();
        _clientRootDomains.Clear();
        _allRootDomains.Clear();
        var domainLookup = domains.ToDictionary(domain => domain.DomainId, StringComparer.OrdinalIgnoreCase);

        foreach (var domain in domains)
        {
            domain.ParentDomain = null;
            domain.ChildDomains.Clear();
            domain.Collections.Clear();
            domain.TreeChildren.Clear();
            domain.IsExpanded = false;
            domain.IsSelected = false;
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

        foreach (var rootDomain in domains
                     .Where(item =>
                         !string.Equals(item.DomainCode, "workspace-memory", StringComparison.OrdinalIgnoreCase)
                         && string.IsNullOrWhiteSpace(item.DomainParentId))
                     .OrderBy(item => item.DisplayOrder)
                     .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            _allRootDomains.Add(rootDomain);
        }

        ApplyDomainSearchFilter();
    }

    private DomainItem? FindDomainByCode(string? domainCode)
    {
        if (string.IsNullOrWhiteSpace(domainCode))
        {
            return null;
        }

        foreach (var rootDomain in _allRootDomains)
        {
            var match = EnumerateDomains(rootDomain)
                .FirstOrDefault(item => string.Equals(item.DomainCode, domainCode, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private IEnumerable<DomainItem> EnumerateDomains(DomainItem domain)
    {
        yield return domain;
        foreach (var child in domain.ChildDomains)
        {
            foreach (var nested in EnumerateDomains(child))
            {
                yield return nested;
            }
        }
    }

    private void SelectDomain(DomainItem domain)
    {
        _selectedDomain = domain;
        ExpandAncestors(domain);
        SharedDomainTreeView.UpdateLayout();
        ClientDomainTreeView.UpdateLayout();
        SelectTreeItemByDomainCode(SharedDomainTreeView, domain.DomainCode);
        SelectTreeItemByDomainCode(ClientDomainTreeView, domain.DomainCode);
        DomainNameTextBox.Text = domain.DisplayName;
        DomainCodeTextBox.Text = domain.DomainCode;
        DomainDescriptionTextBox.Text = domain.Description ?? string.Empty;
        DomainParentPathTextBox.Text = BuildParentPath(domain);
        DomainTypeComboBox.SelectedValue = domain.DomainTypeId;
        DomainStatsTextBlock.Text =
            $"{domain.ChildDomains.Count} child domains, {domain.Collections.Count} collections";
        AssistResponseTextBox.Text = string.Empty;
        _lastAssistResponse = string.Empty;

        CollectionsListBox.ItemsSource = domain.Collections
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        CollectionsListBox.SelectedItem = null;

        _ = LoadDocumentsAsync(domain);
    }

    private void ExpandAncestors(DomainItem domain)
    {
        var current = domain.ParentDomain;
        while (current is not null)
        {
            current.IsExpanded = true;
            current = current.ParentDomain!;
        }
    }

    private async Task LoadDocumentsAsync(DomainItem domain, CollectionItem? selectedCollection = null)
    {
        try
        {
            DocumentsDataGrid.ItemsSource = null;

            var targetCollections = selectedCollection is null
                ? domain.Collections.ToList()
                : [selectedCollection];

            var documents = new List<DocumentListItem>();
            foreach (var collection in targetCollections)
            {
                var collectionDocuments =
                    await _httpClient.GetFromJsonAsync<List<DocumentListItem>>($"/documents?collectionCode={Uri.EscapeDataString(collection.CollectionCode)}")
                    ?? [];
                documents.AddRange(collectionDocuments);
            }

            DocumentScopeTextBlock.Text = selectedCollection is null
                ? $"{targetCollections.Count} collections in scope"
                : selectedCollection.DisplayName;
            DocumentsDataGrid.ItemsSource = documents
                .OrderByDescending(item => item.UpdatedAtUtc)
                .ThenBy(item => item.SourceName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            StatusTextBlock.Text = $"Loaded {documents.Count} documents.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Document load failed: {ex.Message}";
        }
    }

    private string BuildParentPath(DomainItem domain)
    {
        var parts = new Stack<string>();
        var current = domain.ParentDomain;
        while (current is not null)
        {
            parts.Push(current.DisplayName);
            current = current.ParentDomain;
        }

        return parts.Count == 0 ? "(root)" : string.Join(" / ", parts);
    }

    private void ClearEditor()
    {
        _selectedDomain = null;
        DomainNameTextBox.Text = string.Empty;
        DomainCodeTextBox.Text = string.Empty;
        DomainDescriptionTextBox.Text = string.Empty;
        DomainParentPathTextBox.Text = string.Empty;
        DomainTypeComboBox.SelectedItem = null;
        DomainStatsTextBlock.Text = "Select a domain";
        AssistResponseTextBox.Text = string.Empty;
        _lastAssistResponse = string.Empty;
        CollectionsListBox.ItemsSource = null;
        DocumentsDataGrid.ItemsSource = null;
        DocumentScopeTextBlock.Text = "No collection selected";
    }

    private void DomainSearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyDomainSearchFilter();
    }

    private bool IsDomainSearchActive()
    {
        return !string.IsNullOrWhiteSpace(DomainSearchTextBox?.Text?.Trim());
    }

    private void ApplyDomainSearchFilter()
    {
        _sharedRootDomains.Clear();
        _clientRootDomains.Clear();
        var searchText = DomainSearchTextBox?.Text?.Trim() ?? string.Empty;
        var hasSearch = !string.IsNullOrWhiteSpace(searchText);

        foreach (var rootDomain in _allRootDomains)
        {
            SetExpandedRecursive(rootDomain, false);
        }

        if (!hasSearch)
        {
            foreach (var rootDomain in _allRootDomains)
            {
                AddDomainToOrientationSection(rootDomain);
            }

            return;
        }

        foreach (var match in _allRootDomains
                     .SelectMany(EnumerateDomains)
                     .Where(domain => DomainFieldMatches(domain, searchText))
                     .OrderBy(domain => domain.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            AddDomainToOrientationSection(CreateSearchResultItem(match));
        }
    }

    private void AddDomainToOrientationSection(DomainItem domain)
    {
        if (string.Equals(domain.DomainOrientationCode, "CLIENT_SERVICES", StringComparison.OrdinalIgnoreCase))
        {
            _clientRootDomains.Add(domain);
            return;
        }

        _sharedRootDomains.Add(domain);
    }

    private static DomainItem CreateSearchResultItem(DomainItem source)
    {
        return new DomainItem
        {
            DomainId = source.DomainId,
            DomainParentId = source.DomainParentId,
            DomainTypeId = source.DomainTypeId,
            DomainOrientationId = source.DomainOrientationId,
            DisplayOrder = source.DisplayOrder,
            DomainCode = source.DomainCode,
            DomainType = source.DomainType,
            DomainOrientationCode = source.DomainOrientationCode,
            DomainOrientation = source.DomainOrientation,
            DisplayName = source.DisplayName,
            Description = source.Description,
            Status = source.Status,
            SourceDomain = source,
        };
    }

    private static bool DomainFieldMatches(DomainItem domain, string searchText)
    {
        return domain.DisplayName?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false;
    }

    private void SetExpandedRecursive(DomainItem domain, bool isExpanded)
    {
        domain.IsExpanded = isExpanded;
        foreach (var child in domain.ChildDomains)
        {
            SetExpandedRecursive(child, isExpanded);
        }
    }

    private async void SaveDomainButton_OnClick(object sender, RoutedEventArgs e)
    {
        var targetDomain = ResolveSelectedDomainForAction(showPrompt: false);
        if (targetDomain is null)
        {
            return;
        }

        var displayName = DomainNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            MessageBox.Show(this, "Display name is required.", "Domain Store", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            StatusTextBlock.Text = $"Saving {targetDomain.DisplayName}...";
            var response = await _httpClient.PutAsJsonAsync(
                $"/domains/{Uri.EscapeDataString(targetDomain.DomainCode)}",
                new
                {
                    displayName,
                    description = DomainDescriptionTextBox.Text,
                    domainTypeId = GetSelectedDomainTypeId(),
                    domainOrientationId = targetDomain.DomainOrientationId,
                });
            response.EnsureSuccessStatusCode();
            await ReloadAsync(targetDomain.DomainCode);
            StatusTextBlock.Text = $"Saved {displayName}.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Save failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Domain Store", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RevertDomainButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedDomain is not null)
        {
            SelectDomain(_selectedDomain);
            StatusTextBlock.Text = "Changes reverted.";
        }
    }

    private async void AddSharedRootButton_OnClick(object sender, RoutedEventArgs e)
    {
        await CreateDomainAsync(null, GetOrientationIdByCode("SHARED_SERVICES"));
    }

    private async void AddClientRootButton_OnClick(object sender, RoutedEventArgs e)
    {
        await CreateDomainAsync(null, GetOrientationIdByCode("CLIENT_SERVICES"));
    }

    private async void AddChildButton_OnClick(object sender, RoutedEventArgs e)
    {
        var targetDomain = ResolveSelectedDomainForAction();
        if (targetDomain is null)
        {
            return;
        }

        await CreateDomainAsync(targetDomain, targetDomain.DomainOrientationId);
    }

    private async void DeleteDomainButton_OnClick(object sender, RoutedEventArgs e)
    {
        var targetDomain = ResolveSelectedDomainForAction();
        if (targetDomain is null)
        {
            return;
        }

        try
        {
            StatusTextBlock.Text = $"Checking delete impact for {targetDomain.DisplayName}...";
            var preview = await _httpClient.GetFromJsonAsync<DomainDeletePreviewResponse>(
                $"/domains/{Uri.EscapeDataString(targetDomain.DomainCode)}/delete-preview");

            if (preview is null)
            {
                throw new InvalidOperationException("Delete preview returned no response.");
            }

            if (preview.DocumentCount > 0)
            {
                var message =
                    $"Delete {targetDomain.DisplayName} and all underlying data?{Environment.NewLine}{Environment.NewLine}" +
                    $"Domains: {preview.DomainCount}{Environment.NewLine}" +
                    $"Collections: {preview.CollectionCount}{Environment.NewLine}" +
                    $"Documents: {preview.DocumentCount}{Environment.NewLine}{Environment.NewLine}" +
                    "This cannot be undone from the app.";

                if (MessageBox.Show(
                        this,
                        message,
                        "Delete Domain",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    StatusTextBlock.Text = "Delete cancelled.";
                    return;
                }
            }

            StatusTextBlock.Text = $"Deleting {targetDomain.DisplayName}...";
            var response = await _httpClient.DeleteAsync($"/domains/{Uri.EscapeDataString(targetDomain.DomainCode)}");
            response.EnsureSuccessStatusCode();
            var deletedDisplayName = targetDomain.DisplayName;
            await ReloadAsync();
            StatusTextBlock.Text = $"Deleted {deletedDisplayName}.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Delete failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Domain Store", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task CreateDomainAsync(DomainItem? parentDomain, int? domainOrientationId)
    {
        if (parentDomain is null && domainOrientationId is null)
        {
            MessageBox.Show(this, "A domain orientation is required for a new root domain.", "Domain Store", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var prompt = new TextPromptWindow(
            parentDomain is null ? "New Root Domain" : "New Child Domain",
            parentDomain is null ? "Root domain name" : $"Child domain name under {parentDomain.DisplayName}",
            hint: "The domain code will be generated from this name and can be refined later.");
        prompt.Owner = this;
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var domainName = prompt.ResultText;
            var response = await _httpClient.PostAsJsonAsync(
                "/domains",
                new
                {
                    domainCode = Slugify(domainName),
                    domainTypeId = parentDomain?.DomainTypeId,
                    domainOrientationId,
                    domainParentId = parentDomain?.DomainId,
                    displayName = domainName,
                    description = string.Empty,
                });
            response.EnsureSuccessStatusCode();
            await ReloadAsync(Slugify(domainName));
            StatusTextBlock.Text = $"Created domain {domainName}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Domain Store", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void NewCollectionButton_OnClick(object sender, RoutedEventArgs e)
    {
        var targetDomain = ResolveSelectedDomainForAction();
        if (targetDomain is null)
        {
            return;
        }

        var prompt = new TextPromptWindow(
            "New Collection",
            $"Collection name for {targetDomain.DisplayName}",
            hint: "The collection code will be generated from the display name.");
        prompt.Owner = this;
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var collectionName = prompt.ResultText;
            var response = await _httpClient.PostAsJsonAsync(
                "/collections",
                new
                {
                    domainCode = targetDomain.DomainCode,
                    collectionCode = Slugify(collectionName),
                    displayName = collectionName,
                    description = string.Empty,
                });
            response.EnsureSuccessStatusCode();
            await ReloadAsync(targetDomain.DomainCode, Slugify(collectionName));
            StatusTextBlock.Text = $"Created collection {collectionName}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Domain Store", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteCollectionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (CollectionsListBox.SelectedItem is not CollectionItem collection)
        {
            MessageBox.Show(this, "Select a collection first.", "Domain Store", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            StatusTextBlock.Text = $"Checking delete impact for {collection.DisplayName}...";
            var preview = await _httpClient.GetFromJsonAsync<CollectionDeletePreviewResponse>(
                $"/collections/{Uri.EscapeDataString(collection.CollectionCode)}/delete-preview");

            if (preview is null)
            {
                throw new InvalidOperationException("Delete preview returned no response.");
            }

            if (preview.DocumentCount > 0)
            {
                var message =
                    $"Delete collection {collection.DisplayName} and its underlying data?{Environment.NewLine}{Environment.NewLine}" +
                    $"Documents: {preview.DocumentCount}{Environment.NewLine}{Environment.NewLine}" +
                    "This cannot be undone from the app.";

                if (MessageBox.Show(
                        this,
                        message,
                        "Delete Collection",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    StatusTextBlock.Text = "Delete cancelled.";
                    return;
                }
            }

            StatusTextBlock.Text = $"Deleting {collection.DisplayName}...";
            var response = await _httpClient.DeleteAsync($"/collections/{Uri.EscapeDataString(collection.CollectionCode)}");
            response.EnsureSuccessStatusCode();
            await ReloadAsync(_selectedDomain?.DomainCode);
            StatusTextBlock.Text = $"Deleted {collection.DisplayName}.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Delete failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Domain Store", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void UploadDocumentButton_OnClick(object sender, RoutedEventArgs e)
    {
        var targetCollection = ResolveTargetCollection();
        if (targetCollection is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = $"Upload Document Into {targetCollection.DisplayName}",
            Filter = "Supported files|*.txt;*.md;*.json;*.csv;*.log;*.pdf|Text files|*.txt;*.md;*.json;*.csv;*.log|PDF files|*.pdf|All files|*.*",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            StatusTextBlock.Text = $"Uploading {Path.GetFileName(dialog.FileName)}...";
            HttpResponseMessage response;
            if (string.Equals(Path.GetExtension(dialog.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                using var form = new MultipartFormDataContent();
                await using var stream = File.OpenRead(dialog.FileName);
                using var content = new StreamContent(stream);
                form.Add(content, "file", Path.GetFileName(dialog.FileName));
                response = await _httpClient.PostAsync(
                    $"/documents/pdf?collectionCode={Uri.EscapeDataString(targetCollection.CollectionCode)}",
                    form);
            }
            else
            {
                var bodyText = await File.ReadAllTextAsync(dialog.FileName, Encoding.UTF8);
                response = await _httpClient.PostAsJsonAsync(
                    "/documents/text",
                    new
                    {
                        collectionCode = targetCollection.CollectionCode,
                        sourceName = Path.GetFileName(dialog.FileName),
                        bodyText,
                        sourceType = "file_upload",
                    });
            }

            response.EnsureSuccessStatusCode();
            await LoadDocumentsAsync(_selectedDomain!, targetCollection);
            StatusTextBlock.Text = $"Uploaded {Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Domain Store", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddTextButton_OnClick(object sender, RoutedEventArgs e)
    {
        var targetCollection = ResolveTargetCollection();
        if (targetCollection is null)
        {
            return;
        }

        var editor = new DocumentTextWindow("Add Text Document", string.Empty, string.Empty, isReadOnly: false)
        {
            Owner = this
        };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "/documents/text",
                new
                {
                    collectionCode = targetCollection.CollectionCode,
                    sourceName = editor.DocumentName,
                    bodyText = editor.DocumentBody,
                    sourceType = "pasted_text",
                });
            response.EnsureSuccessStatusCode();
            await LoadDocumentsAsync(_selectedDomain!, targetCollection);
            StatusTextBlock.Text = $"Added {editor.DocumentName}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Domain Store", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OpenTextButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DocumentsDataGrid.SelectedItem is not DocumentListItem document)
        {
            MessageBox.Show(this, "Select a document first.", "Domain Store", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            StatusTextBlock.Text = $"Loading text for {document.SourceName}...";
            var chunks = await _httpClient.GetFromJsonAsync<List<ContentUnitListItem>>($"/documents/{Uri.EscapeDataString(document.DocumentId)}/chunks")
                ?? [];
            var bodyText = string.Join(Environment.NewLine + Environment.NewLine, chunks.OrderBy(item => item.UnitOrdinal).Select(item => item.BodyText));
            var viewer = new DocumentTextWindow("Document Text", document.SourceName, bodyText, isReadOnly: true)
            {
                Owner = this
            };
            viewer.ShowDialog();
            StatusTextBlock.Text = $"Opened {document.SourceName}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Domain Store", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteDocumentButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DocumentsDataGrid.SelectedItem is not DocumentListItem document)
        {
            MessageBox.Show(this, "Select a document first.", "Domain Store", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(
                this,
                $"Delete {document.SourceName}?",
                "Domain Store",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var response = await _httpClient.DeleteAsync($"/documents/{Uri.EscapeDataString(document.DocumentId)}");
            response.EnsureSuccessStatusCode();
            await LoadDocumentsAsync(_selectedDomain!, CollectionsListBox.SelectedItem as CollectionItem);
            StatusTextBlock.Text = $"Deleted {document.SourceName}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Domain Store", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private CollectionItem? ResolveTargetCollection()
    {
        var targetDomain = ResolveSelectedDomainForAction();
        if (targetDomain is null)
        {
            return null;
        }

        if (CollectionsListBox.SelectedItem is CollectionItem selectedCollection)
        {
            return selectedCollection;
        }

        if (targetDomain.Collections.Count == 1)
        {
            var collection = targetDomain.Collections[0];
            CollectionsListBox.SelectedItem = collection;
            return collection;
        }

        MessageBox.Show(this, "Select a collection first.", "Domain Store", MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private int? GetSelectedDomainTypeId()
    {
        return DomainTypeComboBox.SelectedValue is int value ? value : null;
    }

    private async Task ReorderSiblingDomainsAsync(TreeView treeView, DomainItem sourceDomain, DomainItem targetDomain, bool insertAfter)
    {
        if (_isReorderingRoots)
        {
            return;
        }

        if (IsDomainSearchActive())
        {
            StatusTextBlock.Text = "Clear domain search before reordering domains.";
            return;
        }

        if (!string.Equals(sourceDomain.ParentDomain?.DomainId, targetDomain.ParentDomain?.DomainId, StringComparison.OrdinalIgnoreCase))
        {
            StatusTextBlock.Text = "Only sibling domains can be reordered.";
            return;
        }

        var siblings = GetSiblingCollection(treeView, sourceDomain).ToList();
        var sourceIndex = siblings.FindIndex(item => string.Equals(item.DomainCode, sourceDomain.DomainCode, StringComparison.OrdinalIgnoreCase));
        var targetIndex = siblings.FindIndex(item => string.Equals(item.DomainCode, targetDomain.DomainCode, StringComparison.OrdinalIgnoreCase));
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        siblings.RemoveAt(sourceIndex);
        if (sourceIndex < targetIndex)
        {
            targetIndex--;
        }

        var insertIndex = insertAfter ? targetIndex + 1 : targetIndex;
        insertIndex = Math.Max(0, Math.Min(insertIndex, siblings.Count));
        siblings.Insert(insertIndex, sourceDomain);

        var orientationCode = sourceDomain.ParentDomain is null
            ? treeView == SharedDomainTreeView ? "SHARED_SERVICES" : "CLIENT_SERVICES"
            : null;

        try
        {
            _isReorderingRoots = true;
            StatusTextBlock.Text = $"Reordering {sourceDomain.DisplayName}...";

            var response = await _httpClient.PutAsJsonAsync(
                "/domain-sibling-order",
                new
                {
                    parentDomainId = sourceDomain.ParentDomain?.DomainId,
                    orientationCode,
                    orderedDomainCodes = siblings.Select(item => item.DomainCode).ToList(),
                });
            response.EnsureSuccessStatusCode();

            await ReloadAsync(sourceDomain.DomainCode);
            StatusTextBlock.Text = $"Reordered {sourceDomain.DisplayName}.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Reorder failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Domain Store", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isReorderingRoots = false;
        }
    }

    private async Task MoveSelectedDomainAsync(TreeView treeView, int direction)
    {
        if (IsDomainSearchActive())
        {
            StatusTextBlock.Text = "Clear domain search before reordering domains.";
            return;
        }

        if (ResolveSelectedDomainForAction() is not { } selectedDomain)
        {
            StatusTextBlock.Text = "Select a domain to reorder.";
            return;
        }

        var siblings = GetSiblingCollection(treeView, selectedDomain).ToList();
        var currentIndex = siblings.FindIndex(item => string.Equals(item.DomainCode, selectedDomain.DomainCode, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
        {
            return;
        }

        var targetIndex = currentIndex + direction;
        if (targetIndex < 0 || targetIndex >= siblings.Count)
        {
            return;
        }

        await ReorderSiblingDomainsAsync(treeView, selectedDomain, siblings[targetIndex], insertAfter: direction > 0);
    }

    private DomainItem? ResolveSelectedDomainForAction(bool showPrompt = true)
    {
        var editorDomainCode = DomainCodeTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(editorDomainCode))
        {
            var editorDomain = FindDomainByCode(editorDomainCode);
            if (editorDomain is not null)
            {
                if (!ReferenceEquals(_selectedDomain, editorDomain))
                {
                    SelectDomain(editorDomain);
                }

                return editorDomain;
            }
        }

        var selectedTreeDomain =
            (SharedDomainTreeView.SelectedItem as DomainItem)?.SourceDomain
            ?? SharedDomainTreeView.SelectedItem as DomainItem
            ?? (ClientDomainTreeView.SelectedItem as DomainItem)?.SourceDomain
            ?? ClientDomainTreeView.SelectedItem as DomainItem;

        if (selectedTreeDomain is not null)
        {
            if (!ReferenceEquals(_selectedDomain, selectedTreeDomain))
            {
                SelectDomain(selectedTreeDomain);
            }

            return selectedTreeDomain;
        }

        if (_selectedDomain is not null)
        {
            return _selectedDomain.SourceDomain ?? _selectedDomain;
        }

        if (showPrompt)
        {
            MessageBox.Show(this, "Select a domain first.", "Domain Store", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        return null;
    }

    private int? GetOrientationIdByCode(string orientationCode)
    {
        return _domainOrientations
            .FirstOrDefault(item => string.Equals(item.Code, orientationCode, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    private static int GetDomainTypeSortBucket(DomainTypeItem item)
    {
        var code = item.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        return code switch
        {
            "STRATEGIC" => 0,
            "TACTICAL" => 98,
            "OPERATIONAL" => 99,
            _ => 50,
        };
    }

    private async void GenerateAssistButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedDomain is null)
        {
            MessageBox.Show(this, "Select a domain first.", "Domain Store", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var instruction = AssistInstructionTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(instruction))
        {
            MessageBox.Show(this, "Enter an instruction for the writing assist.", "Domain Store", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            GenerateAssistButton.IsEnabled = false;
            StatusTextBlock.Text = $"Generating wording help for {_selectedDomain.DisplayName}...";
            AssistResponseTextBox.Text = "Generating suggestion...";

            var response = await _httpClient.PostAsJsonAsync(
                "/domains/assist",
                new
                {
                    domainCode = _selectedDomain.DomainCode,
                    instruction,
                    draftText = DomainDescriptionTextBox.Text,
                });
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<DomainAssistResponse>();
            _lastAssistResponse = payload?.Answer?.Trim() ?? string.Empty;
            AssistResponseTextBox.Text = _lastAssistResponse;
            StatusTextBlock.Text = string.IsNullOrWhiteSpace(payload?.SystemPromptLabel)
                ? "Suggestion ready."
                : $"Suggestion ready via {payload.SystemPromptLabel}.";
        }
        catch (Exception ex)
        {
            AssistResponseTextBox.Text = string.Empty;
            _lastAssistResponse = string.Empty;
            StatusTextBlock.Text = $"Assist failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Domain Store", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            GenerateAssistButton.IsEnabled = true;
        }
    }

    private void AssistQuickActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string instruction })
        {
            AssistInstructionTextBox.Text = instruction;
        }
    }

    private void ReplaceAssistButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastAssistResponse))
        {
            return;
        }

        DomainDescriptionTextBox.Text = _lastAssistResponse;
        StatusTextBlock.Text = "Suggestion replaced the current description.";
    }

    private void AppendAssistButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastAssistResponse))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(DomainDescriptionTextBox.Text))
        {
            DomainDescriptionTextBox.Text = _lastAssistResponse;
        }
        else
        {
            DomainDescriptionTextBox.Text = $"{DomainDescriptionTextBox.Text.TrimEnd()}{Environment.NewLine}{Environment.NewLine}{_lastAssistResponse}";
        }

        StatusTextBlock.Text = "Suggestion appended to the current description.";
    }

    private void CopyAssistButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastAssistResponse))
        {
            return;
        }

        Clipboard.SetText(_lastAssistResponse);
        StatusTextBlock.Text = "Suggestion copied to the clipboard.";
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
            else if (builder.Length == 0 || builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "new-domain" : slug;
    }

    private void DomainTreeView_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is DomainItem domain)
        {
            SelectDomain(domain.SourceDomain ?? domain);
        }
    }

    private void DomainTreeView_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _pendingDragDomain = null;

        if (sender is not TreeView treeView)
        {
            return;
        }

        var item = GetTreeViewItemFromSource(treeView, e.OriginalSource as DependencyObject);
        if (item?.DataContext is not DomainItem domain)
        {
            return;
        }

        var resolvedDomain = domain.SourceDomain ?? domain;
        _pendingDragDomain = resolvedDomain;
    }

    private void DomainTreeView_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not TreeView treeView)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed || _pendingDragDomain is null || IsDomainSearchActive())
        {
            return;
        }

        var currentPoint = e.GetPosition(this);
        if (Math.Abs(currentPoint.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPoint.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var dragDomain = _pendingDragDomain;
        _pendingDragDomain = null;
        DragDrop.DoDragDrop(treeView, new DataObject(typeof(DomainItem), dragDomain), DragDropEffects.Move);
    }

    private void DomainTreeView_OnDragOver(object sender, DragEventArgs e)
    {
        if (sender is not TreeView treeView
            || IsDomainSearchActive()
            || !e.Data.GetDataPresent(typeof(DomainItem)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var sourceDomain = e.Data.GetData(typeof(DomainItem)) as DomainItem;
        var targetItem = GetTreeViewItemFromSource(treeView, e.OriginalSource as DependencyObject);
        var targetDomain = targetItem?.DataContext as DomainItem;
        var resolvedTarget = targetDomain?.SourceDomain ?? targetDomain;

        if (sourceDomain is null
            || resolvedTarget is null
            || string.Equals(sourceDomain.DomainCode, resolvedTarget.DomainCode, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(sourceDomain.ParentDomain?.DomainId, resolvedTarget.ParentDomain?.DomainId, StringComparison.OrdinalIgnoreCase)
            || (sourceDomain.ParentDomain is null
                && !string.Equals(sourceDomain.DomainOrientationCode, resolvedTarget.DomainOrientationCode, StringComparison.OrdinalIgnoreCase)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private async void DomainTreeView_OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not TreeView treeView
            || IsDomainSearchActive()
            || !e.Data.GetDataPresent(typeof(DomainItem)))
        {
            return;
        }

        var sourceDomain = e.Data.GetData(typeof(DomainItem)) as DomainItem;
        var targetItem = GetTreeViewItemFromSource(treeView, e.OriginalSource as DependencyObject);
        var targetDomain = (targetItem?.DataContext as DomainItem)?.SourceDomain ?? targetItem?.DataContext as DomainItem;
        if (sourceDomain is null || targetDomain is null)
        {
            return;
        }

        var resolvedTargetItem = targetItem;
        if (resolvedTargetItem is null)
        {
            return;
        }

        var targetPosition = e.GetPosition(resolvedTargetItem);
        var insertAfter = targetPosition.Y > (resolvedTargetItem.ActualHeight / 2);
        await ReorderSiblingDomainsAsync(treeView, sourceDomain, targetDomain, insertAfter);
    }

    private async void DomainTreeView_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TreeView treeView || Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Alt))
        {
            return;
        }

        if (e.Key == Key.Up)
        {
            e.Handled = true;
            await MoveSelectedDomainAsync(treeView, -1);
        }
        else if (e.Key == Key.Down)
        {
            e.Handled = true;
            await MoveSelectedDomainAsync(treeView, 1);
        }
    }

    private IEnumerable<DomainItem> GetSiblingCollection(TreeView treeView, DomainItem domain)
    {
        if (domain.ParentDomain is not null)
        {
            return domain.ParentDomain.ChildDomains;
        }

        return treeView == SharedDomainTreeView ? _sharedRootDomains : _clientRootDomains;
    }

    private static TreeViewItem? GetTreeViewItemFromSource(ItemsControl treeView, DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is TreeViewItem item)
            {
                return item;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void SelectTreeItemByDomainCode(TreeView treeView, string domainCode)
    {
        if (string.IsNullOrWhiteSpace(domainCode))
        {
            return;
        }

        var treeViewItem = FindTreeViewItemByDomainCode(treeView, domainCode);
        if (treeViewItem is null)
        {
            return;
        }

        treeViewItem.IsSelected = true;
        treeViewItem.Focus();
    }

    private TreeViewItem? FindTreeViewItemByDomainCode(ItemsControl parent, string domainCode)
    {
        parent.UpdateLayout();

        for (var index = 0; index < parent.Items.Count; index++)
        {
            var item = parent.Items[index];
            var container = parent.ItemContainerGenerator.ContainerFromIndex(index) as TreeViewItem;
            if (container is null)
            {
                continue;
            }

            if (item is DomainItem domain
                && string.Equals(domain.DomainCode, domainCode, StringComparison.OrdinalIgnoreCase))
            {
                return container;
            }

            var childContainer = FindTreeViewItemByDomainCode(container, domainCode);
            if (childContainer is not null)
            {
                return childContainer;
            }
        }

        return null;
    }

    private async void CollectionsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectedDomain is null)
        {
            return;
        }

        await LoadDocumentsAsync(_selectedDomain, CollectionsListBox.SelectedItem as CollectionItem);
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ReloadAsync(_selectedDomain?.DomainCode, (CollectionsListBox.SelectedItem as CollectionItem)?.CollectionCode);
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DocumentsDataGrid_OnMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OpenTextButton_OnClick(sender, new RoutedEventArgs(Button.ClickEvent));
    }
}
