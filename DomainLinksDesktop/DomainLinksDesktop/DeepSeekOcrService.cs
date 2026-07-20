using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DomainLinksDesktop;

internal sealed class DeepSeekOcrService
{
    private const string PromptText = "Extract the text in the image.";
    private const int PdfMaxDimension = 1800;

    private readonly HttpClient _httpClient;
    private readonly string _modelName;

    public DeepSeekOcrService(string ollamaBaseUrl, string modelName)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(ollamaBaseUrl),
            Timeout = TimeSpan.FromMinutes(4),
        };
        _modelName = string.IsNullOrWhiteSpace(modelName)
            ? DomainLinksDesktopSettings.DefaultOcrModel
            : modelName.Trim();
    }

    public string ModelName => _modelName;

    public string DisplayName => string.IsNullOrWhiteSpace(_modelName) ? "OCR" : _modelName;

    public async Task<OcrViewerResult> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return new OcrViewerResult(false, string.Empty, "Selected file was not found.", string.Empty);
        }

        if (!await IsModelAvailableAsync(cancellationToken))
        {
            return new OcrViewerResult(
                false,
                string.Empty,
                $"Ollama model '{_modelName}' is not available at {_httpClient.BaseAddress}.",
                _modelName);
        }

        var extension = Path.GetExtension(filePath);
        return string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase)
            ? await ExtractPdfTextAsync(filePath, cancellationToken)
            : await ExtractImageTextAsync(filePath, cancellationToken);
    }

    private async Task<bool> IsModelAvailableAsync(CancellationToken cancellationToken)
    {
        var payload = await _httpClient.GetFromJsonAsync<OllamaTagsPayload>("/api/tags", cancellationToken);
        return payload?.Models?.Any(model =>
                   string.Equals(model.Name, _modelName, StringComparison.OrdinalIgnoreCase))
               == true;
    }

    private async Task<OcrViewerResult> ExtractImageTextAsync(string filePath, CancellationToken cancellationToken)
    {
        var imageBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var result = await GenerateSingleImageAsync(Convert.ToBase64String(imageBytes), cancellationToken);
        return result.Success
            ? result with { StatusMessage = $"OCR complete: 1 image processed with {result.EngineName}." }
            : result;
    }

    private async Task<OcrViewerResult> ExtractPdfTextAsync(string filePath, CancellationToken cancellationToken)
    {
        List<string> pageImages;
        try
        {
            pageImages = await RenderPdfPagesAsync(filePath, cancellationToken);
        }
        catch (Exception ex)
        {
            return new OcrViewerResult(false, string.Empty, ex.Message, ModelName);
        }

        var pageTexts = new List<string>();
        for (var index = 0; index < pageImages.Count; index++)
        {
            var pageResult = await GenerateSingleImageAsync(pageImages[index], cancellationToken);
            if (!pageResult.Success)
            {
                var errorMessage = pageImages.Count == 1
                    ? pageResult.ErrorMessage
                    : $"{DisplayName} failed on PDF page {index + 1}: {pageResult.ErrorMessage}";
                return new OcrViewerResult(false, string.Empty, errorMessage, pageResult.EngineName);
            }

            if (!string.IsNullOrWhiteSpace(pageResult.Text))
            {
                pageTexts.Add(pageResult.Text.Trim());
            }
        }

        if (pageTexts.Count == 0)
        {
            return new OcrViewerResult(false, string.Empty, $"{DisplayName} returned no readable text for any PDF page.", _modelName);
        }

        return new OcrViewerResult(
            true,
            string.Join(Environment.NewLine + Environment.NewLine, pageTexts),
            string.Empty,
            _modelName,
            $"OCR complete: {pageTexts.Count} PDF page{(pageTexts.Count == 1 ? string.Empty : "s")} processed with {_modelName}.");
    }

    private static async Task<List<string>> RenderPdfPagesAsync(string filePath, CancellationToken cancellationToken)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "RenderPdfPagesToBase64.ps1");
        if (!File.Exists(scriptPath))
        {
            throw new InvalidOperationException($"PDF render helper script was not found: {scriptPath}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-FilePath");
        startInfo.ArgumentList.Add(filePath);
        startInfo.ArgumentList.Add("-MaxDimension");
        startInfo.ArgumentList.Add(PdfMaxDimension.ToString());

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var standardOutput = (await standardOutputTask).Trim();
        var standardError = (await standardErrorTask).Trim();
        var payload = string.IsNullOrWhiteSpace(standardOutput)
            ? null
            : JsonSerializer.Deserialize<PdfRenderPayload>(standardOutput);
        if (payload?.Pages is null || payload.Pages.Count == 0)
        {
            if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(standardError))
            {
                throw new InvalidOperationException(standardError);
            }

            throw new InvalidOperationException("PDF renderer returned no page images.");
        }

        return payload.Pages;
    }

    private async Task<OcrViewerResult> GenerateSingleImageAsync(string base64Image, CancellationToken cancellationToken)
    {
        var request = new OllamaGenerateRequest
        {
            Model = _modelName,
            Prompt = PromptText,
            Stream = false,
            Images = [base64Image],
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("/api/generate", request, cancellationToken);
        }
        catch (Exception ex)
        {
            return new OcrViewerResult(
                false,
                string.Empty,
                $"{DisplayName} request failed: {ex.Message}",
                _modelName);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            return new OcrViewerResult(
                false,
                string.Empty,
                $"{DisplayName} returned HTTP {(int)response.StatusCode}: {errorBody}",
                _modelName);
        }

        var payload = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken: cancellationToken);
        var text = (payload?.Response ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new OcrViewerResult(false, string.Empty, $"{DisplayName} returned no readable text.", _modelName);
        }

        return new OcrViewerResult(true, text, string.Empty, payload?.Model ?? _modelName);
    }

    private sealed class OllamaTagsPayload
    {
        [JsonPropertyName("models")]
        public List<OllamaTagItem> Models { get; set; } = [];
    }

    private sealed class OllamaTagItem
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class OllamaGenerateRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("images")]
        public List<string> Images { get; set; } = [];
    }

    private sealed class OllamaGenerateResponse
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;
    }

    private sealed class PdfRenderPayload
    {
        [JsonPropertyName("pages")]
        public List<string> Pages { get; set; } = [];
    }
}

internal sealed record OcrViewerResult(bool Success, string Text, string ErrorMessage, string EngineName, string StatusMessage = "");
