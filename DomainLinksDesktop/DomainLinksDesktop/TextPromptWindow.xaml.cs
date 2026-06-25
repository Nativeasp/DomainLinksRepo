using System.Windows;
using System.Windows.Input;
using System.ComponentModel;

namespace DomainLinksDesktop;

public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string prompt, string initialValue = "", string? hint = null)
    {
        InitializeComponent();
        var settings = DomainLinksDesktopSettings.Load();
        Width = settings.TextPromptWindowWidth;
        Height = settings.TextPromptWindowHeight;
        if (!double.IsNaN(settings.TextPromptWindowLeft))
        {
            Left = settings.TextPromptWindowLeft;
        }
        if (!double.IsNaN(settings.TextPromptWindowTop))
        {
            Top = settings.TextPromptWindowTop;
        }
        UiScaleHelper.ApplyWindowScale(this, UiScaleHelper.Clamp(settings.AppUiScale));
        Title = title;
        PromptTextBlock.Text = prompt;
        ValueTextBox.Text = initialValue;
        if (!string.IsNullOrWhiteSpace(hint))
        {
            HintTextBlock.Text = hint;
            HintTextBlock.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) =>
        {
            ValueTextBox.Focus();
            ValueTextBox.SelectAll();
        };
        Closing += TextPromptWindow_OnClosing;
    }

    public string ResultText { get; private set; } = string.Empty;

    private void OkButton_OnClick(object sender, RoutedEventArgs e)
    {
        var value = ValueTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            ValidationTextBlock.Text = "A value is required.";
            ValidationTextBlock.Visibility = Visibility.Visible;
            ValueTextBox.Focus();
            return;
        }

        ResultText = value;
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ValueTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OkButton_OnClick(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void TextPromptWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        var saved = DomainLinksDesktopSettings.Load() with
        {
            TextPromptWindowWidth = Width,
            TextPromptWindowHeight = ActualHeight,
            TextPromptWindowLeft = Left,
            TextPromptWindowTop = Top,
        };
        saved.Save();
    }
}
