using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace DomainLinksDesktop;

internal static class UiScaleHelper
{
    internal const double MinScale = 0.9;
    internal const double MaxScale = 1.6;
    internal const double DefaultScale = 1.0;
    internal const double ScaleStep = 0.1;

    internal static double Clamp(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale))
        {
            return DefaultScale;
        }

        return Math.Max(MinScale, Math.Min(MaxScale, Math.Round(scale, 2)));
    }

    internal static void ApplyWindowScale(Window window, double scale)
    {
        if (window.Content is FrameworkElement root)
        {
            root.LayoutTransform = new ScaleTransform(scale, scale);
        }
    }

    internal static void ApplyWebViewScale(WebView2? webView, double scale)
    {
        if (webView?.CoreWebView2 is null)
        {
            return;
        }

        webView.ZoomFactor = scale;
    }
}
