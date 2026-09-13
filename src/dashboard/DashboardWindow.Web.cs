using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ResticBackuper.Dashboard
{
    public partial class DashboardWindow
    {
        private const int WebProtocolVersion = 1;
        private const int MaximumWebMessageLength = 64 * 1024;
        private const string WebHostName = "restic.local";
        private const string WebHostOrigin = "https://restic.local";

        private WebView2 webPresentation;
        private Border webPresentationErrorSurface;
        private TextBlock webPresentationErrorText;
        private Task webPresentationInitialization;
        private bool webPresentationConfigured;
        private bool webPresentationClosing;
        private bool webPresentationReady;
        private bool webPresentationHandshakeReady;
        private long webPresentationRevision;
        private string webPresentationPendingState;
        private string webTelemetryError;
        private readonly JavaScriptSerializer webPresentationSerializer =
            new JavaScriptSerializer();

        private void MountWebPresentation(Grid shell, FrameworkElement nativeRoot)
        {
            if (options == null || options.UseNativePresentation || shell == null)
            {
                return;
            }

            // The native tree remains fully built and wired so the protected dialogs and
            // existing operations stay as the adapter layer. WebView2 owns the visible surface.
            if (nativeRoot != null)
            {
                nativeRoot.Visibility = Visibility.Collapsed;
            }
            if (navigationRail != null)
            {
                navigationRail.Visibility = Visibility.Collapsed;
            }

            Panel previousParent = webPresentation == null
                ? null
                : webPresentation.Parent as Panel;
            if (previousParent != null && !object.ReferenceEquals(previousParent, shell))
            {
                previousParent.Children.Remove(webPresentation);
            }

            if (webPresentation == null)
            {
                webPresentation = new WebView2();
                webPresentation.HorizontalAlignment = HorizontalAlignment.Stretch;
                webPresentation.VerticalAlignment = VerticalAlignment.Stretch;
                webPresentation.Focusable = true;
                webPresentation.AllowExternalDrop = false;
                webPresentation.Visibility = Visibility.Visible;
                webPresentation.Loaded += OnWebPresentationLoaded;
            }
            if (!shell.Children.Contains(webPresentation))
            {
                shell.Children.Add(webPresentation);
            }
            Grid.SetColumn(webPresentation, 0);
            Grid.SetColumnSpan(webPresentation, 2);
            Panel.SetZIndex(webPresentation, 100);

            MountWebPresentationErrorSurface(shell);
            if (webPresentation.CoreWebView2 != null)
            {
                ConfigureWebPresentation(webPresentation.CoreWebView2);
                PublishWebPresentationState();
                return;
            }
        }

        private void OnWebPresentationLoaded(object sender, RoutedEventArgs args)
        {
            if (webPresentationClosing || webPresentationInitialization != null ||
                options == null || options.UseNativePresentation || webPresentation == null)
            {
                return;
            }
            webPresentationInitialization = InitializeWebPresentationAsync();
        }

        private void MountWebPresentationErrorSurface(Grid shell)
        {
            if (webPresentationErrorSurface == null)
            {
                webPresentationErrorSurface = new Border();
                webPresentationErrorSurface.Background = BackgroundTop;
                webPresentationErrorSurface.Padding = new Thickness(42);
                webPresentationErrorSurface.Visibility = Visibility.Collapsed;

                Grid errorLayout = new Grid();
                errorLayout.VerticalAlignment = VerticalAlignment.Center;
                errorLayout.HorizontalAlignment = HorizontalAlignment.Center;
                errorLayout.MaxWidth = 720;

                webPresentationErrorText = new TextBlock();
                webPresentationErrorText.Foreground = PrimaryText;
                webPresentationErrorText.FontSize = 15;
                webPresentationErrorText.TextWrapping = TextWrapping.Wrap;
                errorLayout.Children.Add(webPresentationErrorText);
                webPresentationErrorSurface.Child = errorLayout;
            }

            Panel previousParent = webPresentationErrorSurface.Parent as Panel;
            if (previousParent != null && !object.ReferenceEquals(previousParent, shell))
            {
                previousParent.Children.Remove(webPresentationErrorSurface);
            }
            if (!shell.Children.Contains(webPresentationErrorSurface))
            {
                shell.Children.Add(webPresentationErrorSurface);
            }
            Grid.SetColumn(webPresentationErrorSurface, 0);
            Grid.SetColumnSpan(webPresentationErrorSurface, 2);
            Panel.SetZIndex(webPresentationErrorSurface, 200);
        }

        private async Task InitializeWebPresentationAsync()
        {
            try
            {
                if (webPresentationClosing)
                {
                    return;
                }
                string webRoot = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "web");
                string indexPath = Path.Combine(webRoot, "index.html");
                if (!Directory.Exists(webRoot) || !File.Exists(indexPath))
                {
                    ShowWebPresentationFailure(
                        "The bundled BeautifulUI web surface is missing. Expected: " + indexPath);
                    return;
                }

                string userDataFolder = GetWebUserDataFolder();
                Directory.CreateDirectory(userDataFolder);
                CoreWebView2Environment environment =
                    await CoreWebView2Environment.CreateAsync(null, userDataFolder, null);
                if (webPresentationClosing || webPresentation == null)
                {
                    return;
                }
                await webPresentation.EnsureCoreWebView2Async(environment);
                if (webPresentationClosing || webPresentation == null ||
                    webPresentation.CoreWebView2 == null)
                {
                    if (!webPresentationClosing)
                    {
                        ShowWebPresentationFailure("WebView2 initialized without a browser surface.");
                    }
                    return;
                }

                ConfigureWebPresentation(webPresentation.CoreWebView2);
                webPresentation.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    WebHostName,
                    webRoot,
                    CoreWebView2HostResourceAccessKind.DenyCors);
                webPresentationReady = true;
                webPresentationHandshakeReady = false;
                webPresentation.CoreWebView2.Navigate(WebHostOrigin + "/index.html");
            }
            catch (Exception error)
            {
                if (webPresentationClosing)
                {
                    return;
                }
                ShowWebPresentationFailure(
                    "The BeautifulUI web surface could not start.\n\n" +
                    error.Message +
                    "\n\nInstall the WebView2 Runtime and rebuild the bundled web assets. " +
                    "Use --native only for local diagnostics.");
            }
        }

        private string GetWebUserDataFolder()
        {
            if (options != null && options.UseIsolatedPresentationStore)
            {
                return Path.Combine(
                    Path.GetTempPath(),
                    "ResticBackuperDashboardBeautifulUIWebView2");
            }
            string localApplicationData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localApplicationData))
            {
                localApplicationData = Path.GetTempPath();
            }
            return Path.Combine(
                localApplicationData,
                "ResticBackuperDashboard",
                "WebView2");
        }

        private void ConfigureWebPresentation(CoreWebView2 core)
        {
            if (core == null || webPresentationConfigured)
            {
                return;
            }

            CoreWebView2Settings settings = core.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDefaultScriptDialogsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
            settings.IsWebMessageEnabled = true;

            core.NavigationStarting += OnWebPresentationNavigationStarting;
            core.NavigationCompleted += OnWebPresentationNavigationCompleted;
            core.NewWindowRequested += OnWebPresentationNewWindowRequested;
            core.DownloadStarting += OnWebPresentationDownloadStarting;
            core.PermissionRequested += OnWebPresentationPermissionRequested;
            core.LaunchingExternalUriScheme += OnWebPresentationExternalUriScheme;
            core.BasicAuthenticationRequested += OnWebPresentationBasicAuthentication;
            core.ServerCertificateErrorDetected += OnWebPresentationCertificateError;
            core.ContextMenuRequested += OnWebPresentationContextMenuRequested;
            core.WebResourceRequested += OnWebPresentationResourceRequested;
            core.WebMessageReceived += OnWebPresentationMessageReceived;
            core.ProcessFailed += OnWebPresentationProcessFailed;
            core.AddWebResourceRequestedFilter(
                "*",
                CoreWebView2WebResourceContext.All);
            webPresentationConfigured = true;
        }

        private void OnWebPresentationNavigationStarting(
            object sender,
            CoreWebView2NavigationStartingEventArgs args)
        {
            if (webPresentationClosing)
            {
                args.Cancel = true;
                return;
            }
            webPresentationHandshakeReady = false;
            if (!IsAllowedWebUri(args.Uri))
            {
                args.Cancel = true;
            }
        }

        private void OnWebPresentationNavigationCompleted(
            object sender,
            CoreWebView2NavigationCompletedEventArgs args)
        {
            if (webPresentationClosing)
            {
                return;
            }
            if (!args.IsSuccess)
            {
                ShowWebPresentationFailure(
                    "The BeautifulUI web surface failed to load (" +
                    args.WebErrorStatus.ToString() + ").");
            }
        }

        private void OnWebPresentationNewWindowRequested(
            object sender,
            CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;
        }

        private void OnWebPresentationDownloadStarting(
            object sender,
            CoreWebView2DownloadStartingEventArgs args)
        {
            args.Cancel = true;
            args.Handled = true;
        }

        private void OnWebPresentationPermissionRequested(
            object sender,
            CoreWebView2PermissionRequestedEventArgs args)
        {
            args.State = CoreWebView2PermissionState.Deny;
            args.Handled = true;
        }

        private void OnWebPresentationExternalUriScheme(
            object sender,
            CoreWebView2LaunchingExternalUriSchemeEventArgs args)
        {
            args.Cancel = true;
        }

        private void OnWebPresentationBasicAuthentication(
            object sender,
            CoreWebView2BasicAuthenticationRequestedEventArgs args)
        {
            args.Cancel = true;
        }

        private void OnWebPresentationCertificateError(
            object sender,
            CoreWebView2ServerCertificateErrorDetectedEventArgs args)
        {
            args.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
        }

        private void OnWebPresentationContextMenuRequested(
            object sender,
            CoreWebView2ContextMenuRequestedEventArgs args)
        {
            args.Handled = true;
        }

        private void OnWebPresentationResourceRequested(
            object sender,
            CoreWebView2WebResourceRequestedEventArgs args)
        {
            string uri = args.Request == null ? string.Empty : args.Request.Uri;
            if (IsAllowedWebUri(uri))
            {
                return;
            }
            CoreWebView2 core = sender as CoreWebView2;
            if (core != null)
            {
                args.Response = core.Environment.CreateWebResourceResponse(
                    new MemoryStream(),
                    403,
                    "Blocked",
                    "Content-Type: text/plain");
            }
        }

        private void OnWebPresentationProcessFailed(
            object sender,
            CoreWebView2ProcessFailedEventArgs args)
        {
            ShowWebPresentationFailure(
                "The WebView2 renderer stopped unexpectedly (" +
                args.ProcessFailedKind.ToString() + "). Restart the dashboard to try again.");
        }

        private void OnWebPresentationMessageReceived(
            object sender,
            CoreWebView2WebMessageReceivedEventArgs args)
        {
            if (webPresentationClosing)
            {
                return;
            }
            if (!IsAllowedWebUri(args.Source))
            {
                return;
            }
            string json = args.WebMessageAsJson;
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumWebMessageLength)
            {
                return;
            }

            IDictionary<string, object> message;
            try
            {
                object parsed = webPresentationSerializer.DeserializeObject(json);
                message = parsed as IDictionary<string, object>;
                if (message == null && parsed is string)
                {
                    message = webPresentationSerializer.DeserializeObject((string)parsed)
                        as IDictionary<string, object>;
                }
            }
            catch
            {
                return;
            }
            if (message == null)
            {
                return;
            }

            string type = WebReadString(message, "type", 32);
            if (string.Equals(type, "ready", StringComparison.Ordinal))
            {
                webPresentationHandshakeReady = true;
                PublishWebPresentationState();
                return;
            }
            if (!string.Equals(type, "command", StringComparison.Ordinal))
            {
                return;
            }

            string id = WebReadString(message, "id", 128);
            string command = WebReadString(message, "command", 64);
            IDictionary<string, object> payload = null;
            object rawPayload;
            if (message.TryGetValue("payload", out rawPayload))
            {
                payload = rawPayload as IDictionary<string, object>;
            }

            if (string.Equals(command, "ready", StringComparison.Ordinal))
            {
                webPresentationHandshakeReady = true;
                SendWebCommandResult(id, true, string.Empty);
                PublishWebPresentationState();
                return;
            }

            string error;
            bool accepted = ExecuteWebCommand(command, payload, out error);
            SendWebCommandResult(id, accepted, error);
        }

        private bool ExecuteWebCommand(
            string command,
            IDictionary<string, object> payload,
            out string error)
        {
            error = string.Empty;
            if (string.Equals(command, "refresh", StringComparison.Ordinal))
            {
                RefreshDashboard();
                return true;
            }
            if (string.Equals(command, "navigate", StringComparison.Ordinal))
            {
                string page = WebReadString(payload, "page", 32);
                if (page != "Protection" && page != "Activity" &&
                    page != "Restore" && page != "Settings")
                {
                    error = "The requested dashboard page is invalid.";
                    return false;
                }
                ShowDashboardPage(page);
                PublishWebPresentationState();
                return true;
            }
            if (string.Equals(command, "setTheme", StringComparison.Ordinal))
            {
                DashboardThemePreference preference;
                string theme = WebReadString(payload, "theme", 32);
                if (string.IsNullOrWhiteSpace(theme))
                {
                    theme = WebReadString(payload, "preference", 32);
                }
                if (!DashboardThemeManager.TryParsePreference(theme, out preference) ||
                    !themeButtons.ContainsKey(preference))
                {
                    error = "The requested dashboard theme is invalid.";
                    return false;
                }
                OnThemeSegmentClick(themeButtons[preference], new RoutedEventArgs());
                return true;
            }
            if (string.Equals(command, "togglePreview", StringComparison.Ordinal))
            {
                if (previewButton == null || !previewButton.IsEnabled)
                {
                    error = "The preview control is unavailable.";
                    return false;
                }
                previewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return true;
            }
            if (string.Equals(command, "selectRun", StringComparison.Ordinal))
            {
                string runId = WebReadString(payload, "runId", 128);
                RunMetricView run = FindHistoryRun(runId);
                if (run == null || historyGrid == null)
                {
                    error = "The requested backup run is unavailable.";
                    return false;
                }
                historyGrid.SelectedItem = run;
                historyGrid.ScrollIntoView(run);
                UpdateActivityActions();
                PublishWebPresentationState();
                return true;
            }
            if (string.Equals(command, "viewRunDetails", StringComparison.Ordinal))
            {
                string runId = WebReadString(payload, "runId", 128);
                RunMetricView run = string.IsNullOrWhiteSpace(runId)
                    ? (historyGrid == null ? null : historyGrid.SelectedItem as RunMetricView)
                    : FindHistoryRun(runId);
                if (run == null || historyGrid == null)
                {
                    error = "The requested backup run is unavailable.";
                    return false;
                }
                historyGrid.SelectedItem = run;
                if (viewRunDetailsButton == null || !viewRunDetailsButton.IsEnabled)
                {
                    error = "Run details are unavailable for the selected run.";
                    return false;
                }
                OnViewRunDetailsClick(viewRunDetailsButton, new RoutedEventArgs());
                return true;
            }

            Button actionButton;
            if (string.Equals(command, "backupNow", StringComparison.Ordinal))
            {
                actionButton = backupNowButton;
                if (!CheckWebAction(actionButton, out error)) return false;
                OnBackupNowClick(actionButton, new RoutedEventArgs());
                return true;
            }
            if (string.Equals(command, "cancelBackup", StringComparison.Ordinal))
            {
                actionButton = cancelBackupButton;
                if (!CheckWebAction(actionButton, out error)) return false;
                OnCancelBackupClick(actionButton, new RoutedEventArgs());
                return true;
            }
            if (string.Equals(command, "addSource", StringComparison.Ordinal))
            {
                actionButton = addSourceButton;
                if (!CheckWebAction(actionButton, out error)) return false;
                OnAddSourceClick(actionButton, new RoutedEventArgs());
                return true;
            }
            if (string.Equals(command, "removeSource", StringComparison.Ordinal))
            {
                string path = WebReadString(payload, "path", 4096);
                Button remove = FindRemoveSourceButton(path);
                if (!CheckWebAction(remove, out error)) return false;
                OnRemoveSourceClick(remove, new RoutedEventArgs());
                return true;
            }
            if (string.Equals(command, "retrySourceChange", StringComparison.Ordinal))
            {
                actionButton = sourceOperationRetryButton;
                if (!CheckVisibleWebAction(actionButton, out error)) return false;
                OnRetrySourceOperationClick(actionButton, new RoutedEventArgs());
                PublishWebPresentationState();
                return true;
            }
            if (string.Equals(command, "dismissSourceChange", StringComparison.Ordinal))
            {
                actionButton = sourceOperationDismissButton;
                if (!CheckVisibleWebAction(actionButton, out error)) return false;
                OnDismissSourceOperationClick(actionButton, new RoutedEventArgs());
                PublishWebPresentationState();
                return true;
            }
            if (string.Equals(command, "editSchedule", StringComparison.Ordinal))
            {
                actionButton = editScheduleButton;
                if (!CheckWebAction(actionButton, out error)) return false;
                OnEditScheduleClick(actionButton, new RoutedEventArgs());
                return true;
            }
            if (string.Equals(command, "changeRepository", StringComparison.Ordinal))
            {
                actionButton = changeRepositoryButton != null && changeRepositoryButton.IsEnabled
                    ? changeRepositoryButton
                    : settingsChangeRepositoryButton;
                if (!CheckWebAction(actionButton, out error)) return false;
                OnChangeRepositoryClick(actionButton, new RoutedEventArgs());
                return true;
            }
            if (string.Equals(command, "repairRepository", StringComparison.Ordinal))
            {
                actionButton = protectionRepositoryRecoveryButton != null &&
                    protectionRepositoryRecoveryButton.IsEnabled
                    ? protectionRepositoryRecoveryButton
                    : settingsRepositoryRecoveryButton;
                if (!CheckWebAction(actionButton, out error)) return false;
                OnRepairRepositoryClick(actionButton, new RoutedEventArgs());
                return true;
            }
            if (string.Equals(command, "reviewChanges", StringComparison.Ordinal))
            {
                actionButton = reviewChangesButton;
                if (!CheckWebAction(actionButton, out error)) return false;
                OnReviewChangesClick(actionButton, new RoutedEventArgs());
                return true;
            }
            if (string.Equals(command, "openRestore", StringComparison.Ordinal))
            {
                actionButton = openRestoreCenterButton;
                if (!CheckWebAction(actionButton, out error)) return false;
                OnOpenRestoreCenterClick(actionButton, new RoutedEventArgs());
                return true;
            }
            if (string.Equals(command, "checkReadiness", StringComparison.Ordinal))
            {
                actionButton = checkRecoveryReadinessButton;
                if (!CheckWebAction(actionButton, out error)) return false;
                OnCheckRecoveryReadinessClick(actionButton, new RoutedEventArgs());
                return true;
            }
            if (string.Equals(command, "exportDiagnostics", StringComparison.Ordinal))
            {
                actionButton = exportDiagnosticsButton;
                if (!CheckWebAction(actionButton, out error)) return false;
                OnExportDiagnosticsClick(actionButton, new RoutedEventArgs());
                return true;
            }

            error = "The requested dashboard command is not allowed.";
            return false;
        }

        private bool CheckWebAction(Button button, out string error)
        {
            if (button == null)
            {
                error = "The requested dashboard action is unavailable.";
                return false;
            }
            if (!button.IsEnabled)
            {
                error = button.ToolTip == null
                    ? "The requested dashboard action is currently unavailable."
                    : button.ToolTip.ToString();
                return false;
            }
            error = string.Empty;
            return true;
        }

        private bool CheckVisibleWebAction(Button button, out string error)
        {
            if (!CheckWebAction(button, out error))
            {
                return false;
            }
            if (button.Visibility != Visibility.Visible)
            {
                error = "The requested dashboard action is currently unavailable.";
                return false;
            }
            return true;
        }

        private Button FindRemoveSourceButton(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }
            foreach (Button button in removeSourceButtons)
            {
                BackupSourceView source = button.Tag as BackupSourceView;
                if (source != null &&
                    string.Equals(source.SourcePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    return button;
                }
            }
            return null;
        }

        private RunMetricView FindHistoryRun(string runId)
        {
            if (historyGrid == null || string.IsNullOrWhiteSpace(runId))
            {
                return null;
            }
            return historyGrid.Items.OfType<RunMetricView>().FirstOrDefault(
                run => string.Equals(run.RunId, runId, StringComparison.Ordinal));
        }

        private static string WebReadString(
            IDictionary<string, object> values,
            string key,
            int maximumLength)
        {
            if (values == null)
            {
                return string.Empty;
            }
            object value;
            if (!values.TryGetValue(key, out value) || value == null)
            {
                return string.Empty;
            }
            string result = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            if (result.Length > maximumLength)
            {
                return string.Empty;
            }
            return result;
        }

        private void SendWebCommandResult(string id, bool ok, string message)
        {
            if (webPresentationClosing || !webPresentationReady || webPresentation == null ||
                webPresentation.CoreWebView2 == null)
            {
                return;
            }
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["type"] = "result";
            result["id"] = id ?? string.Empty;
            result["ok"] = ok;
            if (!ok && !string.IsNullOrWhiteSpace(message))
            {
                result["message"] = message;
            }
            try
            {
                webPresentation.CoreWebView2.PostWebMessageAsJson(
                    webPresentationSerializer.Serialize(result));
            }
            catch
            {
                // The renderer may have exited between the command and the response.
            }
        }

        private void PublishWebPresentationState()
        {
            if (webPresentationClosing || options == null || options.UseNativePresentation ||
                webPresentation == null)
            {
                return;
            }
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(PublishWebPresentationState));
                return;
            }
            try
            {
                webPresentationRevision++;
                Dictionary<string, object> envelope = new Dictionary<string, object>();
                envelope["type"] = "state";
                envelope["state"] = BuildWebPresentationState();
                webPresentationPendingState = webPresentationSerializer.Serialize(envelope);
                if (!webPresentationReady || !webPresentationHandshakeReady ||
                    webPresentation.CoreWebView2 == null)
                {
                    return;
                }
                webPresentation.CoreWebView2.PostWebMessageAsJson(
                    webPresentationPendingState);
            }
            catch (Exception error)
            {
                ShowWebPresentationFailure(
                    "The dashboard state could not be delivered to the BeautifulUI surface.\n\n" +
                    error.Message);
            }
        }

        private Dictionary<string, object> BuildWebPresentationState()
        {
            TelemetrySnapshot snapshot = lastSnapshot;
            Dictionary<string, object> state = new Dictionary<string, object>();
            state["page"] = selectedDashboardPage;
            state["demo"] = false;
            state["preview"] = previewEnabled;
            state["dataError"] = webTelemetryError ?? string.Empty;
            state["theme"] = themePreference.ToString();
            state["dark"] = themeResolution != null && themeResolution.Palette.IsDark;
            state["reducedMotion"] = !DashboardMotion.MotionAllowed();
            state["highContrast"] = themeResolution != null && themeResolution.IsHighContrast;
            state["updated"] = lastUpdated == null ? string.Empty : lastUpdated.Text ?? string.Empty;
            state["subtitle"] = headerSubtitle == null ? string.Empty : headerSubtitle.Text ?? string.Empty;
            state["status"] = BuildWebStatusState(snapshot);
            state["sources"] = BuildWebSourceStates();
            state["sourceStatus"] = sourceStatus == null ? string.Empty : sourceStatus.Text ?? string.Empty;
            state["sourceOperation"] = BuildWebSourceOperationState();
            state["history"] = BuildWebHistoryStates(snapshot);
            RunMetricView selected = historyGrid == null ? null : historyGrid.SelectedItem as RunMetricView;
            state["selectedRunId"] = selected == null ? null : selected.RunId;
            state["schedule"] = BuildWebScheduleState();
            state["repository"] = BuildWebRepositoryState();
            state["freshness"] = BuildWebFreshnessState();
            state["offsite"] = BuildWebOffsiteState(snapshot);
            state["recovery"] = BuildWebRecoveryState();
            state["actions"] = BuildWebActionStates();
            return state;
        }

        private Dictionary<string, object> BuildWebStatusState(TelemetrySnapshot snapshot)
        {
            Dictionary<string, object> status = new Dictionary<string, object>();
            bool presentationPreview = previewEnabled && string.IsNullOrEmpty(webTelemetryError);
            if (!string.IsNullOrEmpty(webTelemetryError)) snapshot = null;
            status["key"] = presentationPreview
                ? "preview"
                : snapshot == null ? "unknown" : snapshot.StateKey ?? "unknown";
            status["title"] = heroTitle == null ? string.Empty : heroTitle.Text ?? string.Empty;
            status["detail"] = heroDetail == null ? string.Empty : heroDetail.Text ?? string.Empty;
            status["badge"] = statusBadgeText == null ? string.Empty : statusBadgeText.Text ?? string.Empty;
            status["active"] = presentationPreview || (snapshot != null && snapshot.IsActive);
            status["success"] = !presentationPreview && snapshot != null && snapshot.IsSuccess;
            status["failure"] = !presentationPreview && snapshot != null && snapshot.IsFailure;
            status["cancelled"] = !presentationPreview && snapshot != null && snapshot.IsCancelled;
            double displayedProgress = presentationPreview
                ? currentProgress
                : snapshot == null ? 0.0 : Math.Max(0.0, Math.Min(1.0, snapshot.Percent));
            status["phaseIndex"] = presentationPreview
                ? (displayedProgress < 0.90 ? 0 : displayedProgress < 0.94 ? 1 : displayedProgress < 0.97 ? 2 : 3)
                : snapshot == null ? -1 : snapshot.PhaseIndex;
            status["phaseLabel"] = presentationPreview
                ? "Preview telemetry"
                : snapshot == null ? string.Empty : snapshot.PhaseLabel ?? string.Empty;
            status["progress"] = displayedProgress;
            status["estimated"] = presentationPreview || snapshot != null && snapshot.ProgressIsEstimated;
            status["runId"] = snapshot == null ? string.Empty : snapshot.RunId ?? string.Empty;
            status["files"] = filesValue == null ? string.Empty : filesValue.Text ?? string.Empty;
            status["bytes"] = bytesValue == null ? string.Empty : bytesValue.Text ?? string.Empty;
            status["speed"] = speedValue == null ? string.Empty : speedValue.Text ?? string.Empty;
            status["elapsed"] = elapsedValue == null ? string.Empty : elapsedValue.Text ?? string.Empty;
            status["errors"] = errorsValue == null ? string.Empty : errorsValue.Text ?? string.Empty;
            status["etaTitle"] = etaTitle == null ? string.Empty : etaTitle.Text ?? string.Empty;
            status["eta"] = etaValue == null ? string.Empty : etaValue.Text ?? string.Empty;
            status["etaHint"] = etaHint == null ? string.Empty : etaHint.Text ?? string.Empty;
            if (!string.IsNullOrEmpty(webTelemetryError))
            {
                status["title"] = "Dashboard data is temporarily unavailable";
                status["detail"] = webTelemetryError;
                status["badge"] = "Status unavailable";
                foreach (string metric in new[] { "files", "bytes", "speed", "elapsed", "errors", "eta" })
                    status[metric] = "—";
            }
            return status;
        }

        private List<Dictionary<string, object>> BuildWebSourceStates()
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            List<BackupSourceView> sources = new List<BackupSourceView>();
            if (currentSourceConfiguration != null && currentSourceConfiguration.Sources != null)
            {
                sources.AddRange(currentSourceConfiguration.Sources);
            }

            foreach (BackupSourceView source in sources)
            {
                bool exists = Directory.Exists(source.SourcePath);
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["path"] = source.SourcePath ?? string.Empty;
                item["name"] = source.DisplayName ?? string.Empty;
                item["isCanary"] = source.IsProtectedCanary;
                item["exists"] = exists;
                item["canRemove"] = !source.IsProtectedCanary &&
                    FindRemoveSourceButton(source.SourcePath) != null &&
                    FindRemoveSourceButton(source.SourcePath).IsEnabled;
                item["detail"] = source.RoleLabel + "  •  " + source.ShortPath;
                result.Add(item);
            }
            return result;
        }

        private Dictionary<string, object> BuildWebSourceOperationState()
        {
            Dictionary<string, object> operation = new Dictionary<string, object>();
            bool active = sourceOperationInProgress ||
                sourceOperationStage == SourceOperationStage.AwaitingApproval ||
                sourceOperationStage == SourceOperationStage.Applying ||
                sourceOperationStage == SourceOperationStage.Verifying;
            operation["active"] = active;
            operation["stage"] = sourceOperationStage.ToString();
            operation["path"] = sourceOperationPath ?? string.Empty;
            string message = sourceOperationError;
            if (string.IsNullOrWhiteSpace(message) && sourceOperationStatusText != null)
            {
                message = sourceOperationStatusText.Text;
            }
            operation["message"] = message ?? string.Empty;
            return operation;
        }

        private List<Dictionary<string, object>> BuildWebHistoryStates(TelemetrySnapshot snapshot)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            IList<RunMetricView> history = snapshot == null || snapshot.History == null
                ? new List<RunMetricView>()
                : snapshot.History;
            foreach (RunMetricView run in history)
            {
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["id"] = run.RunId ?? string.Empty;
                item["started"] = run.StartedLocal.ToString("o", CultureInfo.InvariantCulture);
                item["startedDisplay"] = run.StartedDisplay;
                item["type"] = run.TypeLabel ?? string.Empty;
                item["result"] = run.StateLabel ?? string.Empty;
                item["success"] = run.Success;
                item["durationSeconds"] = Math.Max(0.0, run.DurationSeconds);
                item["durationDisplay"] = run.DurationDisplay;
                item["files"] = run.Files;
                item["filesDisplay"] = run.FilesDisplay;
                item["processedBytes"] = run.ProcessedBytes;
                item["processedDisplay"] = run.ProcessedBytesDisplay;
                item["storedBytes"] = run.StoredBytes;
                item["storedDisplay"] = run.StoredBytesDisplay;
                item["snapshot"] = run.SnapshotShort ?? string.Empty;
                result.Add(item);
            }
            return result;
        }

        private Dictionary<string, object> BuildWebScheduleState()
        {
            Dictionary<string, object> schedule = new Dictionary<string, object>();
            schedule["summary"] = currentTaskSchedule == null
                ? (string.IsNullOrWhiteSpace(scheduleReadError) ? "Schedule unavailable" : scheduleReadError)
                : currentTaskSchedule.Summary;
            schedule["nextRun"] = currentTaskSchedule == null
                ? "Unavailable"
                : currentTaskSchedule.NextRunDisplay;
            schedule["detail"] = currentTaskSchedule == null
                ? (string.IsNullOrWhiteSpace(scheduleReadError) ? "The installed schedule could not be verified." : scheduleReadError)
                : currentTaskSchedule.SettingsSummary;
            return schedule;
        }

        private Dictionary<string, object> BuildWebRepositoryState()
        {
            Dictionary<string, object> repository = new Dictionary<string, object>();
            repository["path"] = currentSourceConfiguration == null
                ? settingsRepositoryValue == null ? string.Empty : settingsRepositoryValue.Text ?? string.Empty
                : currentSourceConfiguration.RepositoryPath ?? string.Empty;
            repository["volume"] = settingsRepositoryVolume == null
                ? string.Empty
                : settingsRepositoryVolume.Text ?? string.Empty;
            return repository;
        }

        private Dictionary<string, object> BuildWebFreshnessState()
        {
            Dictionary<string, object> freshness = new Dictionary<string, object>();
            freshness["title"] = settingsFreshnessStatus == null
                ? string.Empty
                : settingsFreshnessStatus.Text ?? string.Empty;
            freshness["detail"] = settingsFreshnessDetail == null
                ? string.Empty
                : settingsFreshnessDetail.Text ?? string.Empty;
            return freshness;
        }

        private Dictionary<string, object> BuildWebOffsiteState(TelemetrySnapshot snapshot)
        {
            Dictionary<string, object> offsite = new Dictionary<string, object>();
            offsite["title"] = settingsOffsiteStatus == null
                ? string.Empty
                : settingsOffsiteStatus.Text ?? string.Empty;
            offsite["detail"] = settingsOffsiteDetail == null
                ? string.Empty
                : settingsOffsiteDetail.Text ?? string.Empty;
            offsite["evidence"] = settingsOffsiteEvidence == null
                ? string.Empty
                : settingsOffsiteEvidence.Text ?? string.Empty;
            return offsite;
        }

        private Dictionary<string, object> BuildWebRecoveryState()
        {
            RepositoryRecoveryStatus status = repositoryRecoveryStatus ?? RepositoryRecoveryStatus.None();
            Dictionary<string, object> recovery = new Dictionary<string, object>();
            string readinessTitle = restoreReadinessTitle == null
                ? string.Empty
                : restoreReadinessTitle.Text ?? string.Empty;
            string readinessDetail = restoreReadinessDetail == null
                ? string.Empty
                : restoreReadinessDetail.Text ?? string.Empty;
            recovery["title"] = string.IsNullOrWhiteSpace(readinessTitle)
                ? (status.Exists ? "Repository repair required" : "Repository recovery clear")
                : readinessTitle;
            recovery["detail"] = string.IsNullOrWhiteSpace(readinessDetail)
                ? status.Message ?? string.Empty
                : readinessDetail;
            recovery["repairNeeded"] = status.Exists;
            recovery["repairMessage"] = protectionRepositoryRecoveryMessage == null
                ? string.Empty
                : protectionRepositoryRecoveryMessage.Text ?? string.Empty;
            recovery["repairState"] = protectionRepositoryRecoveryState == null
                ? string.Empty
                : protectionRepositoryRecoveryState.Text ?? string.Empty;
            recovery["repairBusy"] = repositoryRecoveryInProgress;
            recovery["repairPercent"] = repositoryRecoveryOperationPercent.HasValue
                ? (object)repositoryRecoveryOperationPercent.Value
                : null;
            return recovery;
        }

        private Dictionary<string, object> BuildWebActionStates()
        {
            Dictionary<string, object> actions = new Dictionary<string, object>();
            actions["togglePreview"] = BuildWebActionState(previewButton);
            actions["backupNow"] = BuildWebActionState(backupNowButton);
            actions["cancelBackup"] = BuildWebActionState(cancelBackupButton);
            actions["addSource"] = BuildWebActionState(addSourceButton);
            actions["retrySourceChange"] = BuildWebActionState(sourceOperationRetryButton);
            actions["dismissSourceChange"] = BuildWebActionState(sourceOperationDismissButton);
            actions["editSchedule"] = BuildWebActionState(editScheduleButton);
            actions["changeRepository"] = BuildWebActionState(changeRepositoryButton);
            actions["repairRepository"] = BuildWebActionState(
                protectionRepositoryRecoveryButton != null && protectionRepositoryRecoveryButton.IsEnabled
                    ? protectionRepositoryRecoveryButton
                    : settingsRepositoryRecoveryButton);
            actions["reviewChanges"] = BuildWebActionState(reviewChangesButton);
            actions["openRestore"] = BuildWebActionState(openRestoreCenterButton);
            actions["checkReadiness"] = BuildWebActionState(checkRecoveryReadinessButton);
            actions["viewRunDetails"] = BuildWebActionState(viewRunDetailsButton);
            actions["exportDiagnostics"] = BuildWebActionState(exportDiagnosticsButton);
            return actions;
        }

        private Dictionary<string, object> BuildWebActionState(Button button)
        {
            Dictionary<string, object> action = new Dictionary<string, object>();
            action["enabled"] = button != null && button.IsEnabled;
            action["visible"] = button != null && button.Visibility == Visibility.Visible;
            action["label"] = button == null || button.Content == null
                ? string.Empty
                : button.Content.ToString();
            object help = button == null ? null : button.ToolTip;
            action["help"] = help == null ? string.Empty : help.ToString();
            return action;
        }

        private void ShowWebPresentationFailure(string message)
        {
            Action display = delegate
            {
                if (webPresentationClosing || webPresentationErrorSurface == null)
                {
                    return;
                }
                webPresentationErrorText.Text = message ?? "The BeautifulUI web surface is unavailable.";
                webPresentationErrorSurface.Visibility = Visibility.Visible;
            };
            if (Dispatcher.CheckAccess())
            {
                display();
            }
            else if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
            {
                Dispatcher.BeginInvoke(display);
            }
        }

        private void DisposeWebPresentation()
        {
            webPresentationClosing = true;
            webPresentationReady = false;
            webPresentationHandshakeReady = false;
            webPresentationConfigured = false;
            webPresentationPendingState = null;

            WebView2 view = webPresentation;
            webPresentation = null;
            if (view == null)
            {
                return;
            }

            view.Loaded -= OnWebPresentationLoaded;
            try
            {
                view.Dispose();
            }
            catch
            {
                // The WebView2 controller may already be shutting down with the window.
            }
        }

        private static bool IsAllowedWebUri(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri))
            {
                return false;
            }
            return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(uri.Host, WebHostName, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrEmpty(uri.UserInfo) &&
                (uri.Port == -1 || uri.Port == 443);
        }
    }
}
