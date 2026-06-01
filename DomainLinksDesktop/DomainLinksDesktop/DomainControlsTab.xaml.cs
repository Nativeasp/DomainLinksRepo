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

    public ObservableCollection<ControlListItem> BranchControls { get; } = [];
    public ObservableCollection<ControlSuggestionItem> Suggestions { get; } = [];

    public DomainControlsTab()
    {
        InitializeComponent();

        CountComboBox.ItemsSource = new[] { 1, 3, 5, 8, 10 };
        ControlTypeComboBox.ItemsSource = _controlTypeOptions;
    }

    internal void Configure(DomainLinksDesktopSettings settings)
    {
        _settings = settings;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(settings.BackendBaseUrl)
        };
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

        try
        {
            SetBusyState(true, $"Loading controls for {selectedDomainName}...");
            await EnsureControlTypesLoadedAsync();

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

            _loadedDomainCode = selectedDomainCode;
            UpdateBranchSummary();
            ControlsStatusTextBlock.Text = $"Loaded {BranchControls.Count} branch controls.";
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
            DraftDetailTextBox.Text = string.Empty;
            UpdateDraftButtons();
            ControlsStatusTextBlock.Text = $"Generated {Suggestions.Count} suggestions.";
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
        if (BranchControlsDataGrid.SelectedItem is not ControlListItem control)
        {
            return;
        }

        _selectedSuggestion = null;
        SuggestionsDataGrid.SelectedItem = null;
        DraftDetailTextBox.Text = BuildSavedControlDetail(control);
        UpdateDraftButtons();
        ControlsStatusTextBlock.Text = $"Viewing saved control: {control.DisplayName}";
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

    private void SuggestionsDataGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedSuggestion = SuggestionsDataGrid.SelectedItem as ControlSuggestionItem;
        if (_selectedSuggestion is not null)
        {
            BranchControlsDataGrid.SelectedItem = null;
        }

        DraftDetailTextBox.Text = _selectedSuggestion is null
            ? string.Empty
            : BuildDraftDetail(_selectedSuggestion);
        UpdateDraftButtons();
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
            DraftDetailTextBox.Text = string.Empty;
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
            : $"Branch: {_selectedDomain.DisplayName} ({_selectedDomain.DomainCode})";
    }

    private void UpdateDraftButtons()
    {
        var hasSuggestion = _selectedSuggestion is not null;
        SqlPreviewButton.IsEnabled = hasSuggestion;
        RunInsertButton.IsEnabled = hasSuggestion;
    }

    private void SetBusyState(bool isBusy, string? statusText = null)
    {
        RefreshControlsButton.IsEnabled = !isBusy;
        PromptPreviewButton.IsEnabled = !isBusy;
        GenerateSuggestionsButton.IsEnabled = !isBusy;
        RunInsertButton.IsEnabled = !isBusy && _selectedSuggestion is not null;
        SqlPreviewButton.IsEnabled = !isBusy && _selectedSuggestion is not null;
        if (!string.IsNullOrWhiteSpace(statusText))
        {
            ControlsStatusTextBlock.Text = statusText;
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
}
