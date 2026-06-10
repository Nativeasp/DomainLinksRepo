using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;

namespace DomainLinksDesktop;

internal static class BackendAutoStarter
{
    public static async Task EnsureBackendIsAvailableAsync(DomainLinksDesktopSettings settings, CancellationToken cancellationToken = default)
    {
        if (!settings.AutoStartLocalBackend || !Uri.TryCreate(settings.BackendBaseUrl, UriKind.Absolute, out var backendUri))
        {
            return;
        }

        if (!IsLoopbackHost(backendUri.Host))
        {
            return;
        }

        if (await IsHealthEndpointAvailableAsync(backendUri, cancellationToken))
        {
            return;
        }

        var backendWorkingDirectory = TryResolveBackendWorkingDirectory(settings.BackendRelativeWorkingDirectory);
        if (backendWorkingDirectory is null)
        {
            return;
        }

        var pythonExecutable = ResolvePythonExecutable(backendWorkingDirectory, settings.BackendPythonExecutable);
        if (pythonExecutable is null)
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExecutable,
            Arguments = settings.BackendStartupArguments,
            WorkingDirectory = backendWorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        Process.Start(startInfo);
        await WaitForHealthEndpointAsync(backendUri, TimeSpan.FromSeconds(10), cancellationToken);
    }

    private static async Task<bool> IsHealthEndpointAvailableAsync(Uri backendUri, CancellationToken cancellationToken)
    {
        using var client = new HttpClient
        {
            BaseAddress = new Uri($"{backendUri.Scheme}://{backendUri.Authority}")
        };

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await client.GetAsync("/health", linkedCts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    private static async Task WaitForHealthEndpointAsync(Uri backendUri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await IsHealthEndpointAvailableAsync(backendUri, cancellationToken))
            {
                return;
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    private static string? TryResolveBackendWorkingDirectory(string relativeBackendPath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativeBackendPath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string? ResolvePythonExecutable(string backendWorkingDirectory, string configuredExecutable)
    {
        var configuredPath = Path.Combine(backendWorkingDirectory, configuredExecutable);
        if (File.Exists(configuredPath))
        {
            return configuredPath;
        }

        return FindOnPath("python") ?? FindOnPath("py");
    }

    private static string? FindOnPath(string executableName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var pathEntry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(pathEntry, executableName + ".exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsLoopbackHost(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || host.Equals("::1", StringComparison.OrdinalIgnoreCase);
    }
}
