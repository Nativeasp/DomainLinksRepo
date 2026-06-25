using System.Windows;
using System.ComponentModel;

namespace DomainLinksDesktop;

public partial class DocumentTextWindow : Window
{
    public DocumentTextWindow(string title, string documentName, string bodyText, bool isReadOnly)
    {
        InitializeComponent();
        var settings = DomainLinksDesktopSettings.Load();
        Width = settings.DocumentTextWindowWidth;
        Height = settings.DocumentTextWindowHeight;
        if (!double.IsNaN(settings.DocumentTextWindowLeft))
        {
            Left = settings.DocumentTextWindowLeft;
        }
        if (!double.IsNaN(settings.DocumentTextWindowTop))
        {
            Top = settings.DocumentTextWindowTop;
        }
        UiScaleHelper.ApplyWindowScale(this, UiScaleHelper.Clamp(settings.AppUiScale));
        Title = title;
        HeaderTextBlock.Text = title;
        DocumentNameTextBox.Text = documentName;
        BodyTextBox.Text = bodyText;
        DocumentNameTextBox.IsReadOnly = isReadOnly;
        BodyTextBox.IsReadOnly = isReadOnly;
        SaveButton.Visibility = isReadOnly ? Visibility.Collapsed : Visibility.Visible;
        Closing += DocumentTextWindow_OnClosing;
    }

    public string DocumentName => DocumentNameTextBox.Text.Trim();
    public string DocumentBody => BodyTextBox.Text;

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DocumentName))
        {
            MessageBox.Show(this, "Document name is required.", "Domain Store", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(DocumentBody))
        {
            MessageBox.Show(this, "Document text is required.", "Domain Store", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void DocumentTextWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        var saved = DomainLinksDesktopSettings.Load() with
        {
            DocumentTextWindowWidth = Width,
            DocumentTextWindowHeight = Height,
            DocumentTextWindowLeft = Left,
            DocumentTextWindowTop = Top,
        };
        saved.Save();
    }
}
