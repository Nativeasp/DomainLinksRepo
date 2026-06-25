using System.IO;
using System.Net;
using System.Text;
using System.Windows;
using System.ComponentModel;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace DomainLinksDesktop;

public partial class OcrViewerWindow : Window
{
    private readonly DeepSeekOcrService _ocrService;
    private readonly System.Windows.Media.Brush _defaultStatusBrush =
        new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#23435A"));
    private const string PreviewHostName = "ocrviewer.local";
    private string? _selectedFilePath;
    private bool _isBusy;

    public OcrViewerWindow(string ollamaBaseUrl)
    {
        InitializeComponent();
        var settings = DomainLinksDesktopSettings.Load();
        Width = settings.OcrViewerWindowWidth;
        Height = settings.OcrViewerWindowHeight;
        if (!double.IsNaN(settings.OcrViewerWindowLeft))
        {
            Left = settings.OcrViewerWindowLeft;
        }
        if (!double.IsNaN(settings.OcrViewerWindowTop))
        {
            Top = settings.OcrViewerWindowTop;
        }
        UiScaleHelper.ApplyWindowScale(this, UiScaleHelper.Clamp(settings.AppUiScale));
        if (settings.OcrViewerPreviewPaneWidth > 0)
        {
            PreviewColumn.Width = new GridLength(settings.OcrViewerPreviewPaneWidth);
        }
        _ocrService = new DeepSeekOcrService(ollamaBaseUrl);
        Loaded += OcrViewerWindow_OnLoaded;
        Closing += OcrViewerWindow_OnClosing;
        UpdateActionState();
    }

