using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;

namespace DomainLinksDesktop;

public partial class DomainControlsTab : UserControl
{
    private static readonly string[] AutoControlTypeOrder = ["DIRECTIVE", "PREVENTIVE", "DETERRENT", "DETECTIVE", "CORRECTIVE", "COMPENSATING"];
    private readonly ObservableCollection<ControlTypeOption> _controlTypeOptions = [];
    private DomainLinksDesktopSettings? _settings;
    private HttpClient? _httpClient;
    private DomainItem? _selectedDomain;
    private string? _loadedDomainCode;
    private int _loadRequestVersion;
    private bool _isActive;
    private bool _isDetailEditMode;
    private ControlSuggestionItem? _selectedSuggestion;
    private ControlListItem? _selectedSavedControl;

    public ObservableCollection<ControlListItem> BranchControls { get; } = [];
    public ObservableCollection<ControlListDisplayItem> DisplayControls { get; } = [];
    public ObservableCollection<ControlSuggestionItem> Suggestions { get; } = [];

    internal BrainLaunchContext? GetBrainLaunchContext()
    {
        if (BranchControlsDataGrid.SelectedItem is ControlListDisplayItem { Control: { } control })
        {
            return new BrainLaunchContext(BrainScopeKind.Control, control.ControlId, control.DisplayName,
                FocusNodeId: $"control:{control.ControlId}");
        }
        return _selectedDomain is null
            ? null
            : new BrainLaunchContext(BrainScopeKind.Domain, _selectedDomain.DomainCode, _selectedDomain.DisplayName,
                FocusNodeId: $"domain:{_selectedDomain.DomainId}");
    }

    public DomainControlsTab()
    {
        InitializeComponent();

        CountComboBox.ItemsSource = new[] { 1, 3, 5, 8, 10 };
        ControlTypeComboBox.ItemsSource = _controlTypeOptions;
        DetailControlTypeComboBox.ItemsSource = _controlTypeOptions;
        Loaded += DomainControlsTab_OnLoaded;
        ClearDetailEditor();
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
            ClearDetailEditor();
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
                ClearDetailEditor();
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
            ClearDetailEditor();
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

    private async void AutoCreateControlsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_httpClient is null || !EnsureSelectedDomain())
        {
            return;
        }

        var totalToCreate = AutoControlTypeOrder.Length * 3;
        var confirm = MessageBox.Show(
            Window.GetWindow(this),
            $"Auto-create {totalToCreate} controls for {_selectedDomain?.DisplayName} in this order?\n\nDirective, Preventive, Deterrent, Detective, Corrective, Compensating",
            "Auto Create Controls",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (_isActive)
            {
                await LoadForSelectedDomainAsync(force: false);
            }

            SetBusyState(true, "Auto-creating controls...");
            Suggestions.Clear();
            _selectedSuggestion = null;
            _selectedSavedControl = null;
            ClearDetailEditor();
            RebuildDisplayControls();
            UpdateDraftButtons();

            var createdControls = new List<ControlListItem>();
            var focus = IdeaTextBox.Text.Trim();
            var completed = 0;

            foreach (var controlTypeCode in AutoControlTypeOrder)
            {
                for (var index = 1; index <= 3; index++)
                {
                    completed++;
                    ControlsStatusTextBlock.Text = $"Auto-creating {completed}/{totalToCreate}: {controlTypeCode} #{index}";
                    var suggestion = await GenerateSingleAutoSuggestionAsync(controlTypeCode, index, createdControls, focus);
                    var createdControl = await ExecuteSuggestionAsync(suggestion);
                    createdControls.Add(createdControl);
                    BranchControls.Add(createdControl);
                    RebuildDisplayControls();
                }
            }

            await LoadForSelectedDomainAsync(force: true);
            ControlsStatusTextBlock.Text = $"Auto-created {createdControls.Count} controls.";
        }
        catch (Exception ex)
        {
            ControlsStatusTextBlock.Text = $"Auto-create failed: {ex.Message}";
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Auto Create Controls", MessageBoxButton.OK, MessageBoxImage.Error);
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
            ClearDetailEditor();
            UpdateDraftButtons();
            return;
        }

