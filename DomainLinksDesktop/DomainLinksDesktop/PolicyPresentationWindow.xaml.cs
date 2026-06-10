using System.Windows;
using System.ComponentModel;

namespace DomainLinksDesktop;

public partial class PolicyPresentationWindow : Window
{
    private readonly string _presentationUrl;

    public PolicyPresentationWindow(string policyTitle, string policySubtitle, string presentationUrl)
    {
        InitializeComponent();
        var settings = DomainLinksDesktopSettings.Load();
        Width = settings.PolicyPresentationWindowWidth;
        Height = settings.PolicyPresentationWindowHeight;
        if (!double.IsNaN(settings.PolicyPresentationWindowLeft))
        {
            Left = settings.PolicyPresentationWindowLeft;
        }
        if (!double.IsNaN(settings.PolicyPresentationWindowTop))
        {
            Top = settings.PolicyPresentationWindowTop;
        }
        _presentationUrl = presentationUrl;
        TitleTextBlock.Text = string.IsNullOrWhiteSpace(policyTitle) ? "Policy Presentation" : policyTitle;
        SubtitleTextBlock.Text = string.IsNullOrWhiteSpace(policySubtitle) ? "Saved policy view" : policySubtitle;
        Loaded += PolicyPresentationWindow_OnLoaded;
        Closing += PolicyPresentationWindow_OnClosing;
    }

    private async void PolicyPresentationWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        await PolicyWebView.EnsureCoreWebView2Async();
        PolicyWebView.Source = new Uri(_presentationUrl);
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void PolicyPresentationWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        var saved = DomainLinksDesktopSettings.Load() with
        {
            PolicyPresentationWindowWidth = Width,
            PolicyPresentationWindowHeight = Height,
            PolicyPresentationWindowLeft = Left,
            PolicyPresentationWindowTop = Top,
        };
        saved.Save();
    }
}
