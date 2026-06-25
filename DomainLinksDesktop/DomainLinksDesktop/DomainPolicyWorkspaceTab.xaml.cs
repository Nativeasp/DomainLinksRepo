using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace DomainLinksDesktop;

public partial class DomainPolicyWorkspaceTab : UserControl
{
    private DomainLinksDesktopSettings? _settings;
    private HttpClient? _httpClient;
    private DomainItem? _selectedDomain;
    private bool _isActive;
    private bool _isSyncingControlSelection;
    private bool _isLoadingPolicySelection;
    private string _selectedGroupingModeCode = "smart";
    private bool _isApplyingAiGrouping;
    private List<PolicyDraftControlGroupingItem> _currentOrderedControlGroupings = [];
    private readonly DispatcherTimer _groupingActivityTimer;
    private int _groupingActivityFrame;
    private string _groupingActivityBaseText = "Applying AI grouping...";

    public ObservableCollection<PolicyListItem> Policies { get; } = [];
    public ObservableCollection<SelectableControlItem> SelectableControls { get; } = [];
    public ObservableCollection<ControlGroupingModeItem> ControlGroupingModes { get; } =
    [
        new ControlGroupingModeItem { Code = "ai", DisplayName = "AI Grouping" },
        new ControlGroupingModeItem { Code = "smart", DisplayName = "Smart" },
        new ControlGroupingModeItem { Code = "type", DisplayName = "By Control Type" },
        new ControlGroupingModeItem { Code = "domain", DisplayName = "By Domain" },
        new ControlGroupingModeItem { Code = "none", DisplayName = "None" },
    ];
    public ICollectionView SelectableControlsView { get; }

