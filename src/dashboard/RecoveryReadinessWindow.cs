using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ResticBackuper.Dashboard
{
    internal sealed class RecoveryReadinessWindow : Window
    {
        private readonly SourceConfiguration configuration;
        private readonly DashboardThemePalette palette;
        private TextBlock headline;
        private TextBlock detail;
        private TextBlock repositoryValue;
        private TextBlock capacityValue;
        private TextBlock lockValue;
        private DataGrid checksGrid;
        private Button refreshButton;
        private Button repairButton;
        private Button lockRepairButton;
        private Button keyRotationButton;
        private Button drillButton;
        private Button closeButton;
        private ProgressBar progress;
        private bool inspecting;
        private bool repairEligible;
        private bool lockRepairEligible;
        private bool keyRotationEligible;
        private bool drillEligible;

        internal RecoveryReadinessWindow(
            SourceConfiguration configuration,
            DashboardThemePalette palette)
        {
            if (configuration == null) throw new ArgumentNullException("configuration");
            if (palette == null) throw new ArgumentNullException("palette");
            this.configuration = configuration;
            this.palette = palette;
            Title = "Recovery Readiness";
            Width = 1160;
            Height = 650;
            MinWidth = 900;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            DashboardVisualStyle.ApplyWindow(this, palette);
            Content = BuildInterface();
            Loaded += async delegate { await RefreshHealth(); };
            Closing += OnClosing;
        }

        private UIElement BuildInterface()
        {
            Grid root = new Grid();
            root.Margin = new Thickness(22);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel headerCopy = new StackPanel();
            TextBlock title = Text("Recovery readiness", 25, palette.TextPrimary, FontWeights.SemiBold);
            headerCopy.Children.Add(title);
            headline = Text("Preparing independent checks", 15, palette.TextSecondary, FontWeights.SemiBold);
            headline.Margin = new Thickness(0, 8, 0, 0);
            headerCopy.Children.Add(headline);
            detail = Text(
                "The repository, active credential, recovery key, recovery bundle, capacity, locks, and restore drill are checked separately.",
                12,
                palette.TextSecondary,
                FontWeights.Normal);
            detail.TextWrapping = TextWrapping.Wrap;
            detail.Margin = new Thickness(0, 3, 18, 0);
            headerCopy.Children.Add(detail);
            header.Children.Add(headerCopy);
            refreshButton = Button("Run checks again", true);
            refreshButton.Click += async delegate { await RefreshHealth(); };
            AutomationProperties.SetName(refreshButton, "Run recovery readiness checks again");
            Grid.SetColumn(refreshButton, 1);
            header.Children.Add(refreshButton);
            root.Children.Add(header);

            Grid facts = new Grid();
            facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            repositoryValue = Text("Not checked", 14, palette.TextPrimary, FontWeights.SemiBold);
            capacityValue = Text("Not checked", 14, palette.TextPrimary, FontWeights.SemiBold);
            lockValue = Text("Not checked", 14, palette.TextPrimary, FontWeights.SemiBold);
            AddFact(facts, 0, "REPOSITORY", repositoryValue);
            AddFact(facts, 2, "CAPACITY", capacityValue);
            AddFact(facts, 4, "LOCKS", lockValue);
            Grid.SetRow(facts, 2);
            root.Children.Add(facts);

            Border checksCard = Card();
            checksCard.Padding = new Thickness(12);
            checksGrid = new DataGrid();
            checksGrid.AutoGenerateColumns = false;
            checksGrid.IsReadOnly = true;
            checksGrid.CanUserAddRows = false;
            checksGrid.CanUserDeleteRows = false;
            checksGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
            checksGrid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            checksGrid.HorizontalGridLinesBrush = palette.GridLine;
            checksGrid.VerticalGridLinesBrush = Brushes.Transparent;
            checksGrid.Background = Brushes.Transparent;
            checksGrid.Foreground = palette.TableText;
            checksGrid.RowBackground = palette.RowEven;
            checksGrid.AlternatingRowBackground = palette.AlternatingRow;
            checksGrid.SelectionMode = DataGridSelectionMode.Single;
            DashboardVisualStyle.ApplyDataGridChrome(checksGrid, palette);
            checksGrid.Columns.Add(Column("STATUS", "StatusDisplay", 1.0));
            checksGrid.Columns.Add(Column("CHECK", "Summary", 2.4));
            checksGrid.Columns.Add(Column("DETAIL", "Detail", 3.2));
            AutomationProperties.SetName(checksGrid, "Recovery readiness check results");
            checksCard.Child = checksGrid;
            Grid.SetRow(checksCard, 4);
            root.Children.Add(checksCard);

            Grid footer = new Grid();
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            progress = new ProgressBar();
            progress.Width = 260;
            progress.Height = 5;
            progress.IsIndeterminate = true;
            progress.Visibility = Visibility.Collapsed;
            progress.Background = palette.ProgressTrack;
            progress.Foreground = palette.AccentPrimary;
            progress.VerticalAlignment = VerticalAlignment.Center;
            footer.Children.Add(progress);
            drillButton = Button("Run restore drill", false);
            drillButton.Margin = new Thickness(0, 0, 8, 0);
            drillButton.IsEnabled = false;
            drillButton.Click += async delegate { await RunRestoreDrill(); };
            AutomationProperties.SetName(
                drillButton,
                "Run a recovery-key restore drill of a bounded representative sample");
            AutomationProperties.SetHelpText(
                drillButton,
                "Restores the latest verified plan-bound snapshot into a new protected folder, verifies the canary and a bounded ordinary-data sample, then records readiness evidence.");
            Grid.SetColumn(drillButton, 1);
            footer.Children.Add(drillButton);
            repairButton = Button("Repair active credential", false);
            repairButton.Margin = new Thickness(0, 0, 8, 0);
            repairButton.IsEnabled = false;
            repairButton.Click += async delegate { await RepairCredential(); };
            AutomationProperties.SetName(repairButton, "Repair active credential from configured recovery key");
            AutomationProperties.SetHelpText(
                repairButton,
                "Available only when the recovery key works but the active CurrentUser credential does not.");
            Grid.SetColumn(repairButton, 2);
            footer.Children.Add(repairButton);
            lockRepairButton = Button("Repair stale locks", false);
            lockRepairButton.Margin = new Thickness(0, 0, 8, 0);
            lockRepairButton.IsEnabled = false;
            lockRepairButton.Click += async delegate { await RepairStaleLocks(); };
            AutomationProperties.SetName(lockRepairButton, "Repair repository locks classified as stale");
            AutomationProperties.SetHelpText(
                lockRepairButton,
                "Available only when repository locks are present and the active credential works.");
            Grid.SetColumn(lockRepairButton, 3);
            footer.Children.Add(lockRepairButton);
            keyRotationButton = Button("Rotate / recover key", false);
            keyRotationButton.Margin = new Thickness(0, 0, 8, 0);
            keyRotationButton.IsEnabled = false;
            keyRotationButton.Click += async delegate { await RotateKey(); };
            AutomationProperties.SetName(keyRotationButton, "Rotate repository and recovery credentials safely");
            AutomationProperties.SetHelpText(
                keyRotationButton,
                "Adds and verifies a new key first, retains the prior repository and recovery keys, and resumes an interrupted rotation journal.");
            Grid.SetColumn(keyRotationButton, 4);
            footer.Children.Add(keyRotationButton);
            closeButton = Button("Close", false);
            closeButton.Click += delegate { Close(); };
            Grid.SetColumn(closeButton, 5);
            footer.Children.Add(closeButton);
            Grid.SetRow(footer, 6);
            root.Children.Add(footer);
            AutomationProperties.SetName(root, "Independent repository and recovery readiness report");
            return root;
        }

        private async Task RefreshHealth()
        {
            if (inspecting) return;
            SetInspecting(true);
            RecoveryHealthReport report;
            try
            {
                report = await Task.Run(delegate { return RecoveryHealthLauncher.Inspect(configuration); });
            }
            catch (Exception error)
            {
                report = RecoveryHealthReport.Failure(error.Message);
            }
            ApplyReport(report);
            SetInspecting(false);
        }

        private void ApplyReport(RecoveryHealthReport report)
        {
            if (report == null || !report.Loaded)
            {
                headline.Text = "Readiness inspection unavailable";
                headline.Foreground = palette.Danger;
                detail.Text = report == null ? "No health report was returned." : report.ErrorMessage;
                checksGrid.ItemsSource = null;
                repositoryValue.Text = "Unavailable";
                capacityValue.Text = "Unavailable";
                lockValue.Text = "Unavailable";
                repairEligible = false;
                repairButton.IsEnabled = false;
                lockRepairEligible = false;
                lockRepairButton.IsEnabled = false;
                keyRotationEligible = false;
                keyRotationButton.IsEnabled = false;
                drillEligible = false;
                drillButton.IsEnabled = false;
                Announce();
                return;
            }
            if (report.OverallStatus == "healthy")
            {
                headline.Text = "Recovery path independently verified";
                headline.Foreground = palette.Success;
                detail.Text = "All available readiness checks passed without changing the repository.";
            }
            else if (report.OverallStatus == "warning")
            {
                headline.Text = "Recovery is available, with items to review";
                headline.Foreground = palette.Warning;
                detail.Text = report.WarningCheckCount.ToString(CultureInfo.CurrentCulture) +
                    " readiness check(s) need review.";
            }
            else
            {
                headline.Text = "Recovery readiness needs attention";
                headline.Foreground = palette.Danger;
                detail.Text = report.FailedCheckCount.ToString(CultureInfo.CurrentCulture) +
                    " independent check(s) failed. Review each result before relying on recovery.";
            }
            repositoryValue.Text = string.IsNullOrWhiteSpace(report.RepositoryId)
                ? "ID unavailable"
                : "Format " + report.RepositoryFormat.ToString(CultureInfo.CurrentCulture) +
                    " · " + report.RepositoryId.Substring(0, Math.Min(10, report.RepositoryId.Length));
            capacityValue.Text = report.FreeBytes.HasValue
                ? FormatBytes(report.FreeBytes.Value) + " free · " + FormatBytes(report.ReserveBytes) + " reserve"
                : "Unavailable";
            lockValue.Text = report.ActiveLockCount.HasValue
                ? (report.ActiveLockCount.Value == 0
                    ? "No locks"
                    : report.ActiveLockCount.Value.ToString(CultureInfo.CurrentCulture) + " require review")
                : "Unavailable";
            checksGrid.ItemsSource = report.Checks;
            repairEligible = CheckPassed(report, "recovery_key") &&
                !CheckPassed(report, "active_credential");
            repairButton.IsEnabled = repairEligible && !inspecting;
            lockRepairEligible = report.ActiveLockCount.HasValue &&
                report.ActiveLockCount.Value > 0 && CheckPassed(report, "active_credential");
            lockRepairButton.IsEnabled = lockRepairEligible && !inspecting;
            bool rotationJournalPending = CheckDetailContains(
                report,
                "pending_transaction",
                "credential-rotation.journal.json");
            keyRotationEligible = CheckPassed(report, "active_credential") &&
                CheckPassed(report, "recovery_key") &&
                report.ActiveLockCount.HasValue && report.ActiveLockCount.Value == 0 &&
                (CheckPassed(report, "pending_transaction") || rotationJournalPending);
            keyRotationButton.IsEnabled = keyRotationEligible && !inspecting;
            keyRotationButton.Content = rotationJournalPending
                ? "Resume key rotation"
                : "Rotate keys";
            AutomationProperties.SetHelpText(
                repairButton,
                repairEligible
                    ? "Use the independently verified recovery key to replace only the broken CurrentUser DPAPI envelope."
                    : "Repair is available only when the recovery key passes and the active credential fails.");
            AutomationProperties.SetHelpText(
                lockRepairButton,
                lockRepairEligible
                    ? "After Windows approval, block active backup work and ask Restic to remove only locks it classifies as stale."
                    : "Stale-lock repair is available only when locks are present and the active credential passes.");
            AutomationProperties.SetHelpText(
                keyRotationButton,
                rotationJournalPending
                    ? "Resume and verify the protected interrupted key-rotation transaction."
                    : keyRotationEligible
                        ? "Add and prove a new repository key, publish matching active and recovery credentials, and retain the prior key for rollback."
                        : "Key rotation requires both credentials, no repository locks, and no unrelated pending transaction.");
            drillEligible = CheckNeedsReview(report, "restore_drill") &&
                CheckPassed(report, "recovery_key") &&
                CheckPassed(report, "recovery_bundle") &&
                CheckPassed(report, "last_verification") &&
                CheckPassed(report, "pending_transaction") &&
                report.ActiveLockCount.HasValue && report.ActiveLockCount.Value == 0;
            drillButton.IsEnabled = drillEligible && !inspecting;
            Announce();
        }

        private async Task RunRestoreDrill()
        {
            if (inspecting || !drillEligible) return;
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                "Run a real recovery-key restore drill now?\n\n" +
                "The protected manager will choose the latest verified snapshot, create a new nonce-bound folder under ProgramData, restore the protected canary plus a bounded ordinary-data sample, verify every restored file, and record the evidence.\n\n" +
                "It never overwrites or deletes sources, repository data, keys, snapshots, or an existing restore folder. The restored sample is retained for inspection.",
                "Run restore drill",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);
            if (confirmation != MessageBoxResult.OK) return;

            SetInspecting(true);
            headline.Text = "Waiting for Windows approval";
            headline.Foreground = palette.TextPrimary;
            detail.Text = "The recovery key stays inside the elevated protected restore manager.";
            Announce();
            RestoreManagerResult result;
            try
            {
                result = await Task.Run(delegate
                {
                    return RestoreManagerLauncher.RunRecoveryDrill(
                        configuration,
                        delegate
                        {
                            Dispatcher.BeginInvoke(new Action(delegate
                            {
                                headline.Text = "Recovery drill approved";
                                detail.Text = "Restoring and independently verifying the bounded sample.";
                                Announce();
                            }));
                        },
                        delegate(RestoreManagerProgress value)
                        {
                            Dispatcher.BeginInvoke(new Action(delegate
                            {
                                detail.Text = value.Message;
                                Announce();
                            }));
                        });
                });
            }
            catch (Exception error)
            {
                result = RestoreManagerResult.Failure(error.Message, -1);
            }
            if (result.UserCancelled)
            {
                headline.Text = "Windows approval cancelled";
                headline.Foreground = palette.Warning;
                detail.Text = "No restore was started and no repository or protected evidence changed.";
                SetInspecting(false);
                Announce();
                return;
            }
            if (!result.Succeeded)
            {
                headline.Text = result.Report != null && result.Report.Verified
                    ? "Restore verified; evidence recording failed"
                    : "Recovery drill did not complete";
                headline.Foreground = palette.Warning;
                detail.Text = (result.ErrorMessage ?? "The protected recovery drill failed.") +
                    (result.Report == null || string.IsNullOrWhiteSpace(result.Report.Target)
                        ? string.Empty
                        : " Retained target: " + result.Report.Target);
                SetInspecting(false);
                Announce();
                return;
            }

            headline.Text = "Recovery drill verified and recorded";
            headline.Foreground = palette.Success;
            detail.Text = "Retained protected sample: " + result.Report.Target;
            Announce();
            RecoveryHealthReport health;
            try
            {
                health = await Task.Run(delegate { return RecoveryHealthLauncher.Inspect(configuration); });
            }
            catch (Exception error)
            {
                health = RecoveryHealthReport.Failure(error.Message);
            }
            string retainedTarget = result.Report.Target;
            ApplyReport(health);
            detail.Text = detail.Text + " Retained protected sample: " + retainedTarget;
            SetInspecting(false);
            Announce();
        }

        private async Task RepairCredential()
        {
            if (inspecting || !repairEligible) return;
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                "Repair the active Windows credential from the configured recovery key?\n\n" +
                "This replaces only the CurrentUser DPAPI envelope after authenticating the recovery key. " +
                "It does not change the repository, repository keys, snapshots, or recovery-key file.",
                "Repair active credential",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.OK) return;
            SetInspecting(true);
            headline.Text = "Waiting for Windows approval";
            detail.Text = "Credential repair is protected and uses the shared backup-operation lock.";
            Announce();
            CredentialRepairResult result;
            try
            {
                result = await Task.Run(delegate
                {
                    return CredentialRepairLauncher.Repair(
                        configuration,
                        delegate
                        {
                            Dispatcher.BeginInvoke(new Action(delegate
                            {
                                headline.Text = "Credential repair approved";
                                detail.Text = "Authenticating the recovery key before replacing the active envelope.";
                                Announce();
                            }));
                        });
                });
            }
            catch (Exception error)
            {
                result = CredentialRepairResult.Failure(error.Message, null);
            }
            if (result.UserCancelled)
            {
                headline.Text = "Windows approval cancelled";
                detail.Text = "No credential or repository data was changed.";
                headline.Foreground = palette.Warning;
                SetInspecting(false);
                Announce();
                return;
            }
            if (result.Succeeded && result.Health != null)
            {
                ApplyReport(result.Health);
                headline.Text = "Active credential repaired and independently verified";
                headline.Foreground = palette.Success;
                detail.Text = "The recovery key and the replacement CurrentUser credential both unlock the same repository.";
                SetInspecting(false);
                Announce();
                return;
            }
            if (result.Health != null && result.Health.Loaded)
            {
                ApplyReport(result.Health);
            }
            else
            {
                headline.Text = "Credential repair did not complete";
                headline.Foreground = palette.Danger;
                detail.Text = result.ErrorMessage;
            }
            SetInspecting(false);
            Announce();
        }

        private async Task RepairStaleLocks()
        {
            if (inspecting || !lockRepairEligible) return;
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                "Ask Restic to remove locks it classifies as stale?\n\n" +
                "The protected helper first acquires the shared operation lock and refuses to continue while any Restic process is running. " +
                "It never uses --remove-all and does not change snapshots or repository data.",
                "Repair stale repository locks",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.OK) return;
            SetInspecting(true);
            headline.Text = "Waiting for Windows approval";
            detail.Text = "No lock will be removed until activity checks pass.";
            Announce();
            StaleLockRepairResult result;
            try
            {
                result = await Task.Run(delegate
                {
                    return StaleLockRepairLauncher.Repair(
                        configuration,
                        delegate
                        {
                            Dispatcher.BeginInvoke(new Action(delegate
                            {
                                headline.Text = "Stale-lock repair approved";
                                detail.Text = "Proving protected operations and Restic processes are inactive.";
                                Announce();
                            }));
                        });
                });
            }
            catch (Exception error)
            {
                result = StaleLockRepairResult.Failure(error.Message, null);
            }
            if (result.UserCancelled)
            {
                headline.Text = "Windows approval cancelled";
                headline.Foreground = palette.Warning;
                detail.Text = "No repository lock or data was changed.";
                SetInspecting(false);
                Announce();
                return;
            }
            if (result.Health != null && result.Health.Loaded)
            {
                ApplyReport(result.Health);
            }
            if (result.Succeeded)
            {
                headline.Text = "Stale repository locks repaired";
                headline.Foreground = palette.Success;
                detail.Text = "A fresh read-only inspection confirms that no repository locks remain.";
            }
            else
            {
                headline.Text = "Repository locks were not fully repaired";
                headline.Foreground = palette.Warning;
                detail.Text = result.ErrorMessage;
            }
            SetInspecting(false);
            Announce();
        }

        private async Task RotateKey()
        {
            if (inspecting || !keyRotationEligible) return;
            bool resuming = Convert.ToString(keyRotationButton.Content, CultureInfo.InvariantCulture)
                .IndexOf("Resume", StringComparison.OrdinalIgnoreCase) >= 0;
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                resuming
                    ? "Resume the interrupted protected key rotation?\n\nThe helper will authenticate the staged key and either complete the safe publish or restore the known-good prior state."
                    : "Rotate repository access and the configured recovery key?\n\n" +
                      "A new Restic key is added and authenticated before local credentials change. " +
                      "The previous repository key and a restricted previous recovery-key file are retained for rollback. " +
                      "No snapshot or backup data is deleted. Store the new recovery key away from this computer after rotation.",
                resuming ? "Resume key rotation" : "Rotate recovery credentials",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.OK) return;
            SetInspecting(true);
            headline.Text = "Waiting for Windows approval";
            detail.Text = resuming
                ? "The protected journal will be validated before recovery continues."
                : "The prior credential remains active until Restic proves the new repository key.";
            Announce();
            KeyRotationResult result;
            try
            {
                result = await Task.Run(delegate
                {
                    return KeyRotationLauncher.Rotate(
                        configuration,
                        delegate
                        {
                            Dispatcher.BeginInvoke(new Action(delegate
                            {
                                headline.Text = resuming
                                    ? "Key-rotation recovery approved"
                                    : "Key rotation approved";
                                detail.Text = "Holding the shared operation lock while repository and recovery credentials are independently verified.";
                                Announce();
                            }));
                        });
                });
            }
            catch (Exception error)
            {
                result = KeyRotationResult.Failure(error.Message, null);
            }
            if (result.UserCancelled)
            {
                headline.Text = "Windows approval cancelled";
                headline.Foreground = palette.Warning;
                detail.Text = "No key rotation was started.";
                SetInspecting(false);
                Announce();
                return;
            }
            if (result.Health != null && result.Health.Loaded) ApplyReport(result.Health);
            if (result.Succeeded)
            {
                headline.Text = "New and rollback credentials independently verified";
                headline.Foreground = palette.Success;
                detail.Text = "The new active and recovery credentials work. The previous recovery file remains beside the configured key and the prior Restic key was not retired.";
            }
            else
            {
                headline.Text = "Key rotation needs attention";
                headline.Foreground = palette.Warning;
                detail.Text = result.ErrorMessage;
            }
            SetInspecting(false);
            Announce();
        }

        private void Announce()
        {
            AutomationProperties.SetName(headline, headline.Text + ". " + detail.Text);
            AutomationProperties.SetLiveSetting(
                headline,
                headline.Foreground == palette.Danger
                    ? AutomationLiveSetting.Assertive
                    : AutomationLiveSetting.Polite);
        }

        private void SetInspecting(bool value)
        {
            inspecting = value;
            refreshButton.IsEnabled = !value;
            repairButton.IsEnabled = !value && repairEligible;
            lockRepairButton.IsEnabled = !value && lockRepairEligible;
            keyRotationButton.IsEnabled = !value && keyRotationEligible;
            drillButton.IsEnabled = !value && drillEligible;
            closeButton.IsEnabled = !value;
            progress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (value)
            {
                headline.Text = "Running independent checks";
                headline.Foreground = palette.TextPrimary;
                detail.Text = "Authenticating both recovery paths and validating protected artifacts.";
                Announce();
            }
        }

        private void OnClosing(object sender, CancelEventArgs args)
        {
            if (!inspecting) return;
            args.Cancel = true;
            headline.Text = "Readiness checks still running";
            detail.Text = "Keep this window open until the read-only inspection finishes.";
            Announce();
        }

        private void AddFact(Grid grid, int column, string label, TextBlock value)
        {
            Border card = Card();
            card.Padding = new Thickness(14, 12, 14, 12);
            StackPanel copy = new StackPanel();
            copy.Children.Add(Text(label, 10, palette.AccentInfoText, FontWeights.Bold));
            value.Margin = new Thickness(0, 6, 0, 0);
            value.TextWrapping = TextWrapping.Wrap;
            copy.Children.Add(value);
            card.Child = copy;
            Grid.SetColumn(card, column);
            grid.Children.Add(card);
        }

        private Border Card()
        {
            return new Border
            {
                Background = palette.Surface,
                BorderBrush = palette.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10)
            };
        }

        private Button Button(string text, bool primary)
        {
            Button button = new Button();
            button.Content = text;
            button.Padding = new Thickness(14, 8, 14, 8);
            button.MinHeight = 36;
            button.Background = primary ? palette.AccentInfo : palette.ButtonBackground;
            button.Foreground = primary ? palette.TextOnAccent : palette.ButtonText;
            button.BorderBrush = primary ? palette.AccentInfo : palette.Border;
            button.BorderThickness = new Thickness(1);
            button.FontSize = 12.5;
            button.FontWeight = FontWeights.Medium;
            ToolTipService.SetShowOnDisabled(button, true);
            DashboardVisualStyle.ApplyButtonChrome(button, 8);
            DashboardVisualStyle.ApplyFocusOutline(button, palette.Focus);
            AutomationProperties.SetName(button, text);
            return button;
        }

        private static TextBlock Text(string value, double size, Brush brush, FontWeight weight)
        {
            return new TextBlock
            {
                Text = value,
                FontSize = size,
                Foreground = brush,
                FontWeight = weight
            };
        }

        private static DataGridTextColumn Column(string header, string property, double width)
        {
            return new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(property),
                Width = new DataGridLength(width, DataGridLengthUnitType.Star)
            };
        }

        private static string FormatBytes(long value)
        {
            string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
            double amount = Math.Max(0, value);
            int index = 0;
            while (amount >= 1024 && index < units.Length - 1)
            {
                amount /= 1024;
                index++;
            }
            return amount.ToString(index == 0 ? "0" : "0.0", CultureInfo.CurrentCulture) + " " + units[index];
        }

        private static bool CheckPassed(RecoveryHealthReport report, string id)
        {
            if (report == null || report.Checks == null) return false;
            foreach (RecoveryHealthCheck check in report.Checks)
            {
                if (check.Id == id && check.Status == "pass") return true;
            }
            return false;
        }

        private static bool CheckNeedsReview(RecoveryHealthReport report, string id)
        {
            if (report == null || report.Checks == null) return false;
            foreach (RecoveryHealthCheck check in report.Checks)
            {
                if (check.Id == id && check.Status == "warn") return true;
            }
            return false;
        }

        private static bool CheckDetailContains(
            RecoveryHealthReport report,
            string id,
            string value)
        {
            if (report == null || report.Checks == null) return false;
            foreach (RecoveryHealthCheck check in report.Checks)
            {
                if (check.Id == id && !string.IsNullOrEmpty(check.Detail) &&
                    check.Detail.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
