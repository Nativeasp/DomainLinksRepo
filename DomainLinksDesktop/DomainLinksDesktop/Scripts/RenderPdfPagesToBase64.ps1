param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,

    [int]$MaxDimension = 1800
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Runtime.WindowsRuntime
[void][Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
[void][Windows.Storage.FileAccessMode, Windows.Storage, ContentType = WindowsRuntime]
[void][Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType = WindowsRuntime]
[void][Windows.Data.Pdf.PdfPageRenderOptions, Windows.Data.Pdf, ContentType = WindowsRuntime]
[void][Windows.Foundation.IAsyncAction, Windows.Foundation, ContentType = WindowsRuntime]
[void][Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]
[void][Windows.Storage.Streams.IRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]

function Invoke-WinRtTask {
    param(
        [Parameter(Mandatory = $true)]
        $Operation,
        [Type]$ResultType
    )

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

function Read-StreamBytes {
    param($Stream)

    $Stream.Seek(0)
    $managedStream = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($Stream)
    try {
        $memoryStream = New-Object System.IO.MemoryStream
        $managedStream.CopyTo($memoryStream)
        return $memoryStream.ToArray()
    }
    finally {
        $managedStream.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $FilePath)) {
    throw "File not found: $FilePath"
}

$resolvedPath = [System.IO.Path]::GetFullPath($FilePath)
$storageFile = Get-StorageFile -Path $resolvedPath
$pdfDocument = Invoke-WinRtTask ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($storageFile)) ([Windows.Data.Pdf.PdfDocument])
$pages = New-Object System.Collections.Generic.List[string]

for ($pageIndex = 0; $pageIndex -lt $pdfDocument.PageCount; $pageIndex++) {
    $page = $pdfDocument.GetPage([uint32]$pageIndex)
    $renderOptions = New-Object Windows.Data.Pdf.PdfPageRenderOptions
    $widthScale = $MaxDimension / [Math]::Max($page.Size.Width, 1)
    $heightScale = $MaxDimension / [Math]::Max($page.Size.Height, 1)
    $scale = [Math]::Min(1.0, [Math]::Min($widthScale, $heightScale))
    $renderOptions.DestinationWidth = [uint32][Math]::Max(1, [Math]::Round($page.Size.Width * $scale))
    $renderOptions.DestinationHeight = [uint32][Math]::Max(1, [Math]::Round($page.Size.Height * $scale))

    $stream = New-Object Windows.Storage.Streams.InMemoryRandomAccessStream
    Invoke-WinRtTask ($page.RenderToStreamAsync($stream, $renderOptions)) $null | Out-Null
    $bytes = Read-StreamBytes -Stream $stream
    $pages.Add([Convert]::ToBase64String($bytes))
}

[pscustomobject]@{
    pages = $pages
} | ConvertTo-Json -Depth 3 -Compress