    public DomainPolicyWorkspaceTab()
    {
        InitializeComponent();
        DataContext = this;
        _groupingActivityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(280)
        };
        _groupingActivityTimer.Tick += GroupingActivityTimer_OnTick;
        SelectableControlsView = CollectionViewSource.GetDefaultView(SelectableControls);
        DraftTabContent.IncludedControlCodesChanged += DraftTabContent_OnIncludedControlCodesChanged;
        Loaded += DomainPolicyWorkspaceTab_OnLoaded;
    }

    private async void DomainPolicyWorkspaceTab_OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLayoutSettings();

        if (ControlGroupingComboBox.SelectedValue is null)
        {
            ControlGroupingComboBox.SelectedValue = _selectedGroupingModeCode;
        }

        await ApplyControlGroupingAsync();
    }

    internal void Configure(DomainLinksDesktopSettings settings)
    {
        _settings = settings;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(settings.BackendBaseUrl)
        };
        DraftTabContent.Configure(settings);

        if (IsLoaded)
        {
            ApplyLayoutSettings();
        }
    }

    internal double PoliciesPaneWidth => WorkspacePoliciesColumn.ActualWidth > WorkspacePoliciesColumn.MinWidth
        ? WorkspacePoliciesColumn.ActualWidth
        : _settings?.PolicyWorkspacePoliciesPaneWidth ?? 430;

    internal double ControlSelectionPaneHeight => ControlSelectionRow.ActualHeight > ControlSelectionRow.MinHeight
        ? ControlSelectionRow.ActualHeight
        : _settings?.PolicyWorkspaceControlSelectionPaneHeight ?? 220;

    private void ApplyLayoutSettings()
    {
        if (_settings is null)
        {
            return;
        }

        if (_settings.PolicyWorkspacePoliciesPaneWidth > 0)
        {
            WorkspacePoliciesColumn.Width = new GridLength(_settings.PolicyWorkspacePoliciesPaneWidth);
        }

        if (_settings.PolicyWorkspaceControlSelectionPaneHeight > 0)
        {
            ControlSelectionRow.Height = new GridLength(_settings.PolicyWorkspaceControlSelectionPaneHeight);
        }
    }

    public void SetSelectedDomain(DomainItem? domain)
    {
        _selectedDomain = ResolveDomain(domain);
        DraftTabContent.SetSelectedDomain(_selectedDomain);
        UpdateSummary();

        if (_isActive)
        {
            _ = LoadControlsAsync();
            _ = LoadPoliciesAsync(force: true);
        }
    }

    public async Task ActivateAsync(DomainItem? domain)
    {
        SetSelectedDomain(domain);
        _isActive = true;
        await LoadControlsAsync();
        await DraftTabContent.ActivateAsync(_selectedDomain);
        await LoadPoliciesAsync(force: true);
    }

    public void Deactivate()
    {
        _isActive = false;
        StopGroupingActivity();
        DraftTabContent.Deactivate();
    }

    private void GroupingActivityTimer_OnTick(object? sender, EventArgs e)
    {
        var frames = new[] { ".", "..", "...", "...." };
        SetWorkspaceStatus(_groupingActivityBaseText, frames[_groupingActivityFrame % frames.Length]);
        _groupingActivityFrame++;
    }

    private void StartGroupingActivity(string baseText)
    {
        _groupingActivityFrame = 0;
        _groupingActivityBaseText = string.IsNullOrWhiteSpace(baseText)
            ? "Applying AI grouping"
            : baseText.Trim().TrimEnd('.');
        SetWorkspaceStatus(_groupingActivityBaseText, ".");
        _groupingActivityTimer.Start();
    }

    private void StopGroupingActivity()
    {
        _groupingActivityTimer.Stop();
    }

    private void SetWorkspaceStatus(string text, string dots = "")
    {
        WorkspaceStatusBaseRun.Text = text;
        WorkspaceStatusDotsRun.Text = dots;
    }

    private async void DraftPolicyButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusyState(true);
            await DraftTabContent.ExecuteDraftPolicyAsync();
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private async void SavePolicyButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusyState(true);
            await DraftTabContent.ExecuteSaveDraftAsync();
            await LoadPoliciesAsync(force: true);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private async void ClearPolicyTestDataButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusyState(true);
            await DraftTabContent.ExecuteClearPolicyTestDataAsync();
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private void SelectAllControlsButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetAllControlsIncluded(isIncluded: true);
    }

    private void ClearControlsButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetAllControlsIncluded(isIncluded: false);
    }

    private async void ControlGroupingComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ControlGroupingComboBox.SelectedValue is not string code || string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        _selectedGroupingModeCode = code;
        await ApplyControlGroupingAsync();
    }

    private void SelectableControlsListBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A)
        {
            SelectableControlsListBox.SelectAll();
            e.Handled = true;
            return;
        }

        var selectedControls = SelectableControlsListBox.SelectedItems
            .OfType<SelectableControlItem>()
            .ToList();

        if ((Keyboard.Modifiers == ModifierKeys.Control)
            && (e.Key == Key.Delete || e.Key == Key.Back)
            && selectedControls.Count > 0)
        {
            foreach (var control in selectedControls)
            {
                control.IsIncluded = false;
            }

            e.Handled = true;
            return;
        }

        if (e.Key != Key.Space)
        {
            return;
        }

        if (selectedControls.Count == 0
            && SelectableControlsListBox.SelectedItem is SelectableControlItem selectedControl)
        {
            selectedControls.Add(selectedControl);
        }

        if (selectedControls.Count == 0)
        {
            return;
        }

        var nextIncludedState = selectedControls.Any(item => !item.IsIncluded);
        foreach (var control in selectedControls)
        {
            control.IsIncluded = nextIncludedState;
        }

        e.Handled = true;
    }

    private void PoliciesDataGrid_OnMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_settings is null || PoliciesDataGrid.SelectedItem is not PolicyListItem policy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(policy.PolicyId))
        {
            return;
        }

        var baseUrl = _settings.BackendBaseUrl.TrimEnd('/');
        var presentationUrl = $"{baseUrl}/policies/{Uri.EscapeDataString(policy.PolicyId)}/presentation";
        var subtitle = $"{policy.RootDomainName} ({policy.RootDomainCode})";
        var window = new PolicyPresentationWindow(policy.PolicyTitle, subtitle, presentationUrl)
        {
            Owner = Window.GetWindow(this),
        };
        window.ShowDialog();
    }

    private async void PoliciesDataGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingPolicySelection || PoliciesDataGrid.SelectedItem is not PolicyListItem policy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(policy.PolicyId))
        {
            return;
        }

        try
        {
            _isLoadingPolicySelection = true;
            SetBusyState(true, $"Loading {policy.PolicyTitle} ({policy.VersionText})...");
            var payload = await DraftTabContent.LoadPolicyByIdAsync(policy.PolicyId);
            if (payload is null)
            {
                return;
            }

            ApplyLoadedPolicyToControls(payload);
            SetWorkspaceStatus($"Loaded {policy.PolicyTitle} ({policy.VersionText}) for editing.");
        }
        catch (Exception ex)
        {
            SetWorkspaceStatus($"Policy load failed: {ex.Message}");
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Policy Workspace", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isLoadingPolicySelection = false;
            SetBusyState(false);
        }
    }

    private async void DeleteSelectedPolicyButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_httpClient is null || PoliciesDataGrid.SelectedItem is not PolicyListItem policy)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            Window.GetWindow(this),
            $"Delete saved policy version {policy.VersionText} for \"{policy.PolicyTitle}\"?",
            "Delete Policy",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            SetBusyState(true, $"Deleting {policy.PolicyTitle} ({policy.VersionText})...");
            var response = await _httpClient.DeleteAsync($"/policies/{Uri.EscapeDataString(policy.PolicyId)}");
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
            }

            await LoadPoliciesAsync(force: true);
            SetWorkspaceStatus($"Deleted {policy.PolicyTitle} ({policy.VersionText}).");
        }
        catch (Exception ex)
        {
            SetWorkspaceStatus($"Policy delete failed: {ex.Message}");
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Delete Policy", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private async Task LoadPoliciesAsync(bool force)
    {
        if (_httpClient is null)
        {
            return;
        }

        if (_selectedDomain is null || string.IsNullOrWhiteSpace(_selectedDomain.DomainCode))
        {
            Policies.Clear();
            UpdateSummary();
            SetWorkspaceStatus("Select a domain to load policies.");
            return;
        }

        try
        {
            SetBusyState(true, $"Loading policies for {_selectedDomain.DisplayName}...");
            var policies = await _httpClient.GetFromJsonAsync<List<PolicyListItem>>("/policies") ?? [];
            var filteredPolicies = policies
                .Where(item => string.Equals(item.RootDomainCode, _selectedDomain.DomainCode, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.UpdatedAtUtc)
                .ThenBy(item => item.PolicyTitle, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Policies.Clear();
            foreach (var policy in filteredPolicies)
            {
                Policies.Add(policy);
            }

            UpdateSummary();
            SetWorkspaceStatus(Policies.Count == 0
                ? $"No saved policies found for {_selectedDomain.DisplayName}."
                : $"Loaded {Policies.Count} saved policies for {_selectedDomain.DisplayName}.");
        }
        catch (Exception ex)
        {
            SetWorkspaceStatus($"Policy workspace load failed: {ex.Message}");
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Policy Workspace", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private async Task LoadControlsAsync()
    {
        if (_httpClient is null)
        {
            return;
        }

        foreach (var item in SelectableControls)
        {
            item.PropertyChanged -= SelectableControl_OnPropertyChanged;
        }

        SelectableControls.Clear();

        if (_selectedDomain is null || string.IsNullOrWhiteSpace(_selectedDomain.DomainCode))
        {
            DraftTabContent.SetIncludedControlCodes([]);
            DraftTabContent.SetControlGroupings([]);
            UpdateSummary();
            return;
        }

        try
        {
            var controls = await _httpClient.GetFromJsonAsync<List<ControlListItem>>(
                $"/controls?branchRootDomainCode={Uri.EscapeDataString(_selectedDomain.DomainCode)}") ?? [];

            foreach (var control in controls
                         .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.ControlCode, StringComparer.OrdinalIgnoreCase))
            {
                var selectable = new SelectableControlItem
                {
                    ControlId = control.ControlId,
                    ControlCode = control.ControlCode,
                    DisplayName = control.DisplayName,
                    DomainCode = control.DomainCode,
                    DomainDisplayName = control.DomainDisplayName,
                    ControlTypeCode = control.ControlTypeCode,
                    ControlTypeName = control.ControlTypeName,
                    IsIncluded = true,
                };
                selectable.PropertyChanged += SelectableControl_OnPropertyChanged;
                SelectableControls.Add(selectable);
            }

            PushSelectedControlsToDraft();
            await ApplyControlGroupingAsync();
            UpdateSummary();
        }
        catch (Exception ex)
        {
            SetWorkspaceStatus($"Control load failed: {ex.Message}");
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Policy Workspace", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateSummary()
    {
        if (_selectedDomain is null)
        {
            WorkspaceSummaryTextBlock.Text = "Select a domain to work with policies.";
            PolicyListSummaryTextBlock.Text = "No domain selected.";
            ControlSelectionSummaryTextBlock.Text = "Select a domain to choose controls.";
            return;
        }

        WorkspaceSummaryTextBlock.Text = $"Selected domain: {_selectedDomain.DisplayName} ({_selectedDomain.DomainCode})";
        PolicyListSummaryTextBlock.Text = Policies.Count == 0
            ? $"No saved policies yet for {_selectedDomain.DisplayName}."
            : $"{Policies.Count} saved policies for {_selectedDomain.DisplayName}.";
        var selectedControlCount = SelectableControls.Count(item => item.IsIncluded);
        ControlSelectionSummaryTextBlock.Text = SelectableControls.Count == 0
            ? $"No controls found for {_selectedDomain.DisplayName}."
            : $"{selectedControlCount} of {SelectableControls.Count} controls selected for this policy.";
    }

    private void ApplyLoadedPolicyToControls(LoadedPolicyDraftResponse payload)
    {
        var includedCodes = new HashSet<string>(
            payload.Controls.Select(item => item.ControlCode),
            StringComparer.OrdinalIgnoreCase);
        var groupLabelByCode = payload.Controls
            .Where(item => !string.IsNullOrWhiteSpace(item.ControlCode))
            .ToDictionary(
                item => item.ControlCode,
                item => item.GroupLabel ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        _isSyncingControlSelection = true;
        try
        {
            foreach (var item in SelectableControls)
            {
                item.IsIncluded = includedCodes.Contains(item.ControlCode);
                item.GroupLabel = groupLabelByCode.TryGetValue(item.ControlCode, out var label)
                    ? label
                    : string.Empty;
            }
        }
        finally
        {
            _isSyncingControlSelection = false;
        }

        _currentOrderedControlGroupings = payload.Controls
            .OrderBy(item => item.GroupDisplayOrder)
            .ThenBy(item => item.ControlDisplayOrder)
            .ThenBy(item => item.ControlName, StringComparer.OrdinalIgnoreCase)
            .GroupBy(
                item => string.IsNullOrWhiteSpace(item.GroupLabel) ? "Ungrouped Controls" : item.GroupLabel,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => new PolicyDraftControlGroupingItem
            {
                GroupLabel = group.Key,
                ControlCodes = group
                    .Select(item => item.ControlCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .ToList();

        ReorderSelectableControlsByCurrentGrouping();

        using (SelectableControlsView.DeferRefresh())
        {
            SelectableControlsView.GroupDescriptions.Clear();
            SelectableControlsView.GroupDescriptions.Add(
                new PropertyGroupDescription(nameof(SelectableControlItem.GroupLabel)));
        }

        PushSelectedControlsToDraft();
        UpdateSummary();
    }

    private void SetBusyState(bool isBusy, string? statusText = null)
    {
        DraftPolicyButton.IsEnabled = !isBusy;
        SavePolicyButton.IsEnabled = !isBusy;
        ClearPolicyTestDataButton.IsEnabled = !isBusy;
        ControlGroupingComboBox.IsEnabled = !isBusy;
        if (!string.IsNullOrWhiteSpace(statusText))
        {
            SetWorkspaceStatus(statusText);
        }
    }

    private static DomainItem? ResolveDomain(DomainItem? domain)
    {
        if (domain is null)
        {
            return null;
        }

        if (!domain.IsGroup)
        {
            return domain.SourceDomain ?? domain;
        }

        return null;
    }

    private void SelectableControl_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(SelectableControlItem.IsIncluded), StringComparison.Ordinal))
        {
            return;
        }

        if (_isSyncingControlSelection)
        {
            return;
        }

        PushSelectedControlsToDraft();
        UpdateSummary();
    }

    private void PushSelectedControlsToDraft()
    {
        var includedControls = SelectableControls
            .Where(item => item.IsIncluded)
            .ToList();

        DraftTabContent.SetIncludedControlCodes(includedControls.Select(item => item.ControlCode));
        var includedCodes = new HashSet<string>(
            includedControls.Select(item => item.ControlCode),
            StringComparer.OrdinalIgnoreCase);

        var orderedGroupings = new List<PolicyDraftControlGroupingItem>();
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var grouping in _currentOrderedControlGroupings)
        {
            var groupCodes = (grouping.ControlCodes ?? [])
                .Where(code => includedCodes.Contains(code))
                .Where(code => seenCodes.Add(code))
                .ToList();
            if (groupCodes.Count == 0)
            {
                continue;
            }

            orderedGroupings.Add(new PolicyDraftControlGroupingItem
            {
                GroupLabel = string.IsNullOrWhiteSpace(grouping.GroupLabel) ? "Ungrouped Controls" : grouping.GroupLabel,
                ControlCodes = groupCodes,
            });
        }

        var leftovers = includedControls
            .Where(item => seenCodes.Add(item.ControlCode))
            .GroupBy(
                item => string.IsNullOrWhiteSpace(item.GroupLabel) ? "Ungrouped Controls" : item.GroupLabel,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => new PolicyDraftControlGroupingItem
            {
                GroupLabel = group.Key,
                ControlCodes = group
                    .Select(item => item.ControlCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            });

        orderedGroupings.AddRange(leftovers);
        DraftTabContent.SetControlGroupings(orderedGroupings);
    }

    private void SetAllControlsIncluded(bool isIncluded)
    {
        _isSyncingControlSelection = true;
        try
        {
            foreach (var item in SelectableControls)
            {
                item.IsIncluded = isIncluded;
            }
        }
        finally
        {
            _isSyncingControlSelection = false;
        }

        PushSelectedControlsToDraft();
        UpdateSummary();
    }

    private void DraftTabContent_OnIncludedControlCodesChanged(IReadOnlyList<string> controlCodes)
    {
        var selectedCodes = new HashSet<string>(
            controlCodes.Where(code => !string.IsNullOrWhiteSpace(code)),
            StringComparer.OrdinalIgnoreCase);

        if (SelectableControls.Count == 0)
        {
            return;
        }

        _isSyncingControlSelection = true;
        try
        {
            if (selectedCodes.Count > 0)
            {
                foreach (var item in SelectableControls)
                {
                    item.IsIncluded = selectedCodes.Contains(item.ControlCode);
                }
            }
        }
        finally
        {
            _isSyncingControlSelection = false;
        }

        PushSelectedControlsToDraft();
        UpdateSummary();
    }

    private async Task ApplyControlGroupingAsync()
    {
        if (string.Equals(_selectedGroupingModeCode, "ai", StringComparison.OrdinalIgnoreCase))
        {
            await ApplyAiGroupingAsync();
            return;
        }

        foreach (var item in SelectableControls)
        {
            item.GroupLabel = ResolveGroupLabel(item, _selectedGroupingModeCode);
        }

        _currentOrderedControlGroupings = SelectableControls
            .GroupBy(
                item => string.IsNullOrWhiteSpace(item.GroupLabel) ? "Ungrouped Controls" : item.GroupLabel,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => new PolicyDraftControlGroupingItem
            {
                GroupLabel = group.Key,
                ControlCodes = group
                    .Select(item => item.ControlCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .ToList();

        ReorderSelectableControlsByCurrentGrouping();

        using (SelectableControlsView.DeferRefresh())
        {
            SelectableControlsView.GroupDescriptions.Clear();
            if (!string.Equals(_selectedGroupingModeCode, "none", StringComparison.OrdinalIgnoreCase))
            {
                SelectableControlsView.GroupDescriptions.Add(
                    new PropertyGroupDescription(nameof(SelectableControlItem.GroupLabel)));
            }
        }

        PushSelectedControlsToDraft();
    }

    private async Task ApplyAiGroupingAsync()
    {
        if (_httpClient is null || _selectedDomain is null || _isApplyingAiGrouping)
        {
            return;
        }

        try
        {
            _isApplyingAiGrouping = true;
            SetWorkspaceStatus($"Applying AI grouping for {_selectedDomain.DisplayName}", "...");
            ControlGroupingComboBox.IsEnabled = false;
            StartGroupingActivity($"Applying AI grouping for {_selectedDomain.DisplayName}");

            var response = await _httpClient.PostAsJsonAsync(
                "/controls/grouping/ai",
                new
                {
                    domainCode = _selectedDomain.DomainCode,
                    model = ResolveModelName(),
                    controlCodes = SelectableControls.Select(item => item.ControlCode).ToList(),
                });
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<AiControlGroupingResponse>();
            var assignments = payload?.Assignments ?? [];
            var groups = payload?.Groups ?? [];
            var labelsByCode = assignments
                .Where(item => !string.IsNullOrWhiteSpace(item.ControlCode))
                .GroupBy(item => item.ControlCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().GroupLabel,
                    StringComparer.OrdinalIgnoreCase);

            foreach (var item in SelectableControls)
            {
                item.GroupLabel = labelsByCode.TryGetValue(item.ControlCode, out var label) && !string.IsNullOrWhiteSpace(label)
                    ? label
                    : BuildSmartGroupLabel(item);
            }

            _currentOrderedControlGroupings = groups
                .Where(group => group is not null)
                .Select(group => new PolicyDraftControlGroupingItem
                {
                    GroupLabel = string.IsNullOrWhiteSpace(group.GroupLabel) ? "Other Controls" : group.GroupLabel,
                    ControlCodes = (group.ControlCodes ?? [])
                        .Where(code => !string.IsNullOrWhiteSpace(code))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .Where(group => group.ControlCodes.Count > 0)
                .ToList();

            ReorderSelectableControlsByCurrentGrouping();

            using (SelectableControlsView.DeferRefresh())
            {
                SelectableControlsView.GroupDescriptions.Clear();
                SelectableControlsView.GroupDescriptions.Add(
                    new PropertyGroupDescription(nameof(SelectableControlItem.GroupLabel)));
            }

            SetWorkspaceStatus($"Applied AI grouping to {SelectableControls.Count} controls.");
            PushSelectedControlsToDraft();
        }
        catch (Exception ex)
        {
            foreach (var item in SelectableControls)
            {
                item.GroupLabel = BuildSmartGroupLabel(item);
            }

            _currentOrderedControlGroupings = SelectableControls
                .GroupBy(item => item.GroupLabel, StringComparer.OrdinalIgnoreCase)
                .Select(group => new PolicyDraftControlGroupingItem
                {
                    GroupLabel = group.Key,
                    ControlCodes = group
                        .Select(item => item.ControlCode)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .ToList();

            ReorderSelectableControlsByCurrentGrouping();

            using (SelectableControlsView.DeferRefresh())
            {
                SelectableControlsView.GroupDescriptions.Clear();
                SelectableControlsView.GroupDescriptions.Add(
                    new PropertyGroupDescription(nameof(SelectableControlItem.GroupLabel)));
            }

            SetWorkspaceStatus($"AI grouping failed, using local smart grouping: {ex.Message}");
            PushSelectedControlsToDraft();
        }
        finally
        {
            _isApplyingAiGrouping = false;
            StopGroupingActivity();
            ControlGroupingComboBox.IsEnabled = true;
        }
    }

    private static string ResolveGroupLabel(SelectableControlItem item, string groupingModeCode)
    {
        return groupingModeCode switch
        {
            "domain" => item.DomainDisplayName,
            "type" => item.ControlTypeName,
            "smart" => BuildSmartGroupLabel(item),
            _ => string.Empty,
        };
    }

    private void ReorderSelectableControlsByCurrentGrouping()
    {
        if (SelectableControls.Count <= 1)
        {
            return;
        }

        var controlByCode = SelectableControls
            .Where(item => !string.IsNullOrWhiteSpace(item.ControlCode))
            .GroupBy(item => item.ControlCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var orderedItems = new List<SelectableControlItem>();
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var grouping in _currentOrderedControlGroupings)
        {
            foreach (var code in grouping.ControlCodes ?? [])
            {
                if (string.IsNullOrWhiteSpace(code) || !seenCodes.Add(code))
                {
                    continue;
                }

                if (controlByCode.TryGetValue(code, out var item))
                {
                    orderedItems.Add(item);
                }
            }
        }

        orderedItems.AddRange(
            SelectableControls
                .Where(item => string.IsNullOrWhiteSpace(item.ControlCode) || seenCodes.Add(item.ControlCode)));

        if (orderedItems.Count != SelectableControls.Count)
        {
            return;
        }

        SelectableControls.Clear();
        foreach (var item in orderedItems)
        {
            SelectableControls.Add(item);
        }
    }

    private static string BuildSmartGroupLabel(SelectableControlItem item)
    {
        var corpus = string.Join(
                " ",
                item.DisplayName,
                item.ControlTypeName,
                item.DomainDisplayName,
                item.ControlCode)
            .ToLowerInvariant();

        if (corpus.Contains("publish") || corpus.Contains("communication") || corpus.Contains("report"))
        {
            return "Communication and Publication";
        }

        if (corpus.Contains("mandate") || corpus.Contains("directive") || corpus.Contains("approve") || corpus.Contains("authorization"))
        {
            return "Direction and Approval";
        }

        if (corpus.Contains("plan") || corpus.Contains("strategy") || corpus.Contains("objective") || corpus.Contains("roadmap"))
        {
            return "Planning and Strategy";
        }

        if (corpus.Contains("measure") || corpus.Contains("metric") || corpus.Contains("performance") || corpus.Contains("monitor") || corpus.Contains("review"))
        {
            return "Monitoring and Measurement";
        }

        if (corpus.Contains("risk") || corpus.Contains("audit") || corpus.Contains("compliance") || corpus.Contains("issue"))
        {
            return "Risk and Assurance";
        }

        return string.IsNullOrWhiteSpace(item.ControlTypeName)
            ? "Other Controls"
            : item.ControlTypeName;
    }

    private string ResolveModelName()
    {
        if (_settings is null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(_settings.LastSelectedModel)
            ? string.Empty
            : _settings.LastSelectedModel.Trim();
    }
}
