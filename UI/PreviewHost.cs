using System.Globalization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using NicPad.Core;

namespace NicPad.UI;

/// <summary>Shared WebView2 environment with an isolated profile folder.</summary>
internal static class PreviewEnvironment
{
    private static Task<CoreWebView2Environment?>? environment;

    public static Task<CoreWebView2Environment?> GetAsync(string userDataFolder) =>
        environment ??= CreateAsync(userDataFolder);

    public static string? RuntimeVersion()
    {
        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return null;
        }
    }

    private static async Task<CoreWebView2Environment?> CreateAsync(string userDataFolder)
    {
        try
        {
            Directory.CreateDirectory(userDataFolder);
            var options = new CoreWebView2EnvironmentOptions
            {
                AllowSingleSignOnUsingOSPrimaryAccount = false,
                EnableTrackingPrevention = true,
            };
            return await CoreWebView2Environment.CreateAsync(null, userDataFolder, options).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}

/// <summary>
/// Locked-down Markdown preview on WebView2 (Edge/Chromium):
/// scripts off, navigation blocked, downloads/new windows/permissions denied,
/// every network request filtered through <see cref="UrlPolicy"/>.
/// </summary>
internal sealed class PreviewHost : Panel
{
    private const int MaxNavigateStringChars = 1_500_000;

    private readonly WebView2 web;
    private readonly Label message;
    private readonly string userDataFolder;
    private CoreWebView2Environment? environment;
    private bool ready;
    private bool initStarted;
    private string? pendingHtml;
    private bool expectingNavigation;
    private double pendingScroll;

    public PreviewHost(string userDataFolder, Color background)
    {
        this.userDataFolder = userDataFolder;
        Dock = DockStyle.Fill;
        BackColor = background;

        web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = background, Visible = false };
        message = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "Loading preview...",
            AccessibleName = "Preview status",
        };
        Controls.Add(web);
        Controls.Add(message);
    }

    public event EventHandler<string>? LinkClicked;

    public bool AllowRemoteImages { get; set; }

    public void SetBackground(Color background, Color foreground)
    {
        BackColor = background;
        message.BackColor = background;
        message.ForeColor = foreground;
        web.DefaultBackgroundColor = background;
    }

    public void ShowHtml(string html)
    {
        if (html.Length > MaxNavigateStringChars)
        {
            ShowMessage("This document is too large to preview. Split it into smaller files, or use File > Export as HTML.");
            return;
        }

        pendingHtml = html;
        if (!initStarted)
        {
            initStarted = true;
            _ = InitializeAsync();
            return;
        }

        if (ready)
        {
            _ = NavigateAsync();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            web.Dispose();
            message.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task InitializeAsync()
    {
        try
        {
            environment = await PreviewEnvironment.GetAsync(userDataFolder).ConfigureAwait(true);
            if (environment is null || IsDisposed)
            {
                ShowMessage("The preview needs the Microsoft Edge WebView2 Runtime.\nInstall it from https://go.microsoft.com/fwlink/p/?LinkId=2124703 and restart NicPad.");
                return;
            }

            await web.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
            var core = web.CoreWebView2;
            var s = core.Settings;
            s.IsScriptEnabled = false;
            s.AreDefaultScriptDialogsEnabled = false;
            s.IsWebMessageEnabled = false;
            s.AreHostObjectsAllowed = false;
            s.AreDevToolsEnabled = false;
            s.AreDefaultContextMenusEnabled = false;
            s.IsStatusBarEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsGeneralAutofillEnabled = false;
            s.IsPasswordAutosaveEnabled = false;
            s.IsSwipeNavigationEnabled = false;
            s.IsZoomControlEnabled = true;

            core.NavigationStarting += OnNavigationStarting;
            core.FrameNavigationStarting += (_, e) => e.Cancel = true;
            core.NewWindowRequested += OnNewWindowRequested;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnWebResourceRequested;
            core.NavigationCompleted += OnNavigationCompleted;

            ready = true;
            web.Visible = true;
            message.Visible = false;
            await NavigateAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or System.Runtime.InteropServices.COMException or ObjectDisposedException)
        {
            ShowMessage("The preview couldn't start: " + ex.Message);
        }
    }

    private async Task NavigateAsync()
    {
        if (!ready || pendingHtml is null || IsDisposed)
        {
            return;
        }

        var html = pendingHtml;
        pendingHtml = null;
        try
        {
            // Keep the reader's place across re-renders. This is our own constant script, not page content
            // (page scripts stay disabled).
            var y = await web.CoreWebView2.ExecuteScriptAsync("window.scrollY").ConfigureAwait(true);
            pendingScroll = double.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            pendingScroll = 0;
        }

        if (IsDisposed)
        {
            return;
        }

        web.Visible = true;
        message.Visible = false;
        expectingNavigation = true;
        web.CoreWebView2.NavigateToString(html);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (expectingNavigation && (e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)))
        {
            expectingNavigation = false;
            return;
        }

        e.Cancel = true;
        if (e.IsUserInitiated)
        {
            RaiseLink(e.Uri);
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (e.IsUserInitiated)
        {
            RaiseLink(e.Uri);
        }
    }

    private void RaiseLink(string uri)
    {
        if (UrlPolicy.CanOpenExternally(uri))
        {
            BeginInvoke(() => LinkClicked?.Invoke(this, uri));
        }
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var isImage = e.ResourceContext == CoreWebView2WebResourceContext.Image;
        if (!UrlPolicy.IsAllowedPreviewRequest(e.Request.Uri, isImage, AllowRemoteImages) && environment is not null)
        {
            e.Response = environment.CreateWebResourceResponse(null, 403, "Blocked by NicPad", "");
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (pendingScroll > 0 && ready)
        {
            var y = pendingScroll.ToString(CultureInfo.InvariantCulture);
            pendingScroll = 0;
            _ = web.CoreWebView2.ExecuteScriptAsync($"window.scrollTo(0,{y})");
        }

        if (pendingHtml is not null)
        {
            _ = NavigateAsync();
        }
    }

    private void ShowMessage(string text)
    {
        if (IsDisposed)
        {
            return;
        }

        message.Text = text;
        message.Visible = true;
        web.Visible = false;
    }
}
