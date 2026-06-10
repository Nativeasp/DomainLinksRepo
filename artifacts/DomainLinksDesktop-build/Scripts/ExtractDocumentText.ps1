param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Runtime.WindowsRuntime
[void][Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
[void][Windows.Storage.FileAccessMode, Windows.Storage, ContentType = WindowsRuntime]
[void][Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType = WindowsRuntime]
[void][Windows.Data.Pdf.PdfPageRenderOptions, Windows.Data.Pdf, ContentType = WindowsRuntime]
[void][Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
[void][Windows.Graphics.Imaging.BitmapTransform, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
[void][Windows.Graphics.Imaging.BitmapPixelFormat, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
[void][Windows.Graphics.Imaging.BitmapAlphaMode, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
[void][Windows.Graphics.Imaging.ExifOrientationMode, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
[void][Windows.Graphics.Imaging.ColorManagementMode, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
[void][Windows.Graphics.Imaging.SoftwareBitmap, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
[void][Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]
[void][Windows.Media.Ocr.OcrResult, Windows.Foundation, ContentType = WindowsRuntime]
[void][Windows.Foundation.IAsyncAction, Windows.Foundation, ContentType = WindowsRuntime]
[void][Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]
[void][Windows.Storage.Streams.IRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]

function Invoke-WinRtTask {
    param(
        [Parameter(Mandatory = $true)]
        $Operation,
        [Type]$ResultType
    )

    if ($null -eq $Operation) {
        throw "WinRT operation was null."
    }

    if ($null -eq $ResultType) {
        $method = [System.WindowsRuntimeSystemExtensions].GetMethods() |
            Where-Object {
                $_.Name -eq "AsTask" -and
                -not $_.IsGenericMethod -and
                $_.GetParameters().Count -eq 1 -and
                $_.GetParameters()[0].ParameterType.ToString() -eq "Windows.Foundation.IAsyncAction"
            } |
            Select-Object -First 1

        if ($null -eq $method) {
            throw "Unable to find WinRT IAsyncAction AsTask overload."
        }

        $task = $method.Invoke($null, @($Operation))
    }
    else {
        $method = [System.WindowsRuntimeSystemExtensions].GetMethods() |
            Where-Object {
                $_.Name -eq "AsTask" -and
                $_.IsGenericMethod -and
                $_.GetGenericArguments().Count -eq 1 -and
                $_.GetParameters().Count -eq 1
            } |
            Select-Object -First 1

        if ($null -eq $method) {
            throw "Unable to find WinRT AsTask overload."
        }

        $task = $method.MakeGenericMethod($ResultType).Invoke($null, @($Operation))
    }

    return $task.GetAwaiter().GetResult()
}

function Get-StorageFile {
    param([string]$Path)
    return Invoke-WinRtTask ([Windows.Storage.StorageFile]::GetFileFromPathAsync($Path)) ([Windows.Storage.StorageFile])
}

function Open-ReadStream {
    param($StorageFile)
    return Invoke-WinRtTask ($StorageFile.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
}

function Get-OcrEngine {
    $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
    if ($null -eq $engine) {
        throw "Windows OCR is not available on this machine."
    }

    return $engine
}

function New-BitmapTransform {
    param(
        [uint32]$Width,
        [uint32]$Height
    )

    $maxDimension = [double][Windows.Media.Ocr.OcrEngine]::MaxImageDimension
    $scale = [Math]::Min(1.0, [Math]::Min($maxDimension / $Width, $maxDimension / $Height))
    $transform = New-Object Windows.Graphics.Imaging.BitmapTransform
    $transform.ScaledWidth = [uint32][Math]::Max(1, [Math]::Round($Width * $scale))
    $transform.ScaledHeight = [uint32][Math]::Max(1, [Math]::Round($Height * $scale))
    return $transform
}

function Get-SoftwareBitmap {
    param($RandomAccessStream)

    $decoder = Invoke-WinRtTask ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($RandomAccessStream)) ([Windows.Graphics.Imaging.BitmapDecoder])
    $transform = New-BitmapTransform -Width $decoder.PixelWidth -Height $decoder.PixelHeight
    return Invoke-WinRtTask (
        $decoder.GetSoftwareBitmapAsync(
            [Windows.Graphics.Imaging.BitmapPixelFormat]::Bgra8,
            [Windows.Graphics.Imaging.BitmapAlphaMode]::Ignore,
            $transform,
            [Windows.Graphics.Imaging.ExifOrientationMode]::RespectExifOrientation,
            [Windows.Graphics.Imaging.ColorManagementMode]::ColorManageToSRgb
        )
    ) ([Windows.Graphics.Imaging.SoftwareBitmap])
}

function Get-ImageText {
    param($RandomAccessStream)

    $bitmap = Get-SoftwareBitmap -RandomAccessStream $RandomAccessStream
    $ocrResult = Invoke-WinRtTask ((Get-OcrEngine).RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
    return (($ocrResult.Lines | ForEach-Object { $_.Text.Trim() }) -join [Environment]::NewLine).Trim()
}

function Get-PdfText {
    param([string]$Path)

    $storageFile = Get-StorageFile -Path $Path
    $pdfDocument = Invoke-WinRtTask ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($storageFile)) ([Windows.Data.Pdf.PdfDocument])
    $pageTexts = New-Object System.Collections.Generic.List[string]

    for ($pageIndex = 0; $pageIndex -lt $pdfDocument.PageCount; $pageIndex++) {
        $page = $pdfDocument.GetPage([uint32]$pageIndex)
        $renderOptions = New-Object Windows.Data.Pdf.PdfPageRenderOptions
        $maxDimension = [double][Windows.Media.Ocr.OcrEngine]::MaxImageDimension
        $widthScale = $maxDimension / $page.Size.Width
        $heightScale = $maxDimension / $page.Size.Height
        $scale = [Math]::Min(1.0, [Math]::Min($widthScale, $heightScale))
        $renderOptions.DestinationWidth = [uint32][Math]::Max(1, [Math]::Round($page.Size.Width * $scale))
        $renderOptions.DestinationHeight = [uint32][Math]::Max(1, [Math]::Round($page.Size.Height * $scale))

        $stream = New-Object Windows.Storage.Streams.InMemoryRandomAccessStream
        Invoke-WinRtTask ($page.RenderToStreamAsync($stream, $renderOptions)) $null | Out-Null
        $stream.Seek(0)
        $pageText = Get-ImageText -RandomAccessStream $stream
        if (-not [string]::IsNullOrWhiteSpace($pageText)) {
            $pageTexts.Add($pageText)
        }
    }

    return ($pageTexts -join ([Environment]::NewLine + [Environment]::NewLine)).Trim()
}

if (-not (Test-Path -LiteralPath $FilePath)) {
    throw "File not found: $FilePath"
}

$extension = [System.IO.Path]::GetExtension($FilePath).ToLowerInvariant()
if ($extension -eq ".pdf") {
    Get-PdfText -Path $FilePath
}
else {
    $storageFile = Get-StorageFile -Path $FilePath
    $stream = Open-ReadStream -StorageFile $storageFile
    Get-ImageText -RandomAccessStream $stream
}
