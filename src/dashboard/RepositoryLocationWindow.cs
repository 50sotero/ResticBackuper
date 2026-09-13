using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ResticBackuper.Dashboard
{
    internal enum RepositoryLocationStage
    {
        Selecting,
        Reviewing,
        WaitingForApproval,
        Copying,
        Verifying,
        Activating,
        Complete,
        Failed
    }

    internal sealed class RepositoryLocationWindow : Window
    {
        private readonly SourceConfiguration configuration;
        private readonly DashboardThemePalette palette;
        private readonly IDictionary<RepositoryLocationStage, TextBlock> stageItems =
            new Dictionary<RepositoryLocationStage, TextBlock>();
        private TextBlock stageBadge;
        private TextBlock headline;
        private TextBlock detail;
        private TextBlock currentPathValue;
        private TextBlock newPathValue;
        private TextBlock capacityValue;
        private TextBlock progressMetrics;
        private ProgressBar progressBar;
        private Button chooseButton;
        private Button primaryButton;
        private Button closeButton;
        private string selectedRepository;
        private bool operationStarted;
        private int estimateGeneration;

        internal bool RepositoryChanged { get; private set; }

        internal RepositoryLocationWindow(
            SourceConfiguration configuration,
            DashboardThemePalette palette)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException("configuration");
            }
            if (palette == null)
            {
                throw new ArgumentNullException("palette");
            }
            this.configuration = configuration;
            this.palette = palette;

            Title = "Change backup location";
            Width = 780;
            Height = 640;
            MinWidth = 680;
            MinHeight = 580;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            DashboardVisualStyle.ApplyWindow(this, palette);
            Content = BuildContent();
            Loaded += delegate
            {
                Dispatcher.BeginInvoke(
                    new Action(SelectDestination),
                    DispatcherPriority.Background);
            };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs args)
            {
                if (operationStarted)
                {
                    args.Cancel = true;
                }
            };
            AutomationProperties.SetName(this, "Change protected Restic repository location");
        }

        private UIElement BuildContent()
        {
            Grid shell = new Grid();
            shell.Margin = new Thickness(24);
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel heading = new StackPanel();
            stageBadge = new TextBlock();
            stageBadge.Text = "SELECTING";
            stageBadge.FontSize = 11;
            stageBadge.FontWeight = FontWeights.Bold;
            stageBadge.Foreground = palette.AccentInfoText;
            heading.Children.Add(stageBadge);
            headline = new TextBlock();
            headline.Text = "Choose where future backups are stored";
            headline.FontSize = 24;
            headline.FontWeight = FontWeights.SemiBold;
            headline.Foreground = palette.TextPrimary;
            headline.Margin = new Thickness(0, 5, 0, 0);
            heading.Children.Add(headline);
            detail = new TextBlock();
            detail.Text = "Select a folder, then review the copy before Windows asks for approval.";
            detail.FontSize = 13;
            detail.Foreground = palette.TextSecondary;
            detail.Margin = new Thickness(0, 5, 0, 0);
            detail.TextWrapping = TextWrapping.Wrap;
            AutomationProperties.SetLiveSetting(detail, AutomationLiveSetting.Polite);
            heading.Children.Add(detail);
            shell.Children.Add(heading);

            Grid body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(body, 2);
            shell.Children.Add(body);

            Border stageCard = CreateCard();
            stageCard.Padding = new Thickness(16, 15, 16, 15);
            StackPanel stages = new StackPanel();
            AddStage(stages, RepositoryLocationStage.Selecting, "Selecting");
            AddStage(stages, RepositoryLocationStage.Reviewing, "Reviewing");
            AddStage(stages, RepositoryLocationStage.WaitingForApproval, "Waiting for approval");
            AddStage(stages, RepositoryLocationStage.Copying, "Copying");
            AddStage(stages, RepositoryLocationStage.Verifying, "Verifying");
            AddStage(stages, RepositoryLocationStage.Activating, "Activating");
            AddStage(stages, RepositoryLocationStage.Complete, "Complete");
            AddStage(stages, RepositoryLocationStage.Failed, "Failed");
            stageCard.Child = stages;
            body.Children.Add(stageCard);

            Border reviewCard = CreateCard();
            reviewCard.Padding = new Thickness(18, 16, 18, 16);
            Grid.SetColumn(reviewCard, 2);
            StackPanel review = new StackPanel();
            review.Children.Add(BuildPathField("Current repository", configuration.RepositoryPath, out currentPathValue));
            FrameworkElement newField = BuildPathField("New repository", "No folder selected", out newPathValue);
            newField.Margin = new Thickness(0, 14, 0, 0);
            review.Children.Add(newField);

            capacityValue = new TextBlock();
            capacityValue.Text = "Storage estimate appears after you choose a folder.";
            capacityValue.FontSize = 12;
            capacityValue.Foreground = palette.TextSecondary;
            capacityValue.TextWrapping = TextWrapping.Wrap;
            capacityValue.Margin = new Thickness(0, 14, 0, 0);
            review.Children.Add(capacityValue);

            Border safety = new Border();
            safety.Background = palette.SafetyBackground;
            safety.BorderBrush = palette.SafetyBorder;
            safety.BorderThickness = new Thickness(1);
            safety.CornerRadius = new CornerRadius(8);
            safety.Padding = new Thickness(12, 10, 12, 10);
            safety.Margin = new Thickness(0, 16, 0, 0);
            TextBlock safetyText = new TextBlock();
            safetyText.Text = "The repository is copied, verified, and only then activated. " +
                "The old repository is kept untouched for recovery. This does not start a backup.";
            safetyText.FontSize = 12;
            safetyText.Foreground = palette.SafetyText;
            safetyText.TextWrapping = TextWrapping.Wrap;
            safety.Child = safetyText;
            review.Children.Add(safety);

            progressBar = new ProgressBar();
            progressBar.Height = 8;
            progressBar.Minimum = 0;
            progressBar.Maximum = 100;
            progressBar.Value = 0;
            progressBar.Visibility = Visibility.Collapsed;
            progressBar.Margin = new Thickness(0, 18, 0, 0);
            review.Children.Add(progressBar);
            progressMetrics = new TextBlock();
            progressMetrics.FontSize = 11;
            progressMetrics.Foreground = palette.TextSecondary;
            progressMetrics.Margin = new Thickness(0, 7, 0, 0);
            progressMetrics.TextWrapping = TextWrapping.Wrap;
            progressMetrics.Visibility = Visibility.Collapsed;
            AutomationProperties.SetLiveSetting(progressMetrics, AutomationLiveSetting.Polite);
            review.Children.Add(progressMetrics);
            reviewCard.Child = review;
            body.Children.Add(reviewCard);

            Grid actions = new Grid();
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            chooseButton = CreateButton("Choose another folder");
            chooseButton.Click += delegate { SelectDestination(); };
            AutomationProperties.SetAutomationId(chooseButton, "ChooseRepositoryFolderButton");
            AutomationProperties.SetHelpText(
                chooseButton,
                "Choose the exact folder that will contain the copied Restic repository.");
            actions.Children.Add(chooseButton);

            closeButton = CreateButton("Cancel");
            closeButton.MinWidth = 86;
            closeButton.Click += delegate { DialogResult = false; };
            Grid.SetColumn(closeButton, 2);
            actions.Children.Add(closeButton);

            primaryButton = CreateButton("Review a folder first");
            primaryButton.MinWidth = 152;
            primaryButton.Margin = new Thickness(10, 0, 0, 0);
            primaryButton.Background = palette.AccentInfo;
            primaryButton.BorderBrush = palette.AccentInfo;
            primaryButton.Foreground = palette.TextOnAccent;
            primaryButton.IsEnabled = false;
            primaryButton.Click += OnPrimaryClick;
            AutomationProperties.SetAutomationId(primaryButton, "ConfirmRepositoryRelocationButton");
            AutomationProperties.SetHelpText(
                primaryButton,
                "Copy, verify, and activate the reviewed repository location. Windows approval is required.");
            Grid.SetColumn(primaryButton, 3);
            actions.Children.Add(primaryButton);
            Grid.SetRow(actions, 4);
            shell.Children.Add(actions);

            SetStage(RepositoryLocationStage.Selecting, null);
            return shell;
        }

        private FrameworkElement BuildPathField(
            string label,
            string value,
            out TextBlock valueBlock)
        {
            StackPanel field = new StackPanel();
            TextBlock caption = new TextBlock();
            caption.Text = label;
            caption.FontSize = 11;
            caption.FontWeight = FontWeights.SemiBold;
            caption.Foreground = palette.TextSecondary;
            field.Children.Add(caption);
            valueBlock = new TextBlock();
            valueBlock.Text = value;
            valueBlock.FontSize = 13;
            valueBlock.Foreground = palette.TextPrimary;
            valueBlock.TextWrapping = TextWrapping.Wrap;
            valueBlock.Margin = new Thickness(0, 3, 0, 0);
            field.Children.Add(valueBlock);
            return field;
        }

        private void AddStage(Panel parent, RepositoryLocationStage stage, string label)
        {
            TextBlock item = new TextBlock();
            item.Text = "○  " + label;
            item.FontSize = 12;
            item.Foreground = palette.TextTertiary;
            item.Margin = new Thickness(0, stageItems.Count == 0 ? 0 : 10, 0, 0);
            stageItems[stage] = item;
            parent.Children.Add(item);
        }

        private Border CreateCard()
        {
            return new Border
            {
                Background = palette.Surface,
                BorderBrush = palette.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10)
            };
        }

        private Button CreateButton(string label)
        {
            Button button = new Button();
            button.Content = label;
            button.MinHeight = 38;
            button.Padding = new Thickness(14, 7, 14, 7);
            button.Background = palette.ButtonBackground;
            button.Foreground = palette.ButtonText;
            button.BorderBrush = palette.Border;
            button.BorderThickness = new Thickness(1);
            button.FontWeight = FontWeights.SemiBold;
            button.Cursor = System.Windows.Input.Cursors.Hand;
            ToolTipService.SetShowOnDisabled(button, true);
            DashboardVisualStyle.ApplyButtonChrome(button, 8);
            DashboardVisualStyle.ApplyFocusOutline(button, palette.Focus);
            return button;
        }

        private void SelectDestination()
        {
            if (operationStarted)
            {
                return;
            }
            SetStage(
                RepositoryLocationStage.Selecting,
                "Select the exact folder that should contain the Restic repository.");
            using (Forms.FolderBrowserDialog picker = new Forms.FolderBrowserDialog())
            {
                picker.Description = "Choose the new Restic backup repository folder";
                picker.ShowNewFolderButton = true;
                if (!string.IsNullOrWhiteSpace(selectedRepository) && Directory.Exists(selectedRepository))
                {
                    picker.SelectedPath = selectedRepository;
                }
                Forms.DialogResult result = picker.ShowDialog();
                if (result != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(picker.SelectedPath))
                {
                    if (string.IsNullOrWhiteSpace(selectedRepository))
                    {
                        detail.Text = "No folder selected. Your current repository is unchanged.";
                    }
                    else
                    {
                        SetStage(RepositoryLocationStage.Reviewing, "Review the selected location before approval.");
                    }
                    return;
                }
                ReviewDestination(picker.SelectedPath);
            }
        }

        private async void ReviewDestination(string value)
        {
            string canonical;
            try
            {
                canonical = RepositoryManagerLauncher.CanonicalizeRepository(value);
                string current = RepositoryManagerLauncher.CanonicalizeRepository(configuration.RepositoryPath);
                if (string.Equals(canonical, current, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("This is already the active repository.");
                }
                string currentPrefix = current + Path.DirectorySeparatorChar;
                string newPrefix = canonical + Path.DirectorySeparatorChar;
                if (canonical.StartsWith(currentPrefix, StringComparison.OrdinalIgnoreCase) ||
                    current.StartsWith(newPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "The new and current repositories cannot contain one another.");
                }
            }
            catch (Exception error)
            {
                SetStage(RepositoryLocationStage.Failed, error.Message);
                primaryButton.IsEnabled = false;
                return;
            }

            selectedRepository = canonical;
            newPathValue.Text = selectedRepository;
            newPathValue.ToolTip = selectedRepository;
            primaryButton.Content = "Checking storage…";
            primaryButton.IsEnabled = false;
            SetStage(
                RepositoryLocationStage.Reviewing,
                "Review the destination. Nothing changes until you approve the protected copy.");
            int generation = ++estimateGeneration;
            capacityValue.Text = "Calculating repository size and available space…";
            StorageEstimate estimate = await Task.Run(delegate
            {
                return StorageEstimate.Measure(configuration.RepositoryPath, selectedRepository);
            });
            if (generation != estimateGeneration || operationStarted)
            {
                return;
            }
            capacityValue.Text = estimate.DisplayText;
            capacityValue.Foreground = estimate.InsufficientSpace
                ? palette.Danger
                : palette.TextSecondary;
            primaryButton.Content = estimate.InsufficientSpace
                ? "More space required"
                : "Copy and use this location";
            primaryButton.IsEnabled = !estimate.InsufficientSpace;
        }

        private async void OnPrimaryClick(object sender, RoutedEventArgs args)
        {
            if (operationStarted || string.IsNullOrWhiteSpace(selectedRepository))
            {
                return;
            }
            operationStarted = true;
            chooseButton.IsEnabled = false;
            primaryButton.IsEnabled = false;
            closeButton.IsEnabled = false;
            progressBar.Visibility = Visibility.Visible;
            progressBar.IsIndeterminate = true;
            progressMetrics.Visibility = Visibility.Visible;
            progressMetrics.Text = "Waiting for Windows approval. No backup will start.";
            SetStage(
                RepositoryLocationStage.WaitingForApproval,
                "Approve the protected repository copy in the Windows prompt.");

            RepositoryManagerResult result;
            try
            {
                result = await Task.Run(delegate
                {
                    return RepositoryManagerLauncher.Run(
                        configuration,
                        selectedRepository,
                        delegate
                        {
                            Dispatcher.BeginInvoke(
                                new Action(delegate
                                {
                                    SetStage(
                                        RepositoryLocationStage.Copying,
                                        "Approval received. Preparing the verified copy.");
                                    progressMetrics.Text = "Preparing repository copy…";
                                }),
                                DispatcherPriority.Background);
                        },
                        delegate(RepositoryProgress progress)
                        {
                            Dispatcher.BeginInvoke(
                                new Action(delegate { ApplyProgress(progress); }),
                                DispatcherPriority.Background);
                        });
                });
            }
            catch (Exception error)
            {
                result = RepositoryManagerResult.Failure(error.Message, -1);
            }

            operationStarted = false;
            closeButton.IsEnabled = true;
            if (result.UserCancelled)
            {
                progressBar.Visibility = Visibility.Collapsed;
                progressMetrics.Visibility = Visibility.Collapsed;
                chooseButton.IsEnabled = true;
                primaryButton.IsEnabled = true;
                SetStage(
                    RepositoryLocationStage.Reviewing,
                    "Windows approval was cancelled. The active repository was not changed.");
                return;
            }

            SourceConfiguration independentlyRead = null;
            try { independentlyRead = SourceConfiguration.Load(); }
            catch { }
            bool independentlyVerified = result.Succeeded &&
                independentlyRead != null &&
                result.OldRepositoryRetained &&
                !string.IsNullOrWhiteSpace(result.OldRepository) &&
                Directory.Exists(result.OldRepository) &&
                string.Equals(result.PlanId, configuration.PlanId, StringComparison.Ordinal) &&
                string.Equals(independentlyRead.PlanId, configuration.PlanId, StringComparison.Ordinal) &&
                result.PreviousConfigGeneration == configuration.ConfigGeneration &&
                configuration.ConfigGeneration < long.MaxValue &&
                result.ConfigGeneration == configuration.ConfigGeneration + 1 &&
                independentlyRead.ConfigGeneration == result.ConfigGeneration &&
                string.Equals(
                    RepositoryManagerLauncher.CanonicalizeRepository(independentlyRead.RepositoryPath),
                    RepositoryManagerLauncher.CanonicalizeRepository(selectedRepository),
                    StringComparison.OrdinalIgnoreCase);
            if (independentlyVerified)
            {
                RepositoryChanged = true;
                progressBar.IsIndeterminate = false;
                progressBar.Value = 100;
                progressMetrics.Text = "Repository verified and active. The previous repository was retained.";
                SetStage(
                    RepositoryLocationStage.Complete,
                    "The new repository is active. The old copy remains untouched for recovery.");
                primaryButton.Content = "Done";
                primaryButton.IsEnabled = true;
                primaryButton.Click -= OnPrimaryClick;
                primaryButton.Click += delegate { DialogResult = true; };
                closeButton.Visibility = Visibility.Collapsed;
                return;
            }

            string failure = !result.Succeeded
                ? result.ErrorMessage
                : "The protected configuration did not independently confirm the new location.";
            progressBar.IsIndeterminate = false;
            progressBar.Value = 0;
            progressMetrics.Text = "No destination activation was accepted by the dashboard.";
            SetStage(
                RepositoryLocationStage.Failed,
                string.IsNullOrWhiteSpace(failure)
                    ? "The protected repository relocation failed."
                    : failure);
            chooseButton.IsEnabled = true;
            primaryButton.Content = "Try again";
            primaryButton.IsEnabled = true;
        }

        private void ApplyProgress(RepositoryProgress progress)
        {
            if (progress == null || !operationStarted)
            {
                return;
            }
            RepositoryLocationStage stage = RepositoryLocationStage.Copying;
            if (string.Equals(progress.Stage, "verifying", StringComparison.OrdinalIgnoreCase))
            {
                stage = RepositoryLocationStage.Verifying;
            }
            else if (string.Equals(progress.Stage, "activating", StringComparison.OrdinalIgnoreCase))
            {
                stage = RepositoryLocationStage.Activating;
            }
            else if (string.Equals(progress.Stage, "complete", StringComparison.OrdinalIgnoreCase))
            {
                stage = RepositoryLocationStage.Complete;
            }
            else if (string.Equals(progress.Stage, "failed", StringComparison.OrdinalIgnoreCase))
            {
                stage = RepositoryLocationStage.Failed;
            }
            SetStage(stage, string.IsNullOrWhiteSpace(progress.Message) ? null : progress.Message);

            if (progress.Percent.HasValue && progress.Percent.Value >= 0 && progress.Percent.Value <= 100)
            {
                progressBar.IsIndeterminate = false;
                progressBar.Value = progress.Percent.Value;
            }
            else
            {
                progressBar.IsIndeterminate = true;
            }

            List<string> metrics = new List<string>();
            if (progress.BytesCopied.HasValue)
            {
                string bytes = FormatBytes(progress.BytesCopied.Value);
                if (progress.BytesTotal.HasValue)
                {
                    bytes += " of " + FormatBytes(progress.BytesTotal.Value);
                }
                metrics.Add(bytes);
            }
            if (progress.FilesCopied.HasValue)
            {
                string files = progress.FilesCopied.Value.ToString("N0", CultureInfo.CurrentCulture) + " files";
                if (progress.FilesTotal.HasValue)
                {
                    files += " of " + progress.FilesTotal.Value.ToString("N0", CultureInfo.CurrentCulture);
                }
                metrics.Add(files);
            }
            if (progress.ThroughputBytesPerSecond.HasValue)
            {
                metrics.Add(FormatBytes((long)progress.ThroughputBytesPerSecond.Value) + "/s");
            }
            if (progress.EstimatedSecondsRemaining.HasValue)
            {
                metrics.Add(FormatDuration(progress.EstimatedSecondsRemaining.Value) + " remaining");
            }
            progressMetrics.Text = metrics.Count == 0
                ? (string.IsNullOrWhiteSpace(progress.Message) ? "Protected relocation in progress…" : progress.Message)
                : string.Join("  •  ", metrics.ToArray());
        }

        private void SetStage(RepositoryLocationStage stage, string message)
        {
            foreach (KeyValuePair<RepositoryLocationStage, TextBlock> item in stageItems)
            {
                bool active = item.Key == stage;
                item.Value.Foreground = active ? palette.AccentInfoText : palette.TextTertiary;
                item.Value.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
                string label = item.Value.Text.Length > 3 ? item.Value.Text.Substring(3) : item.Value.Text;
                item.Value.Text = (active ? "●  " : "○  ") + label;
            }
            stageBadge.Text = StageLabel(stage).ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(message))
            {
                detail.Text = message;
            }
            AutomationProperties.SetName(stageBadge, "Repository relocation stage " + StageLabel(stage));
        }

        private static string StageLabel(RepositoryLocationStage stage)
        {
            switch (stage)
            {
                case RepositoryLocationStage.WaitingForApproval: return "Waiting for approval";
                default: return stage.ToString();
            }
        }

        private static string FormatBytes(long value)
        {
            string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
            double amount = Math.Max(0, value);
            int unit = 0;
            while (amount >= 1024 && unit < units.Length - 1)
            {
                amount /= 1024;
                unit++;
            }
            return amount.ToString(unit == 0 ? "N0" : "N1", CultureInfo.CurrentCulture) + " " + units[unit];
        }

        private static string FormatDuration(double seconds)
        {
            if (seconds < 60)
            {
                return Math.Max(0, Math.Round(seconds)).ToString("N0", CultureInfo.CurrentCulture) + "s";
            }
            TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return duration.TotalHours >= 1
                ? ((int)duration.TotalHours).ToString(CultureInfo.CurrentCulture) + "h " + duration.Minutes + "m"
                : duration.Minutes.ToString(CultureInfo.CurrentCulture) + "m " + duration.Seconds + "s";
        }

        private sealed class StorageEstimate
        {
            internal string DisplayText { get; private set; }
            internal bool InsufficientSpace { get; private set; }

            internal static StorageEstimate Measure(string currentRepository, string newRepository)
            {
                long? required = TryMeasureDirectory(currentRepository);
                long? available = TryGetFreeSpace(newRepository);
                bool insufficient = required.HasValue && available.HasValue &&
                    available.Value < required.Value;
                string requiredText = required.HasValue
                    ? FormatBytes(required.Value)
                    : "Unavailable";
                string availableText = available.HasValue
                    ? FormatBytes(available.Value)
                    : "Unavailable";
                return new StorageEstimate
                {
                    InsufficientSpace = insufficient,
                    DisplayText = "Repository size: " + requiredText +
                        "  •  Free at destination: " + availableText +
                        (insufficient ? "  •  More free space is required." : string.Empty)
                };
            }

            private static long? TryMeasureDirectory(string root)
            {
                try
                {
                    long total = 0;
                    Stack<string> pending = new Stack<string>();
                    pending.Push(root);
                    while (pending.Count > 0)
                    {
                        string directory = pending.Pop();
                        foreach (string file in Directory.GetFiles(directory))
                        {
                            FileInfo item = new FileInfo(file);
                            if ((item.Attributes & FileAttributes.ReparsePoint) == 0)
                            {
                                checked { total += item.Length; }
                            }
                        }
                        foreach (string child in Directory.GetDirectories(directory))
                        {
                            DirectoryInfo item = new DirectoryInfo(child);
                            if ((item.Attributes & FileAttributes.ReparsePoint) == 0)
                            {
                                pending.Push(child);
                            }
                        }
                    }
                    return total;
                }
                catch
                {
                    return null;
                }
            }

            private static long? TryGetFreeSpace(string path)
            {
                try
                {
                    string root = Path.GetPathRoot(path);
                    if (string.IsNullOrWhiteSpace(root))
                    {
                        return null;
                    }
                    DriveInfo drive = new DriveInfo(root);
                    return drive.IsReady ? (long?)drive.AvailableFreeSpace : null;
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