        _isDetailEditMode = false;
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
            ClearDetailEditor();
        }

        SetDetailMode(false);
        UpdateDraftButtons();
    }

    private void EditDetailButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedSuggestion is null && _selectedSavedControl is null)
        {
            return;
        }

        _isDetailEditMode = !_isDetailEditMode;
        if (_isDetailEditMode)
        {
            if (_selectedSuggestion is not null)
            {
                LoadEditor(_selectedSuggestion);
            }
            else if (_selectedSavedControl is not null)
            {
                LoadEditor(_selectedSavedControl);
            }
        }
        else
        {
            if (_selectedSuggestion is not null)
            {
                DraftDetailTextBlock.Text = BuildDraftDetail(_selectedSuggestion);
            }
            else if (_selectedSavedControl is not null)
            {
                DraftDetailTextBlock.Text = BuildSavedControlDetail(_selectedSavedControl);
            }
        }

        SetDetailMode(_isDetailEditMode);
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
            ShowReadOnlyTextWindow("Control Prompt Preview", preview?.Model ?? ResolveContentGenerationModel(), body);
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

        if (_isDetailEditMode)
        {
            ApplyEditorToSelectedSuggestion();
        }
        ShowReadOnlyTextWindow("Control SQL Preview", _selectedSuggestion.DisplayName, _selectedSuggestion.SqlPreview);
    }

    private async void RunInsertButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_httpClient is null)
        {
            return;
        }

        try
        {
            if (_selectedSuggestion is not null)
            {
                if (_isDetailEditMode)
                {
                    ApplyEditorToSelectedSuggestion();
                }
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
                ClearDetailEditor();
                RebuildDisplayControls();
                UpdateDraftButtons();
                await LoadForSelectedDomainAsync(force: true);
                ControlsStatusTextBlock.Text = "Control inserted and branch list refreshed.";
                return;
            }

            if (_selectedSavedControl is not null)
            {
                if (!_isDetailEditMode)
                {
                    return;
                }
                var payload = BuildEditorRequestPayload();
                var savedControlId = _selectedSavedControl.ControlId;
                SetBusyState(true, $"Saving {_selectedSavedControl.DisplayName}...");
                var response = await _httpClient.PutAsJsonAsync(
                    $"/controls/{Uri.EscapeDataString(savedControlId)}",
                    payload);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(await ReadErrorAsync(response));
                }

                await LoadForSelectedDomainAsync(force: true);
                _selectedSavedControl = BranchControls.FirstOrDefault(item =>
                    string.Equals(item.ControlId, savedControlId, StringComparison.OrdinalIgnoreCase));
                if (_selectedSavedControl is not null)
                {
                    var selectedDisplayItem = DisplayControls.FirstOrDefault(item =>
                        item.Control is not null
                        && string.Equals(item.Control.ControlId, savedControlId, StringComparison.OrdinalIgnoreCase));
                    if (selectedDisplayItem is not null)
                    {
                        BranchControlsDataGrid.SelectedItem = selectedDisplayItem;
                        BranchControlsDataGrid.ScrollIntoView(selectedDisplayItem);
                    }
                    _isDetailEditMode = false;
                    DraftDetailTextBlock.Text = BuildSavedControlDetail(_selectedSavedControl);
                    SetDetailMode(false);
                }
                ControlsStatusTextBlock.Text = "Control updated.";
            }
        }
        catch (Exception ex)
        {
            ControlsStatusTextBlock.Text = $"Save failed: {ex.Message}";
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Controls", MessageBoxButton.OK, MessageBoxImage.Error);
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
            model = ResolveContentGenerationModel(),
        };
    }

    private object BuildEditorRequestPayload()
    {
        var displayName = DetailNameTextBox.Text.Trim();
        var controlTypeCode = (DetailControlTypeComboBox.SelectedItem as ControlTypeOption)?.Code;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new InvalidOperationException("Control name is required.");
        }

        if (string.IsNullOrWhiteSpace(controlTypeCode))
        {
            throw new InvalidOperationException("Control type is required.");
        }

        return new
        {
            controlTypeCode,
            displayName,
            description = NormalizeMultilineField(DetailDescriptionTextBox.Text),
            controlObjective = NormalizeMultilineField(DetailObjectiveTextBox.Text),
            evidenceExpectation = NormalizeMultilineField(DetailEvidenceTextBox.Text),
        };
    }

    private async Task<ControlSuggestionItem> GenerateSingleAutoSuggestionAsync(
        string controlTypeCode,
        int ordinalWithinType,
        IReadOnlyList<ControlListItem> createdControls,
        string focus)
    {
        if (_httpClient is null || _selectedDomain is null)
        {
            throw new InvalidOperationException("A selected domain is required.");
        }

        var response = await _httpClient.PostAsJsonAsync(
            "/controls/suggest",
            new
            {
                branchRootDomainCode = _selectedDomain.DomainCode,
                mode = "auto-sequence",
                idea = string.IsNullOrWhiteSpace(focus) ? null : focus,
                controlTypeCode,
                count = 1,
                model = ResolveContentGenerationModel(),
                sequenceStepLabel = $"{controlTypeCode} control {ordinalWithinType} of 3",
                sequenceContext = BuildAutoSequenceContext(createdControls),
            });
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorAsync(response));
        }

        var payload = await response.Content.ReadFromJsonAsync<ControlSuggestionResponse>();
        var suggestion = payload?.Suggestions?.FirstOrDefault();
        if (suggestion is null)
        {
            throw new InvalidOperationException($"No suggestion returned for {controlTypeCode} control {ordinalWithinType}.");
        }

        suggestion.ControlTypeDescription = GetControlTypeDescription(suggestion.ControlTypeCode);
        return suggestion;
    }

    private async Task<ControlListItem> ExecuteSuggestionAsync(ControlSuggestionItem suggestion)
    {
        if (_httpClient is null)
        {
            throw new InvalidOperationException("HTTP client is not configured.");
        }

        var response = await _httpClient.PostAsJsonAsync(
            "/controls/suggest/execute",
            new
            {
                domainCode = suggestion.DomainCode,
                controlTypeCode = suggestion.ControlTypeCode,
                displayName = suggestion.DisplayName,
                description = suggestion.Description,
                controlObjective = suggestion.ControlObjective,
                evidenceExpectation = suggestion.EvidenceExpectation,
                controlCode = suggestion.ControlCode,
            });
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorAsync(response));
        }

        var payload = await response.Content.ReadFromJsonAsync<ControlExecutionResponse>();
        if (payload?.CreatedControl is null)
        {
            throw new InvalidOperationException($"The control '{suggestion.DisplayName}' was not returned after insert.");
        }

        return payload.CreatedControl;
    }

    private static string BuildAutoSequenceContext(IReadOnlyList<ControlListItem> createdControls)
    {
        if (createdControls.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine,
            createdControls.Select(control =>
                $"- {control.DisplayName} [{control.ControlCode}] type={control.ControlTypeCode} objective={control.ControlObjective}"));
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
        var hasSelection = hasSuggestion || hasSavedControl;
        RejectOrDeleteButton.IsEnabled = hasSuggestion || hasSavedControl;
        RejectOrDeleteButton.Content = hasSuggestion ? "Reject" : hasSavedControl ? "Delete" : "Remove";
        SqlPreviewButton.IsEnabled = hasSuggestion;
        RunInsertButton.IsEnabled = hasSuggestion || (hasSavedControl && _isDetailEditMode);
        RunInsertButton.Content = hasSuggestion ? "Run Insert" : hasSavedControl ? "Save Changes" : "Apply";
        EditDetailButton.IsEnabled = hasSelection;
        EditDetailButton.Content = _isDetailEditMode ? "👓" : "✎";
        SetDetailEditorEnabled(_isDetailEditMode && hasSelection);
    }

    private void RebuildDisplayControls()
    {
        DisplayControls.Clear();

        foreach (var control in BranchControls
                     .OrderBy(item => GetControlTypeSortOrder(item.ControlTypeCode))
                     .ThenBy(item => item.IsCurrentDomainControl ? 0 : 1)
                     .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            DisplayControls.Add(ControlListDisplayItem.FromSaved(control));
        }

        foreach (var suggestion in Suggestions
                     .OrderBy(item => GetControlTypeSortOrder(item.ControlTypeCode))
                     .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            DisplayControls.Add(ControlListDisplayItem.FromSuggestion(suggestion, _selectedDomain));
        }
    }

    private void SetBusyState(bool isBusy, string? statusText = null)
    {
        RefreshControlsButton.IsEnabled = !isBusy;
        AutoCreateControlsButton.IsEnabled = !isBusy;
        PromptPreviewButton.IsEnabled = !isBusy;
        GenerateSuggestionsButton.IsEnabled = !isBusy;
        RejectOrDeleteButton.IsEnabled = !isBusy && (_selectedSuggestion is not null || _selectedSavedControl is not null);
        RunInsertButton.IsEnabled = !isBusy && (_selectedSuggestion is not null || _selectedSavedControl is not null);
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
            ClearDetailEditor();
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
            ClearDetailEditor();
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

    private void LoadEditor(ControlSuggestionItem suggestion)
    {
        DetailModeTextBlock.Text = "Editing pending suggestion";
        DetailMetaTextBlock.Text = $"Domain: {suggestion.DomainCode}";
        DetailNameTextBox.Text = suggestion.DisplayName;
        SelectDetailControlType(suggestion.ControlTypeCode);
        DetailCodeTextBox.Text = suggestion.ControlCode;
        DetailDescriptionTextBox.Text = suggestion.Description;
        DetailObjectiveTextBox.Text = suggestion.ControlObjective;
        DetailEvidenceTextBox.Text = suggestion.EvidenceExpectation;
    }

    private void LoadEditor(ControlListItem control)
    {
        DetailModeTextBlock.Text = "Editing saved control";
        DetailMetaTextBlock.Text = $"Domain: {control.DomainDisplayName} ({control.DomainCode}) | Status: {control.Status}";
        DetailNameTextBox.Text = control.DisplayName;
        SelectDetailControlType(control.ControlTypeCode);
        DetailCodeTextBox.Text = control.ControlCode;
        DetailDescriptionTextBox.Text = control.Description;
        DetailObjectiveTextBox.Text = control.ControlObjective;
        DetailEvidenceTextBox.Text = control.EvidenceExpectation;
    }

    private static string BuildDraftDetail(ControlSuggestionItem suggestion)
    {
        return
            $"Domain: {suggestion.DomainCode}{Environment.NewLine}" +
            $"Control: {suggestion.DisplayName}{Environment.NewLine}" +
            $"Code: {suggestion.ControlCode}{Environment.NewLine}" +
            $"Type: {suggestion.ControlTypeCode}" +
            (string.IsNullOrWhiteSpace(suggestion.ControlTypeDescription) ? string.Empty : $" | {suggestion.ControlTypeDescription}") +
            $"{Environment.NewLine}{Environment.NewLine}Description{Environment.NewLine}{suggestion.Description}" +
            $"{Environment.NewLine}{Environment.NewLine}Objective{Environment.NewLine}{suggestion.ControlObjective}" +
            $"{Environment.NewLine}{Environment.NewLine}Evidence{Environment.NewLine}{suggestion.EvidenceExpectation}";
    }

    private static string BuildSavedControlDetail(ControlListItem control)
    {
        return
            $"Saved Control{Environment.NewLine}{Environment.NewLine}" +
            $"Domain: {control.DomainDisplayName} ({control.DomainCode}){Environment.NewLine}" +
            $"Control: {control.DisplayName}{Environment.NewLine}" +
            $"Code: {control.ControlCode}{Environment.NewLine}" +
            $"Type: {control.ControlTypeName} ({control.ControlTypeCode})" +
            (string.IsNullOrWhiteSpace(control.ControlTypeDescription) ? string.Empty : $" | {control.ControlTypeDescription}") +
            $"{Environment.NewLine}Status: {control.Status}" +
            $"{Environment.NewLine}{Environment.NewLine}Description{Environment.NewLine}{control.Description}" +
            $"{Environment.NewLine}{Environment.NewLine}Objective{Environment.NewLine}{control.ControlObjective}" +
            $"{Environment.NewLine}{Environment.NewLine}Evidence{Environment.NewLine}{control.EvidenceExpectation}";
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

    private void ApplyEditorToSelectedSuggestion()
    {
        if (_selectedSuggestion is null)
        {
            return;
        }

        var controlTypeCode = (DetailControlTypeComboBox.SelectedItem as ControlTypeOption)?.Code;
        var displayName = DetailNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(controlTypeCode))
        {
            throw new InvalidOperationException("Control type is required.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new InvalidOperationException("Control name is required.");
        }

        _selectedSuggestion.ControlTypeCode = controlTypeCode;
        _selectedSuggestion.ControlTypeDescription = GetControlTypeDescription(controlTypeCode);
        _selectedSuggestion.DisplayName = displayName;
        _selectedSuggestion.Description = NormalizeMultilineField(DetailDescriptionTextBox.Text) ?? string.Empty;
        _selectedSuggestion.ControlObjective = NormalizeMultilineField(DetailObjectiveTextBox.Text) ?? string.Empty;
        _selectedSuggestion.EvidenceExpectation = NormalizeMultilineField(DetailEvidenceTextBox.Text) ?? string.Empty;
        _selectedSuggestion.SqlPreview = BuildControlInsertPreview(
            _selectedSuggestion.DomainCode,
            _selectedSuggestion.ControlTypeCode,
            _selectedSuggestion.ControlCode,
            _selectedSuggestion.DisplayName,
            _selectedSuggestion.Description,
            _selectedSuggestion.ControlObjective,
            _selectedSuggestion.EvidenceExpectation);
        RebuildDisplayControls();
    }

    private static int GetControlTypeSortOrder(string? controlTypeCode)
    {
        var normalizedCode = (controlTypeCode ?? string.Empty).Trim().ToUpperInvariant();
        var index = Array.IndexOf(AutoControlTypeOrder, normalizedCode);
        return index >= 0 ? index : int.MaxValue;
    }

    private void ClearDetailEditor()
    {
        _isDetailEditMode = false;
        DraftDetailTextBlock.Text = string.Empty;
        DetailModeTextBlock.Text = "Select a control or suggestion.";
        DetailMetaTextBlock.Text = string.Empty;
        DetailNameTextBox.Text = string.Empty;
        DetailControlTypeComboBox.SelectedItem = null;
        DetailCodeTextBox.Text = string.Empty;
        DetailDescriptionTextBox.Text = string.Empty;
        DetailObjectiveTextBox.Text = string.Empty;
        DetailEvidenceTextBox.Text = string.Empty;
        SetDetailMode(false);
        SetDetailEditorEnabled(false);
    }

    private void SetDetailMode(bool isEditMode)
    {
        DetailViewScrollViewer.Visibility = isEditMode ? Visibility.Collapsed : Visibility.Visible;
        DetailEditScrollViewer.Visibility = isEditMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetDetailEditorEnabled(bool isEnabled)
    {
        DetailNameTextBox.IsEnabled = isEnabled;
        DetailControlTypeComboBox.IsEnabled = isEnabled;
        DetailDescriptionTextBox.IsEnabled = isEnabled;
        DetailObjectiveTextBox.IsEnabled = isEnabled;
        DetailEvidenceTextBox.IsEnabled = isEnabled;
    }

    private void SelectDetailControlType(string controlTypeCode)
    {
        DetailControlTypeComboBox.SelectedItem = _controlTypeOptions
            .FirstOrDefault(item => string.Equals(item.Code, controlTypeCode, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeMultilineField(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private string ResolveContentGenerationModel()
    {
        return string.IsNullOrWhiteSpace(_settings?.ContentGenerationModel)
            ? DomainLinksDesktopSettings.DefaultContentGenerationModel
            : _settings.ContentGenerationModel.Trim();
    }

    private static string BuildControlInsertPreview(
        string domainCode,
        string controlTypeCode,
        string controlCode,
        string displayName,
        string? description,
        string? controlObjective,
        string? evidenceExpectation)
    {
        return
            $"DECLARE @DomainCode NVARCHAR(100) = {ToSqlNVarCharLiteral(domainCode)};{Environment.NewLine}" +
            $"DECLARE @ControlTypeCode NVARCHAR(50) = {ToSqlNVarCharLiteral(controlTypeCode)};{Environment.NewLine}" +
            $"DECLARE @ControlCode NVARCHAR(100) = {ToSqlNVarCharLiteral(controlCode)};{Environment.NewLine}{Environment.NewLine}" +
            $"/* Name: {displayName} */{Environment.NewLine}" +
            $"/* Description: {description ?? string.Empty} */{Environment.NewLine}" +
            $"/* Objective: {controlObjective ?? string.Empty} */{Environment.NewLine}" +
            $"/* Evidence: {evidenceExpectation ?? string.Empty} */";
    }

    private static string ToSqlNVarCharLiteral(string? value)
    {
        if (value is null)
        {
            return "NULL";
        }

        return $"N'{value.Replace("'", "''")}'";
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
