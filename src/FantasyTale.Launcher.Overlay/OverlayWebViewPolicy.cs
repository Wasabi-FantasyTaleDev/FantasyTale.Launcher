using System.Runtime.InteropServices;
using FantasyTale.Launcher.Web;
using Microsoft.Web.WebView2.Core;

namespace FantasyTale.Launcher.Overlay;

/// <summary>Installs document and popup checks before an overlay WebView is navigated.</summary>
internal sealed class OverlayWebViewPolicy
{
    private readonly TrustedWebDocument _document;
    private readonly Action<string> _rejected;

    internal OverlayWebViewPolicy(CoreWebView2 webView, string document, Action<string> rejected)
    {
        _document = new(document);
        _rejected = rejected;
        webView.NavigationStarting += (_, args) =>
        {
            args.Cancel = true;
            try
            {
                if (_document.Allows(args.Uri)) { args.Cancel = false; return; }
            }
            catch (Exception error) when (error is ArgumentException or COMException or InvalidOperationException)
            {
                // Missing event metadata leaves the navigation canceled.
            }
            _rejected("navigation");
        };
        webView.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            _rejected("popup");
        };
    }

    internal bool TryReadMessage(CoreWebView2WebMessageReceivedEventArgs args, out string message)
    {
        message = string.Empty;
        try
        {
            if (_document.Allows(args.Source))
            {
                message = args.TryGetWebMessageAsString();
                return message is not null;
            }
        }
        catch (Exception error) when (error is ArgumentException or COMException or InvalidOperationException)
        {
            // Non-string messages and unavailable event metadata cannot reach overlay state handlers.
        }
        _rejected("message");
        return false;
    }
}
