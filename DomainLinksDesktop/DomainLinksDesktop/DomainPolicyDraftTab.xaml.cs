using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DomainLinksDesktop;

public partial class DomainPolicyDraftTab : UserControl
{
    private const string DefaultTemplatePath = "Policy/Policy-Template-1.01.md";
    private const string DefaultPolicyVersion = "0.01";
    private DomainLinksDesktopSettings? _settings;
    private HttpClient? _httpClient;
    private DomainItem? _selectedDomain;
    private string? _loadedDomainCode;
    private bool _isActive;
    private List<string> _includedControlCodes = [];

    public ObservableCollection<PolicyDraftLineItem> ObjectiveItems { get; } = [];
    public ObservableCollection<PolicyDraftLineItem> PrincipleItems { get; } = [];
    public ObservableCollection<PolicyDraftLineItem> AccountabilityItems { get; } = [];
    public ObservableCollection<PolicyDraftLineItem> TransparencyItems { get; } = [];
    public ObservableCollection<PolicyDraftLineItem> StrategyItems { get; } = [];
    public ObservableCollection<PolicyDraftControlGroup> ControlGroups { get; } = [];
    public ObservableCollection<PolicyDraftLineItem> ConsequenceItems { get; } = [];

    public DomainPolicyDraftTab()
    {
        InitializeComponent();
        DataContext = this;
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
        var resolved = ResolveDomain(domain);
        var selectedDomainChanged = !string.Equals(
            _selectedDomain?.DomainCode,
            resolved?.DomainCode,
            StringComparison.OrdinalIgnoreCase);
        _selectedDomain = resolved;

        if (selectedDomainChanged)
        {
            _loadedDomainCode = null;
            ClearDraftCollections();
            PolicyModelTextBlock.Text = "Model used: --";
        }

        UpdateSummary();

        if (_isActive && selectedDomainChanged)
        {
            _ = LoadExistingDraftAsync(force: true);
        }
    }

    public async Task ActivateAsync(DomainItem? domain)
    {
        var wasActive = _isActive;
        _isActive = false;
        SetSelectedDomain(domain);
        _isActive = true;
        await LoadExistingDraftAsync(force: !wasActive);
    }

    public void Deactivate()
    {
        _isActive = false;
    }

    public event Action<IReadOnlyList<string>>? IncludedControlCodesChanged;

