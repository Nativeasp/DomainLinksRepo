using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace DomainLinksDesktop;

public partial class DomainControlsTab : UserControl
{
    private const string ControlSuggestionModelName = "qwen3.5:35b-mlx";
    private readonly ObservableCollection<ControlTypeOption> _controlTypeOptions = [];
    private DomainLinksDesktopSettings? _settings;
    private HttpClient? _httpClient;
    private DomainItem? _selectedDomain;
    private string? _loadedDomainCode;
    private int _loadRequestVersion;
    private bool _isActive;
    private ControlSuggestionItem? _selectedSuggestion;
    private ControlListItem? _selectedSavedControl;

    public ObservableCollection<ControlListItem> BranchControls { get; } = [];
    public ObservableCollection<ControlListDisplayItem> DisplayControls { get; } = [];
    public ObservableCollection<ControlSuggestionItem> Suggestions { get; } = [];

    public DomainControlsTab()
    {
        InitializeComponent();

        CountComboBox.ItemsSource = new[] { 1, 3, 5, 8, 10 };
        ControlTypeComboBox.ItemsSource = _controlTypeOptions;
        Loaded += DomainControlsTab_OnLoaded;
    }

    internal void Configure(DomainLinksDesktopSettings settings)
    {
        _settings = settings;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(settings.BackendBaseUrl)
        };