    private async void OcrViewerWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        await PreviewWebView.EnsureCoreWebView2Async();
        UiScaleHelper.ApplyWebViewScale(PreviewWebView, UiScaleHelper.Clamp(DomainLinksDesktopSettings.Load().AppUiScale));
        PreviewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            PreviewHostName,
            AppContext.BaseDirectory,
            CoreWebView2HostResourceAccessKind.DenyCors);
        ShowEmptyPreview();
    }

    private void OpenFileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open OCR Source",
            Filter = "Supported files|*.pdf;*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.webp|All files|*.*",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _selectedFilePath = dialog.FileName;
        SelectedFileTextBlock.Text = _selectedFilePath;
        ExtractedTextTextBox.Clear();
        EngineTextBlock.Text = $"Engine: {DeepSeekOcrService.ModelName}";
        SetStatus("File loaded. Review the preview, then run OCR.");
        ShowPreview(_selectedFilePath);
        UpdateActionState();
    }

    private async void RunOcrButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RunOcrAsync();
    }

    private async void RetryButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RunOcrAsync();
    }

    private async Task RunOcrAsync()
    {
        if (_isBusy || string.IsNullOrWhiteSpace(_selectedFilePath))
        {
            return;
        }

        try
        {
            _isBusy = true;
            UpdateActionState();
            SetStatus("Running DeepSeek OCR...");
            var result = await _ocrService.ExtractTextAsync(_selectedFilePath);
            EngineTextBlock.Text = $"Engine: {result.EngineName}";

            if (!result.Success)
            {
                ExtractedTextTextBox.Text = string.Empty;
                SetStatus(result.ErrorMessage, isError: true);
                return;
            }

            ExtractedTextTextBox.Text = result.Text;
            SetStatus(string.IsNullOrWhiteSpace(result.StatusMessage)
                ? "OCR complete. You can copy or save the extracted text."
                : result.StatusMessage);
        }
        catch (Exception ex)
        {
            ExtractedTextTextBox.Text = string.Empty;
            SetStatus($"OCR failed: {ex.Message}", isError: true);
        }
        finally
        {
            _isBusy = false;
            UpdateActionState();
        }
    }

    private void CopyTextButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ExtractedTextTextBox.Text))
        {
            SetStatus("There is no OCR text to copy.", isError: true);
            return;
        }

        Clipboard.SetText(ExtractedTextTextBox.Text);
        SetStatus("OCR text copied to the clipboard.");
    }

    private void SaveTextButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ExtractedTextTextBox.Text))
        {
            SetStatus("There is no OCR text to save.", isError: true);
            return;
        }

        var suggestedName = string.IsNullOrWhiteSpace(_selectedFilePath)
            ? "ocr-output"
            : Path.GetFileNameWithoutExtension(_selectedFilePath);
        if (string.IsNullOrWhiteSpace(suggestedName))
        {
            suggestedName = "ocr-output";
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save OCR Text",
            FileName = $"{suggestedName}-ocr",
            DefaultExt = ".txt",
            Filter = "Text File|*.txt|Markdown File|*.md",
        };

        if (dialog.ShowDialog(this) != true)
        {
            SetStatus("Save cancelled.");
            return;
        }

        File.WriteAllText(dialog.FileName, ExtractedTextTextBox.Text, Encoding.UTF8);
        SetStatus($"Saved OCR text to {dialog.FileName}.");
    }

    private void CopyStatusButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(StatusTextBox.Text))
        {
            return;
        }

        Clipboard.SetText(StatusTextBox.Text);
        SetStatus("Status note copied to the clipboard.");
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OcrViewerWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        var saved = DomainLinksDesktopSettings.Load() with
        {
            OcrViewerWindowWidth = Width,
            OcrViewerWindowHeight = Height,
            OcrViewerWindowLeft = Left,
            OcrViewerWindowTop = Top,
            OcrViewerPreviewPaneWidth = PreviewColumn.ActualWidth,
        };
        saved.Save();
    }

    private void UpdateActionState()
    {
        var hasFile = !string.IsNullOrWhiteSpace(_selectedFilePath);
        var hasText = !string.IsNullOrWhiteSpace(ExtractedTextTextBox.Text);

        OpenFileButton.IsEnabled = !_isBusy;
        RunOcrButton.IsEnabled = !_isBusy && hasFile;
        RetryButton.IsEnabled = !_isBusy && hasFile;
        CopyTextButton.IsEnabled = !_isBusy && hasText;
        SaveTextButton.IsEnabled = !_isBusy && hasText;
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusTextBox.Text = message;
        StatusTextBox.Foreground = isError ? System.Windows.Media.Brushes.Firebrick : _defaultStatusBrush;
    }

    private void ShowEmptyPreview()
    {
        var html = """
                   <!doctype html>
                   <html>
                   <head>
                     <meta charset="utf-8">
                     <style>
                       body {
                         margin: 0;
                         min-height: 100vh;
                         display: grid;
                         place-items: center;
                         background: linear-gradient(160deg, #f8f3ea, #edf3f6);
                         color: #18344A;
                         font-family: Segoe UI, Arial, sans-serif;
                       }
                       .card {
                         padding: 24px 28px;
                         border-radius: 16px;
                         background: rgba(255,255,255,0.82);
                         box-shadow: 0 12px 32px rgba(24,52,74,0.10);
                         border: 1px solid rgba(24,52,74,0.10);
                         max-width: 440px;
                         text-align: center;
                       }
                     </style>
                   </head>
                   <body>
                     <div class="card">
                       <h2>OCR preview is ready</h2>
                       <p>Open a local PDF or image to inspect it here, then run DeepSeek OCR in this isolated tool.</p>
                     </div>
                   </body>
                   </html>
                   """;
        PreviewWebView.NavigateToString(html);
    }

    private void ShowPreview(string filePath)
    {
        try
        {
            var fileName = WebUtility.HtmlEncode(Path.GetFileName(filePath));
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var folderPath = Path.GetDirectoryName(filePath);
            if (PreviewWebView.CoreWebView2 is null)
            {
                throw new InvalidOperationException("Preview browser is not ready yet.");
            }

            if (extension == ".pdf")
            {
                PreviewWebView.Source = new Uri(filePath);
                return;
            }

            if (string.IsNullOrWhiteSpace(folderPath))
            {
                throw new InvalidOperationException("Preview folder could not be resolved.");
            }

            PreviewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                PreviewHostName,
                folderPath,
                CoreWebView2HostResourceAccessKind.DenyCors);
            var safeFileName = Uri.EscapeDataString(Path.GetFileName(filePath));
            var imageSource = $"https://{PreviewHostName}/{safeFileName}";

            var html = $@"
                        <!doctype html>
                        <html>
                        <head>
                          <meta charset=""utf-8"">
                          <style>
                            html, body {{
                              margin: 0;
                              height: 100%;
                              background: #efe9dd;
                            }}
                            body {{
                              display: grid;
                              place-items: center;
                            }}
                            img {{
                              width: 100%;
                              height: 100%;
                              object-fit: contain;
                              background: white;
                            }}
                          </style>
                        </head>
                        <body>
                          <img src=""{imageSource}"" alt=""{fileName}"" />
                        </body>
                        </html>";

            PreviewWebView.NavigateToString(html);
        }
        catch (Exception ex)
        {
            ShowEmptyPreview();
            SetStatus($"Preview could not be loaded: {ex.Message}", isError: true);
        }
    }
}