    public void SetIncludedControlCodes(IEnumerable<string>? controlCodes)
    {
        _includedControlCodes = (controlCodes ?? [])
            .Select(code => (code ?? string.Empty).Trim())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal async Task ExecuteDraftPolicyAsync()
    {
        await GenerateDraftAsync();
    }

    internal async Task ExecuteSaveDraftAsync()
    {
        await SaveDraftAsync();
    }

    internal async Task ExecuteClearPolicyTestDataAsync()
    {
        await ClearPolicyTestDataAsync();
    }

    private async Task GenerateDraftAsync()
    {
        if (_httpClient is null || _settings is null)
        {
            return;
        }

        if (_selectedDomain is null || string.IsNullOrWhiteSpace(_selectedDomain.DomainCode))
        {
            MessageBox.Show(Window.GetWindow(this), "Select a domain in the tree first.", "Policy Draft", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            SetBusyState(true, $"Drafting policy content for {_selectedDomain.DisplayName}...");
            var response = await _httpClient.PostAsJsonAsync(
                "/policy-drafts/content",
                new
                {
                    domainCode = _selectedDomain.DomainCode,
                    templatePath = DefaultTemplatePath,
                    model = ResolveModelName(),
                    includedControlCodes = _includedControlCodes,
                });
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(await ReadErrorAsync(response));
            }

            var payload = await response.Content.ReadFromJsonAsync<PolicyDraftContentResponse>();
            if (payload is null)
            {
                throw new InvalidOperationException("The backend returned an empty policy draft payload.");
            }

            PopulateDraft(payload);
            _loadedDomainCode = _selectedDomain.DomainCode;
            PolicyStatusTextBlock.Text = $"Drafted policy content for {payload.RootDomainName}.";
        }
        catch (Exception ex)
        {
            PolicyStatusTextBlock.Text = $"Policy draft failed: {ex.Message}";
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Policy Draft", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private void PopulateDraft(PolicyDraftContentResponse payload)
    {
        PolicyBranchSummaryTextBlock.Text = string.IsNullOrWhiteSpace(payload.RootBreadcrumb)
            ? $"{payload.RootDomainName} ({payload.RootDomainCode})"
            : $"{payload.RootDomainName} ({payload.RootDomainCode})  |  {payload.RootBreadcrumb}";
        PolicyModelTextBlock.Text = $"Model used: {payload.ModelName}";

        ReplaceLines(ObjectiveItems, payload.Objectives, "objective");
        ReplaceLines(PrincipleItems, payload.Principles, "principle");
        ReplaceLines(AccountabilityItems, payload.Accountability, "accountability");
        ReplaceLines(TransparencyItems, payload.Transparency, "transparency");
        ReplaceLines(StrategyItems, payload.Strategy, "strategy");
        ReplaceLines(ConsequenceItems, payload.Consequences, "consequence");

        ControlGroups.Clear();
        foreach (var control in payload.Controls)
        {
            var group = new PolicyDraftControlGroup
            {
                ControlCode = control.ControlCode,
                ControlName = control.ControlName,
                DetailLine = $"{control.DomainDisplayName} | {control.ControlTypeName} ({control.ControlTypeCode}) | {control.ControlCode}",
            };
            var displayOrder = 10;
            foreach (var statement in control.PolicyStatements)
            {
                group.Statements.Add(new PolicyDraftLineItem(statement, "control-policy", control.ControlCode)
                {
                    DisplayOrder = displayOrder
                });
                displayOrder += 10;
            }
            ControlGroups.Add(group);
        }

        PublishIncludedControlCodes(payload.Controls.Select(item => item.ControlCode));
    }

    private void PopulateSavedDraft(LoadedPolicyDraftResponse payload)
    {
        PolicyBranchSummaryTextBlock.Text = string.IsNullOrWhiteSpace(payload.RootBreadcrumb)
            ? $"{payload.RootDomainName} ({payload.RootDomainCode})"
            : $"{payload.RootDomainName} ({payload.RootDomainCode})  |  {payload.RootBreadcrumb}";
        PolicyModelTextBlock.Text = $"Model used: {payload.ModelName}";

        ReplaceSavedLines(ObjectiveItems, payload.Objectives, "objective");
        ReplaceSavedLines(PrincipleItems, payload.Principles, "principle");
        ReplaceSavedLines(AccountabilityItems, payload.Accountability, "accountability");
        ReplaceSavedLines(TransparencyItems, payload.Transparency, "transparency");
        ReplaceSavedLines(StrategyItems, payload.Strategy, "strategy");
        ReplaceSavedLines(ConsequenceItems, payload.Consequences, "consequence");

        ControlGroups.Clear();
        foreach (var control in payload.Controls)
        {
            var group = new PolicyDraftControlGroup
            {
                ControlCode = control.ControlCode,
                ControlName = control.ControlName,
                DetailLine = $"{control.DomainDisplayName} | {control.ControlTypeName} ({control.ControlTypeCode}) | {control.ControlCode}",
            };
            foreach (var statement in control.PolicyStatements.OrderBy(item => item.DisplayOrder))
            {
                var line = new PolicyDraftLineItem(statement.StatementText, "control-policy", control.ControlCode)
                {
                    DisplayOrder = statement.DisplayOrder,
                };
                line.ApplyReviewStatus(statement.ReviewStatus);
                group.Statements.Add(line);
            }
            ControlGroups.Add(group);
        }

        PublishIncludedControlCodes(payload.Controls.Select(item => item.ControlCode));
    }

    private static void ReplaceLines(ObservableCollection<PolicyDraftLineItem> target, IEnumerable<string> source, string sectionKey)
    {
        target.Clear();
        var displayOrder = 10;
        foreach (var text in source)
        {
            target.Add(new PolicyDraftLineItem(text, sectionKey)
            {
                DisplayOrder = displayOrder
            });
            displayOrder += 10;
        }
    }

    private static void ReplaceSavedLines(
        ObservableCollection<PolicyDraftLineItem> target,
        IEnumerable<PolicyDraftSavedStatementResponse> source,
        string sectionKey)
    {
        target.Clear();
        foreach (var item in source.OrderBy(entry => entry.DisplayOrder))
        {
            var line = new PolicyDraftLineItem(item.StatementText, sectionKey)
            {
                DisplayOrder = item.DisplayOrder,
            };
            line.ApplyReviewStatus(item.ReviewStatus);
            target.Add(line);
        }
    }

    private void ClearDraftCollections()
    {
        ObjectiveItems.Clear();
        PrincipleItems.Clear();
        AccountabilityItems.Clear();
        TransparencyItems.Clear();
        StrategyItems.Clear();
        ControlGroups.Clear();
        ConsequenceItems.Clear();
        if (_isActive)
        {
            PolicyStatusTextBlock.Text = "Policy drafting idle.";
        }
    }

    private void ResetDraftUiAfterCleanup(string statusText)
    {
        _loadedDomainCode = null;
        ObjectiveItems.Clear();
        PrincipleItems.Clear();
        AccountabilityItems.Clear();
        TransparencyItems.Clear();
        StrategyItems.Clear();
        ControlGroups.Clear();
        ConsequenceItems.Clear();
        PolicyModelTextBlock.Text = "Model used: --";
        PolicyStatusTextBlock.Text = statusText;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        if (_selectedDomain is null)
        {
            PolicyBranchSummaryTextBlock.Text = "Select a domain in the tree, then click Draft Policy.";
            return;
        }

        if (string.Equals(_loadedDomainCode, _selectedDomain.DomainCode, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        PolicyBranchSummaryTextBlock.Text = $"Selected domain: {_selectedDomain.DisplayName} ({_selectedDomain.DomainCode})";
    }

    private void SetBusyState(bool isBusy, string? statusText = null)
    {
        if (!string.IsNullOrWhiteSpace(statusText))
        {
            PolicyStatusTextBlock.Text = statusText;
        }
    }

    private async Task LoadExistingDraftAsync(bool force)
    {
        if (_httpClient is null || _selectedDomain is null || string.IsNullOrWhiteSpace(_selectedDomain.DomainCode))
        {
            return;
        }

        if (!force && string.Equals(_loadedDomainCode, _selectedDomain.DomainCode, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            SetBusyState(true, $"Checking for a saved policy for {_selectedDomain.DisplayName}...");
            var response = await _httpClient.GetAsync($"/policies/by-root-domain/{Uri.EscapeDataString(_selectedDomain.DomainCode)}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                ClearDraftCollections();
                _loadedDomainCode = null;
                PolicyModelTextBlock.Text = "Model used: --";
                PolicyStatusTextBlock.Text = $"No saved policy found for {_selectedDomain.DisplayName}.";
                UpdateSummary();
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(await ReadErrorAsync(response));
            }

            var payload = await response.Content.ReadFromJsonAsync<LoadedPolicyDraftResponse>();
            if (payload is null)
            {
                throw new InvalidOperationException("The backend returned an empty saved policy payload.");
            }

            PopulateSavedDraft(payload);
            _loadedDomainCode = _selectedDomain.DomainCode;
            PolicyStatusTextBlock.Text = $"Loaded saved policy draft {payload.PolicyCode} ({payload.VersionText}).";
        }
        catch (Exception ex)
        {
            PolicyStatusTextBlock.Text = $"Saved policy load failed: {ex.Message}";
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Load Saved Policy", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private async Task SaveDraftAsync()
    {
        if (_httpClient is null || _selectedDomain is null)
        {
            return;
        }

        if (!HasDraftContent())
        {
            MessageBox.Show(Window.GetWindow(this), "Draft the policy first, then save it.", "Policy Draft", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            SetBusyState(true, $"Saving policy draft for {_selectedDomain.DisplayName}...");
            var response = await _httpClient.PostAsJsonAsync(
                "/policies/save-draft",
                new
                {
                    rootDomainCode = _selectedDomain.DomainCode,
                    policyCode = BuildPolicyCode(),
                    policyTitle = $"{_selectedDomain.DisplayName} Policy",
                    versionText = DefaultPolicyVersion,
                    status = "Draft",
                    templatePath = DefaultTemplatePath,
                    sourceModelName = ResolveDisplayedModelName(),
                    objectives = ObjectiveItems.Select(ToPayload).ToList(),
                    principles = PrincipleItems.Select(ToPayload).ToList(),
                    accountability = AccountabilityItems.Select(ToPayload).ToList(),
                    transparency = TransparencyItems.Select(ToPayload).ToList(),
                    strategy = StrategyItems.Select(ToPayload).ToList(),
                    consequences = ConsequenceItems.Select(ToPayload).ToList(),
                    controlStatements = ControlGroups
                        .SelectMany(group => group.Statements.Select((line, index) => new
                        {
                            controlCode = group.ControlCode,
                            statementText = line.Text,
                            displayOrder = index * 10 + 10,
                            reviewStatus = NormalizeReviewStatus(line.StatusLabel),
                        }))
                        .ToList(),
                });
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(await ReadErrorAsync(response));
            }

            var payload = await response.Content.ReadFromJsonAsync<SavedPolicyDraftResponse>();
            if (payload is null)
            {
                throw new InvalidOperationException("The backend returned an empty save response.");
            }

            PolicyStatusTextBlock.Text = $"Saved draft {payload.PolicyCode} ({payload.VersionText}).";
            if (!string.IsNullOrWhiteSpace(payload.ModelName))
            {
                PolicyModelTextBlock.Text = $"Model used: {payload.ModelName}";
            }
        }
        catch (Exception ex)
        {
            PolicyStatusTextBlock.Text = $"Save failed: {ex.Message}";
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Save Policy Draft", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private bool HasDraftContent()
    {
        return ObjectiveItems.Count > 0
            || PrincipleItems.Count > 0
            || AccountabilityItems.Count > 0
            || TransparencyItems.Count > 0
            || StrategyItems.Count > 0
            || ConsequenceItems.Count > 0
            || ControlGroups.Any(group => group.Statements.Count > 0);
    }

    private async Task ClearPolicyTestDataAsync()
    {
        if (_httpClient is null)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            Window.GetWindow(this),
            "This will delete all rows from every policy-related table used for testing. Continue?",
            "Clear Policy Test Data",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            PolicyStatusTextBlock.Text = "Policy test cleanup cancelled.";
            return;
        }

        try
        {
            SetBusyState(true, "Clearing all policy test data...");
            var response = await _httpClient.PostAsync("/policies/testing/clear-all", content: null);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(await ReadErrorAsync(response));
            }

            var payload = await response.Content.ReadFromJsonAsync<PolicyCleanupResponse>();
            if (payload is null)
            {
                throw new InvalidOperationException("The backend returned an empty cleanup response.");
            }

            var nonZeroCounts = payload.Counts.Where(item => item.TotalRows != 0).ToList();
            if (nonZeroCounts.Count > 0)
            {
                var details = string.Join(", ", nonZeroCounts.Select(item => $"{item.TableName}={item.TotalRows}"));
                throw new InvalidOperationException($"Cleanup completed but some tables were not empty: {details}");
            }

            ResetDraftUiAfterCleanup("Cleared all policy test data.");
        }
        catch (Exception ex)
        {
            PolicyStatusTextBlock.Text = $"Cleanup failed: {ex.Message}";
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Clear Policy Test Data", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private object ToPayload(PolicyDraftLineItem line)
    {
        return new
        {
            statementText = line.Text,
            displayOrder = line.DisplayOrder,
            reviewStatus = NormalizeReviewStatus(line.StatusLabel),
        };
    }

    private string BuildPolicyCode()
    {
        if (_selectedDomain is null || string.IsNullOrWhiteSpace(_selectedDomain.DomainCode))
        {
            return "policy-draft";
        }

        return $"{_selectedDomain.DomainCode}-policy";
    }

    private string ResolveDisplayedModelName()
    {
        var text = PolicyModelTextBlock.Text ?? string.Empty;
        const string prefix = "Model used:";
        return text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? text[prefix.Length..].Trim()
            : ResolveModelName();
    }

    private static string NormalizeReviewStatus(string statusLabel)
    {
        return statusLabel switch
        {
            "Accepted" => "Accepted",
            "Rejected" => "Rejected",
            "Revised" => "Revised",
            _ => "Pending",
        };
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

    private async void RetryLineButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_httpClient is null || _selectedDomain is null)
        {
            return;
        }

        if (sender is not Button { Tag: PolicyDraftLineItem line })
        {
            return;
        }

        try
        {
            line.StatusLabel = "Retrying";
            line.StatusBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F4F1E8"));
            line.StatusForeground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8A6D2F"));

            var response = await _httpClient.PostAsJsonAsync(
                "/policy-drafts/redraft-line",
                new
                {
                    domainCode = _selectedDomain.DomainCode,
                    sectionKey = line.SectionKey,
                    currentText = line.Text,
                    controlCode = line.ControlCode,
                    templatePath = DefaultTemplatePath,
                    model = ResolveModelName(),
                    includedControlCodes = _includedControlCodes,
                });
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(await ReadErrorAsync(response));
            }

            var payload = await response.Content.ReadFromJsonAsync<PolicyDraftLineRetryResponse>();
            if (payload is null || string.IsNullOrWhiteSpace(payload.Text))
            {
                throw new InvalidOperationException("The backend returned an empty retry line.");
            }

            line.Text = payload.Text;
            line.MarkPending();
            PolicyModelTextBlock.Text = $"Model used: {payload.ModelName}";
            PolicyStatusTextBlock.Text = "Replaced one policy line.";
        }
        catch (Exception ex)
        {
            line.MarkPending();
            PolicyStatusTextBlock.Text = $"Retry failed: {ex.Message}";
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Retry Policy Line", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AcceptLineButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PolicyDraftLineItem line })
        {
            return;
        }

        line.MarkAccepted();
        PolicyStatusTextBlock.Text = "Marked line as accepted.";
    }

    private void RejectLineButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PolicyDraftLineItem line })
        {
            return;
        }

        line.MarkRejected();
        PolicyStatusTextBlock.Text = "Marked line as rejected.";
    }

    private static DomainItem? ResolveDomain(DomainItem? domain)
    {
        return domain?.SourceDomain ?? domain;
    }

    private void PublishIncludedControlCodes(IEnumerable<string> controlCodes)
    {
        var normalized = controlCodes
            .Select(code => (code ?? string.Empty).Trim())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _includedControlCodes = normalized;
        IncludedControlCodesChanged?.Invoke(normalized);
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(body)
            ? $"{(int)response.StatusCode} {response.ReasonPhrase}"
            : body;
    }
}

public sealed class PolicyDraftControlGroup : INotifyPropertyChanged
{
    private string _controlCode = string.Empty;
    private string _controlName = string.Empty;
    private string _detailLine = string.Empty;

    public string ControlCode
    {
        get => _controlCode;
        set => SetField(ref _controlCode, value);
    }

    public string ControlName
    {
        get => _controlName;
        set => SetField(ref _controlName, value);
    }

    public string DetailLine
    {
        get => _detailLine;
        set => SetField(ref _detailLine, value);
    }

    public ObservableCollection<PolicyDraftLineItem> Statements { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

public sealed class PolicyDraftLineItem : INotifyPropertyChanged
{
    private static readonly Brush PendingBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
    private static readonly Brush AcceptedBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EFF7EF"));
    private static readonly Brush RejectedBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FAEEEE"));
    private static readonly Brush PendingForegroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5B6770"));
    private static readonly Brush AcceptedForegroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2F6B39"));
    private static readonly Brush RejectedForegroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9B3A3A"));

    private string _text;
    private string _statusLabel = "Pending Review";
    private Brush _statusBackground = PendingBackgroundBrush;
    private Brush _statusForeground = PendingForegroundBrush;

    public PolicyDraftLineItem(string text, string sectionKey, string? controlCode = null)
    {
        _text = text;
        SectionKey = sectionKey;
        ControlCode = controlCode ?? string.Empty;
    }

    public int DisplayOrder { get; set; }

    public string Text
    {
        get => _text;
        set => SetField(ref _text, value);
    }

    public string SectionKey { get; }

    public string ControlCode { get; }

    public string StatusLabel
    {
        get => _statusLabel;
        set => SetField(ref _statusLabel, value);
    }

    public Brush StatusBackground
    {
        get => _statusBackground;
        set => SetField(ref _statusBackground, value);
    }

    public Brush StatusForeground
    {
        get => _statusForeground;
        set => SetField(ref _statusForeground, value);
    }

    public void MarkAccepted()
    {
        StatusLabel = "Accepted";
        StatusBackground = AcceptedBackgroundBrush;
        StatusForeground = AcceptedForegroundBrush;
    }

    public void MarkRejected()
    {
        StatusLabel = "Rejected";
        StatusBackground = RejectedBackgroundBrush;
        StatusForeground = RejectedForegroundBrush;
    }

    public void ApplyReviewStatus(string? reviewStatus)
    {
        var normalized = (reviewStatus ?? string.Empty).Trim();
        if (normalized.Equals("Accepted", StringComparison.OrdinalIgnoreCase))
        {
            MarkAccepted();
            return;
        }

        if (normalized.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
        {
            MarkRejected();
            return;
        }

        MarkPending();
    }

    public void MarkPending()
    {
        StatusLabel = "Pending Review";
        StatusBackground = PendingBackgroundBrush;
        StatusForeground = PendingForegroundBrush;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