        if (IsLoaded)
        {
            ApplyLayoutSettings();
        }
    }

    internal double BranchPaneHeight => BranchControlsRow.ActualHeight > BranchControlsRow.MinHeight
        ? BranchControlsRow.ActualHeight
        : _settings?.DomainControlsBranchPaneHeight ?? 240;

    internal double SuggestionsPaneWidth => SuggestionsColumn.ActualWidth > SuggestionsColumn.MinWidth
        ? SuggestionsColumn.ActualWidth
        : _settings?.DomainControlsSuggestionPaneWidth ?? 460;

    private void DomainControlsTab_OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLayoutSettings();
    }

    private void ApplyLayoutSettings()
    {
        if (_settings is null)
        {
            return;
        }

        if (_settings.DomainControlsBranchPaneHeight > 0)
        {
            BranchControlsRow.Height = new GridLength(_settings.DomainControlsBranchPaneHeight);
        }

        if (_settings.DomainControlsSuggestionPaneWidth > 0)
        {
            SuggestionsColumn.Width = new GridLength(_settings.DomainControlsSuggestionPaneWidth);
        }
    }

    public void SetSelectedDomain(DomainItem? domain)
    {
        _selectedDomain = ResolveDomain(domain);
        if (!_isActive)
        {
            UpdateBranchSummary();
            return;
        }

        _ = LoadForSelectedDomainAsync(force: false);
    }

    public async Task ActivateAsync(DomainItem? domain)
    {
        _isActive = true;
        _selectedDomain = ResolveDomain(domain);
        await LoadForSelectedDomainAsync(force: false);
    }

    public void Deactivate()
    {
        _isActive = false;
    }

    private async Task LoadForSelectedDomainAsync(bool force)
    {
        if (_httpClient is null)
        {
            return;
        }

        if (_selectedDomain is null || string.IsNullOrWhiteSpace(_selectedDomain.DomainCode))
        {
            BranchSummaryTextBlock.Text = "Select a domain to load controls.";
            BranchControls.Clear();
            DisplayControls.Clear();
            return;
        }

        if (!force && string.Equals(_loadedDomainCode, _selectedDomain.DomainCode, StringComparison.OrdinalIgnoreCase))
        {
            UpdateBranchSummary();
            return;
        }

        var requestVersion = ++_loadRequestVersion;
        var selectedDomainCode = _selectedDomain.DomainCode;
        var selectedDomainName = _selectedDomain.DisplayName;
        var domainChanged = !string.Equals(_loadedDomainCode, selectedDomainCode, StringComparison.OrdinalIgnoreCase);

        try
        {
            SetBusyState(true, $"Loading controls for {selectedDomainName}...");
            await EnsureControlTypesLoadedAsync();

            if (domainChanged)
            {
                Suggestions.Clear();
                _selectedSuggestion = null;
                _selectedSavedControl = null;
                DraftDetailTextBlock.Text = string.Empty;
                RebuildDisplayControls();
                UpdateDraftButtons();
            }

            var branchControls = await _httpClient.GetFromJsonAsync<List<ControlListItem>>(
                $"/controls?branchRootDomainCode={Uri.EscapeDataString(selectedDomainCode)}") ?? [];

            if (requestVersion != _loadRequestVersion
                || _selectedDomain is null
                || !string.Equals(_selectedDomain.DomainCode, selectedDomainCode, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            BranchControls.Clear();
            foreach (var control in branchControls)
            {
                BranchControls.Add(control);
            }
            RebuildDisplayControls();

            _loadedDomainCode = selectedDomainCode;
            UpdateBranchSummary();
            ControlsStatusTextBlock.Text = $"Loaded {BranchControls.Count} saved controls.";
        }
        catch (Exception ex)
        {
            ControlsStatusTextBlock.Text = $"Control load failed: {ex.Message}";
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Controls", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private async Task EnsureControlTypesLoadedAsync()
    {
        if (_httpClient is null || _controlTypeOptions.Count > 0)
        {
            return;
        }

        var controlTypes = await _httpClient.GetFromJsonAsync<List<ControlTypeItem>>("/control-types") ?? [];
        _controlTypeOptions.Clear();
        _controlTypeOptions.Add(new ControlTypeOption("Any", string.Empty, "Let the assistant choose the best control type for the suggestion."));
        foreach (var controlType in controlTypes.OrderBy(item => item.DisplayOrder).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            _controlTypeOptions.Add(new ControlTypeOption(controlType.Name, controlType.Code, controlType.Description));
        }

        ControlTypeComboBox.SelectedIndex = 0;
        UpdateControlTypeComboBoxToolTip();
    }

    private void ControlTypeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateControlTypeComboBoxToolTip();
    }

    private void UpdateControlTypeComboBoxToolTip()
    {
        if (ControlTypeComboBox.SelectedItem is ControlTypeOption option)
        {
            ControlTypeComboBox.ToolTip = option.Description;
            ToolTipService.SetShowDuration(ControlTypeComboBox, 30000);
            return;
        }

        ControlTypeComboBox.ToolTip = null;
    }

    private async void RefreshControlsButton_OnClick(object sender, RoutedEventArgs e)
    {
        await LoadForSelectedDomainAsync(force: true);
    }

    private async void GenerateSuggestionsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_httpClient is null || !EnsureSelectedDomain())
        {
            return;
        }

        try
        {
            if (_isActive)
            {
                await LoadForSelectedDomainAsync(force: false);
            }

            SetBusyState(true, "Generating control suggestions...");
            var response = await _httpClient.PostAsJsonAsync("/controls/suggest", BuildSuggestionRequest());
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(await ReadErrorAsync(response));
            }

            var payload = await response.Content.ReadFromJsonAsync<ControlSuggestionResponse>();
            Suggestions.Clear();
            foreach (var suggestion in payload?.Suggestions ?? [])
            {
                suggestion.ControlTypeDescription = GetControlTypeDescription(suggestion.ControlTypeCode);
                Suggestions.Add(suggestion);
            }

            _selectedSuggestion = null;
            _selectedSavedControl = null;
            DraftDetailTextBlock.Text = string.Empty;
            RebuildDisplayControls();
            UpdateDraftButtons();
            ControlsStatusTextBlock.Text = $"Generated {Suggestions.Count} pending suggestions.";
        }
        catch (Exception ex)
        {
            ControlsStatusTextBlock.Text = $"Suggestion failed: {ex.Message}";
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Suggest Controls", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private void BranchControlsDataGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BranchControlsDataGrid.SelectedItem is not ControlListDisplayItem item)
        {
            _selectedSuggestion = null;
            _selectedSavedControl = null;
            DraftDetailTextBlock.Text = string.Empty;
            UpdateDraftButtons();
            return;
        }

        if (item.Suggestion is not null)
        {
            _selectedSuggestion = item.Suggestion;
            _selectedSavedControl = null;
            DraftDetailTextBlock.Text = BuildDraftDetail(item.Suggestion);
            ControlsStatusTextBlock.Text = $"Viewing pending suggestion: {item.DisplayName}";
        }
        else if (item.Control is not null)
        {
            _selectedSuggestion = null;
            _selectedSavedControl = item.Control;
            DraftDetailTextBlock.Text = BuildSavedControlDetail(item.Control);
            ControlsStatusTextBlock.Text = $"Viewing saved control: {item.DisplayName}";
        }
        else
        {
            _selectedSuggestion = null;
            _selectedSavedControl = null;
            DraftDetailTextBlock.Text = string.Empty;
        }

        UpdateDraftButtons();
    }

    private async void PromptPreviewButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_httpClient is null || !EnsureSelectedDomain())
        {
            return;
        }

        try
        {
            if (_isActive)
            {
                await LoadForSelectedDomainAsync(force: false);
            }

            var response = await _httpClient.PostAsJsonAsync("/controls/suggest-preview", BuildSuggestionRequest());
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(await ReadErrorAsync(response));
            }

            var preview = await response.Content.ReadFromJsonAsync<PromptPreviewResponse>();
            var body = $"SYSTEM PROMPT{Environment.NewLine}{preview?.SystemPrompt ?? string.Empty}{Environment.NewLine}{Environment.NewLine}USER PROMPT{Environment.NewLine}{preview?.UserPrompt ?? string.Empty}";
            ShowReadOnlyTextWindow("Control Prompt Preview", preview?.Model ?? ControlSuggestionModelName, body);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Prompt Preview", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SqlPreviewButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedSuggestion is null)
        {
            return;
        }

        ShowReadOnlyTextWindow("Control SQL Preview", _selectedSuggestion.DisplayName, _selectedSuggestion.SqlPreview);
    }

    private async void RunInsertButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_httpClient is null || _selectedSuggestion is null)
        {
            return;
        }

        try
        {
            SetBusyState(true, $"Inserting {_selectedSuggestion.DisplayName}...");
            var response = await _httpClient.PostAsJsonAsync(
                "/controls/suggest/execute",
                new
                {
                    domainCode = _selectedSuggestion.DomainCode,
                    controlTypeCode = _selectedSuggestion.ControlTypeCode,
                    displayName = _selectedSuggestion.DisplayName,
                    description = _selectedSuggestion.Description,
                    controlObjective = _selectedSuggestion.ControlObjective,
                    evidenceExpectation = _selectedSuggestion.EvidenceExpectation,
                    controlCode = _selectedSuggestion.ControlCode,
                });
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(await ReadErrorAsync(response));
            }

            await response.Content.ReadFromJsonAsync<ControlExecutionResponse>();
            Suggestions.Remove(_selectedSuggestion);
            _selectedSuggestion = null;
            _selectedSavedControl = null;
            DraftDetailTextBlock.Text = string.Empty;
            RebuildDisplayControls();
            UpdateDraftButtons();
            await LoadForSelectedDomainAsync(force: true);
            ControlsStatusTextBlock.Text = "Control inserted and branch list refreshed.";
        }
        catch (Exception ex)
        {
            ControlsStatusTextBlock.Text = $"Insert failed: {ex.Message}";
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Run Insert", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private object BuildSuggestionRequest()
    {
        var count = CountComboBox.SelectedItem is int selectedCount ? selectedCount : 5;
        var controlTypeCode = (ControlTypeComboBox.SelectedItem as ControlTypeOption)?.Code;
        var idea = IdeaTextBox.Text.Trim();
        return new
        {
            branchRootDomainCode = _selectedDomain?.DomainCode ?? string.Empty,
            mode = string.IsNullOrWhiteSpace(idea) ? "options" : "idea",
            idea,
            controlTypeCode = string.IsNullOrWhiteSpace(controlTypeCode) ? null : controlTypeCode,
            count,
            model = ControlSuggestionModelName,
        };
    }

    private bool EnsureSelectedDomain()
    {
        if (_selectedDomain is not null && !string.IsNullOrWhiteSpace(_selectedDomain.DomainCode))
        {
            return true;
        }

        MessageBox.Show(Window.GetWindow(this), "Select a domain first.", "Controls", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private void UpdateBranchSummary()
    {
        BranchSummaryTextBlock.Text = _selectedDomain is null
            ? "Select a domain to load controls."
            : _selectedDomain.DisplayName;
    }

    private void UpdateDraftButtons()
    {
        var hasSuggestion = _selectedSuggestion is not null;
        var hasSavedControl = _selectedSavedControl is not null;
        RejectOrDeleteButton.IsEnabled = hasSuggestion || hasSavedControl;
        RejectOrDeleteButton.Content = hasSuggestion ? "Reject" : hasSavedControl ? "Delete" : "Remove";
        SqlPreviewButton.IsEnabled = hasSuggestion;
        RunInsertButton.IsEnabled = hasSuggestion;
    }

    private void RebuildDisplayControls()
    {
        DisplayControls.Clear();

        foreach (var control in BranchControls)
        {
            DisplayControls.Add(ControlListDisplayItem.FromSaved(control));
        }

        foreach (var suggestion in Suggestions)
        {
            DisplayControls.Add(ControlListDisplayItem.FromSuggestion(suggestion, _selectedDomain));
        }
    }

    private void SetBusyState(bool isBusy, string? statusText = null)
    {
        RefreshControlsButton.IsEnabled = !isBusy;
        PromptPreviewButton.IsEnabled = !isBusy;
        GenerateSuggestionsButton.IsEnabled = !isBusy;
        RejectOrDeleteButton.IsEnabled = !isBusy && (_selectedSuggestion is not null || _selectedSavedControl is not null);
        RunInsertButton.IsEnabled = !isBusy && _selectedSuggestion is not null;
        SqlPreviewButton.IsEnabled = !isBusy && _selectedSuggestion is not null;
        if (!string.IsNullOrWhiteSpace(statusText))
        {
            ControlsStatusTextBlock.Text = statusText;
        }
    }

    private async void RejectOrDeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedSuggestion is not null)
        {
            var suggestionName = _selectedSuggestion.DisplayName;
            Suggestions.Remove(_selectedSuggestion);
            _selectedSuggestion = null;
            _selectedSavedControl = null;
            BranchControlsDataGrid.SelectedItem = null;
            DraftDetailTextBlock.Text = string.Empty;
            RebuildDisplayControls();
            UpdateDraftButtons();
            ControlsStatusTextBlock.Text = $"Rejected suggestion: {suggestionName}";
            return;
        }

        if (_httpClient is null || _selectedSavedControl is null)
        {
            return;
        }

        var control = _selectedSavedControl;
        var confirm = MessageBox.Show(
            Window.GetWindow(this),
            $"Delete '{control.DisplayName}' from the database?",
            "Delete Control",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            SetBusyState(true, $"Deleting {control.DisplayName}...");
            var response = await _httpClient.DeleteAsync($"/controls/{Uri.EscapeDataString(control.ControlId)}");
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(await ReadErrorAsync(response));
            }

            _selectedSavedControl = null;
            _selectedSuggestion = null;
            BranchControlsDataGrid.SelectedItem = null;
            DraftDetailTextBlock.Text = string.Empty;
            UpdateDraftButtons();
            await LoadForSelectedDomainAsync(force: true);
            ControlsStatusTextBlock.Text = $"Deleted control: {control.DisplayName}";
        }
        catch (Exception ex)
        {
            ControlsStatusTextBlock.Text = $"Delete failed: {ex.Message}";
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Delete Control", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private void ShowReadOnlyTextWindow(string title, string documentName, string bodyText)
    {
        var window = new DocumentTextWindow(title, documentName, bodyText, isReadOnly: true)
        {
            Owner = Window.GetWindow(this),
            Width = 880,
            Height = 680,
        };
        window.ShowDialog();
    }

    private static DomainItem? ResolveDomain(DomainItem? domain)
    {
        return domain?.SourceDomain ?? domain;
    }

    private static string BuildDraftDetail(ControlSuggestionItem suggestion)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Domain: {suggestion.DomainCode}");
        builder.AppendLine($"Control: {suggestion.DisplayName}");
        builder.AppendLine($"Code: {suggestion.ControlCode}");
        builder.AppendLine($"Type: {suggestion.ControlTypeCode}");
        if (!string.IsNullOrWhiteSpace(suggestion.ControlTypeDescription))
        {
            builder.AppendLine($"Type Description: {suggestion.ControlTypeDescription}");
        }
        builder.AppendLine();
        builder.AppendLine("Description");
        builder.AppendLine(suggestion.Description);
        builder.AppendLine();
        builder.AppendLine("Objective");
        builder.AppendLine(suggestion.ControlObjective);
        builder.AppendLine();
        builder.AppendLine("Evidence");
        builder.AppendLine(suggestion.EvidenceExpectation);
        return builder.ToString();
    }

    private static string BuildSavedControlDetail(ControlListItem control)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Saved Control");
        builder.AppendLine();
        builder.AppendLine($"Domain: {control.DomainDisplayName} ({control.DomainCode})");
        builder.AppendLine($"Control: {control.DisplayName}");
        builder.AppendLine($"Code: {control.ControlCode}");
        builder.AppendLine($"Type: {control.ControlTypeName} ({control.ControlTypeCode})");
        if (!string.IsNullOrWhiteSpace(control.ControlTypeDescription))
        {
            builder.AppendLine($"Type Description: {control.ControlTypeDescription}");
        }
        builder.AppendLine($"Status: {control.Status}");
        builder.AppendLine();
        builder.AppendLine("Description");
        builder.AppendLine(control.Description);
        builder.AppendLine();
        builder.AppendLine("Objective");
        builder.AppendLine(control.ControlObjective);
        builder.AppendLine();
        builder.AppendLine("Evidence");
        builder.AppendLine(control.EvidenceExpectation);
        return builder.ToString();
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(body)
            ? $"{(int)response.StatusCode} {response.ReasonPhrase}"
            : body;
    }

    private string GetControlTypeDescription(string controlTypeCode)
    {
        return _controlTypeOptions
            .FirstOrDefault(item => string.Equals(item.Code, controlTypeCode, StringComparison.OrdinalIgnoreCase))
            ?.Description
            ?? string.Empty;
    }

    private sealed record ControlTypeOption(string DisplayName, string Code, string Description);

    public sealed class ControlListDisplayItem
    {
        public ControlListItem? Control { get; private init; }
        public ControlSuggestionItem? Suggestion { get; private init; }
        public string DisplayName { get; private init; } = string.Empty;
        public string ControlTypeName { get; private init; } = string.Empty;
        public string ControlTypeDescription { get; private init; } = string.Empty;
        public string Status { get; private init; } = string.Empty;
        public bool IsCurrentDomainControl { get; private init; }
        public bool IsPending { get; private init; }

        public static ControlListDisplayItem FromSaved(ControlListItem control)
        {
            return new ControlListDisplayItem
            {
                Control = control,
                DisplayName = control.DisplayName,
                ControlTypeName = string.IsNullOrWhiteSpace(control.ControlTypeName) ? control.ControlTypeCode : control.ControlTypeName,
                ControlTypeDescription = control.ControlTypeDescription,
                Status = string.IsNullOrWhiteSpace(control.Status) ? "Saved" : control.Status,
                IsCurrentDomainControl = control.IsCurrentDomainControl,
                IsPending = false,
            };
        }

        public static ControlListDisplayItem FromSuggestion(ControlSuggestionItem suggestion, DomainItem? selectedDomain)
        {
            var isCurrent = selectedDomain is not null
                && string.Equals(suggestion.DomainCode, selectedDomain.DomainCode, StringComparison.OrdinalIgnoreCase);
            return new ControlListDisplayItem
            {
                Suggestion = suggestion,
                DisplayName = suggestion.DisplayName,
                ControlTypeName = suggestion.ControlTypeCode,
                ControlTypeDescription = suggestion.ControlTypeDescription,
                Status = "Pending",
                IsCurrentDomainControl = isCurrent,
                IsPending = true,
            };
        }
    }
}
