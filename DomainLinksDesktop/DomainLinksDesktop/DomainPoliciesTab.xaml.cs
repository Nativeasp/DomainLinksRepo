using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;

namespace DomainLinksDesktop;

public partial class DomainPoliciesTab : UserControl
{
    private DomainLinksDesktopSettings? _settings;
    private HttpClient? _httpClient;
    private bool _isActive;
    private bool _hasLoaded;

    public ObservableCollection<PolicyListItem> Policies { get; } = [];

    public DomainPoliciesTab()
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

    public async Task ActivateAsync()
    {
        _isActive = true;
        await LoadPoliciesAsync(force: false);
    }

    public void Deactivate()
    {
        _isActive = false;
    }

    private async void RefreshPoliciesButton_OnClick(object sender, RoutedEventArgs e)
    {
        await LoadPoliciesAsync(force: true);
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

    private async Task LoadPoliciesAsync(bool force)
    {
        if (_httpClient is null)
        {
            return;
        }

        if (!force && _hasLoaded && _isActive)
        {
            UpdateSummary();
            return;
        }

        try
        {
            SetBusyState(true, "Loading policies...");
            var policies = await _httpClient.GetFromJsonAsync<List<PolicyListItem>>("/policies") ?? [];

            Policies.Clear();
            foreach (var policy in policies)
            {
                Policies.Add(policy);
            }

            _hasLoaded = true;
            UpdateSummary();
            PoliciesStatusTextBlock.Text = Policies.Count == 0
                ? "No saved policies found."
                : $"Loaded {Policies.Count} saved policies.";
        }
        catch (Exception ex)
        {
            PoliciesStatusTextBlock.Text = $"Policy load failed: {ex.Message}";
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Policies", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private void UpdateSummary()
    {
        PoliciesSummaryTextBlock.Text = Policies.Count == 0
            ? "All saved policies across all domains."
            : $"{Policies.Count} saved policies across all domains.";
    }

    private void SetBusyState(bool isBusy, string? statusText = null)
    {
        RefreshPoliciesButton.IsEnabled = !isBusy;
        if (!string.IsNullOrWhiteSpace(statusText))
        {
            PoliciesStatusTextBlock.Text = statusText;
        }
    }
}
