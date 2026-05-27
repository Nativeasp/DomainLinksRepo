using System.Windows;

namespace DomainLinksDesktop;

public partial class DocumentTextWindow : Window
{
    public DocumentTextWindow(string title, string documentName, string bodyText, bool isReadOnly)
    {
        InitializeComponent();
        Title = title;
        HeaderTextBlock.Text = title;
        DocumentNameTextBox.Text = documentName;
        BodyTextBox.Text = bodyText;
        DocumentNameTextBox.IsReadOnly = isReadOnly;
        BodyTextBox.IsReadOnly = isReadOnly;
        SaveButton.Visibility = isReadOnly ? Visibility.Collapsed : Visibility.Visible;
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
}
