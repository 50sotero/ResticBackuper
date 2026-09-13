using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ResticBackuper.Dashboard
{
    internal sealed class RestoreWindow : Window
    {
        private readonly SourceConfiguration configuration;
        private readonly DashboardThemePalette palette;
        private DataGrid snapshotGrid;
        private DataGrid treeGrid;
        private TextBox treePathBox;
        private TextBox targetBox;
        private TextBox includesBox;
        private TextBlock statusTitle;
        private TextBlock statusDetail;
        private TextBlock selectedSnapshotDetail;
        private ProgressBar progressBar;
        private Button refreshButton;
        private Button browseTreeButton;
        private Button upButton;
        private Button useSelectionButton;
        private Button chooseTargetButton;
        private Button restoreButton;
        private Button openTargetButton;
        private Button closeButton;
        private bool operationInProgress;
        private string completedTarget = string.Empty;

        internal RestoreWindow(
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
            Title = "Restore Center";
            Width = 1120;
            Height = 760;
            MinWidth = 920;
            MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            DashboardVisualStyle.ApplyWindow(this, palette);
            Content = BuildInterface();
            Loaded += OnLoaded;
            Closing += OnClosing;
        }

        private UIElement BuildInterface()
        {
            Grid root = new Grid();
            root.Margin = new Thickness(22, 18, 22, 18);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel heading = new StackPanel();
            TextBlock title = CreateText("Restore Center", 25, palette.TextPrimary, FontWeights.SemiBold);
            heading.Children.Add(title);
            TextBlock subtitle = CreateText(
                "Browse plan-bound and safely matched legacy snapshots, then restore to a separate verified destination.",
                12,
                palette.TextSecondary,
                FontWeights.Normal);
            subtitle.Margin = new Thickness(0, 3, 0, 0);
            heading.Children.Add(subtitle);
            header.Children.Add(heading);
            refreshButton = CreateButton("Refresh snapshots", false);
            refreshButton.MinWidth = 132;
            refreshButton.Click += async delegate { await LoadSnapshots(); };
            Grid.SetColumn(refreshButton, 1);
            header.Children.Add(refreshButton);
            root.Children.Add(header);

            Border statusCard = CreateCard();
            statusCard.Padding = new Thickness(16, 12, 16, 12);
            Grid statusLayout = new Grid();
            statusLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statusLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            StackPanel statusCopy = new StackPanel();
            statusTitle = CreateText("Ready to read snapshot history", 14, palette.TextPrimary, FontWeights.SemiBold);
            statusCopy.Children.Add(statusTitle);
            statusDetail = CreateText(
                "Windows approval protects repository access. Reading snapshots does not change them.",
                11,
                palette.TextSecondary,
                FontWeights.Normal);
            statusDetail.Margin = new Thickness(0, 3, 0, 0);
            statusDetail.TextWrapping = TextWrapping.Wrap;
            statusCopy.Children.Add(statusDetail);
            statusLayout.Children.Add(statusCopy);
            progressBar = new ProgressBar();
            progressBar.Height = 8;
            progressBar.Minimum = 0;
            progressBar.Maximum = 100;
            progressBar.Visibility = Visibility.Collapsed;
            progressBar.VerticalAlignment = VerticalAlignment.Center;
            progressBar.Foreground = palette.AccentPrimary;
            progressBar.Background = palette.ProgressTrack;
            Grid.SetColumn(progressBar, 1);
            statusLayout.Children.Add(progressBar);
            statusCard.Child = statusLayout;
            Grid.SetRow(statusCard, 2);
            root.Children.Add(statusCard);

            Grid workspace = new Grid();
            workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.92, GridUnitType.Star) });
            workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.08, GridUnitType.Star) });
            workspace.Children.Add(BuildSnapshotCard());
            UIElement restoreCard = BuildRestoreCard();
            Grid.SetColumn(restoreCard, 2);
            workspace.Children.Add(restoreCard);
            Grid.SetRow(workspace, 4);
            root.Children.Add(workspace);

            Grid footer = new Grid();
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock safety = CreateText(
                "Safety contract: exact snapshot · new or empty target · never overwrite · verify after restore",
                11,
                palette.SafetyText,
                FontWeights.SemiBold);
            safety.VerticalAlignment = VerticalAlignment.Center;
            safety.TextWrapping = TextWrapping.Wrap;
            footer.Children.Add(safety);
            StackPanel footerActions = new StackPanel { Orientation = Orientation.Horizontal };
            openTargetButton = CreateButton("Open restored folder", false);
            openTargetButton.Visibility = Visibility.Collapsed;
            openTargetButton.Click += OnOpenTargetClick;
            footerActions.Children.Add(openTargetButton);
            closeButton = CreateButton("Close", false);
            closeButton.Margin = new Thickness(8, 0, 0, 0);
            closeButton.Click += delegate { Close(); };
            footerActions.Children.Add(closeButton);
            Grid.SetColumn(footerActions, 1);
            footer.Children.Add(footerActions);
            Grid.SetRow(footer, 6);
            root.Children.Add(footer);
            AutomationProperties.SetName(root, "Protected snapshot restore center");
            return root;
        }

        private UIElement BuildSnapshotCard()
        {
            Border card = CreateCard();
            card.Padding = new Thickness(16, 14, 16, 14);
            Grid layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            StackPanel heading = new StackPanel();
            heading.Children.Add(CreateText("Snapshots", 17, palette.TextPrimary, FontWeights.SemiBold));
            TextBlock hint = CreateText(
                "Plan generations and exact configuration-matched legacy snapshots, newest first",
                11,
                palette.TextSecondary,
                FontWeights.Normal);
            hint.Margin = new Thickness(0, 2, 0, 0);
            heading.Children.Add(hint);
            layout.Children.Add(heading);

            snapshotGrid = CreateGrid();
            snapshotGrid.SelectionMode = DataGridSelectionMode.Single;
            snapshotGrid.SelectionChanged += OnSnapshotSelectionChanged;
            snapshotGrid.Columns.Add(CreateColumn("When", "WhenDisplay", 1.35));
            snapshotGrid.Columns.Add(CreateColumn("ID", "ShortId", 0.75));
            snapshotGrid.Columns.Add(CreateColumn("Binding", "GenerationDisplay", 1.0));
            snapshotGrid.Columns.Add(CreateColumn("Data", "SizeDisplay", 0.75));
            Grid.SetRow(snapshotGrid, 2);
            layout.Children.Add(snapshotGrid);

            selectedSnapshotDetail = CreateText(
                "Select a snapshot to browse or restore.",
                11,
                palette.TextSecondary,
                FontWeights.Normal);
            selectedSnapshotDetail.Margin = new Thickness(0, 10, 0, 0);
            selectedSnapshotDetail.TextWrapping = TextWrapping.Wrap;
            Grid.SetRow(selectedSnapshotDetail, 3);
            layout.Children.Add(selectedSnapshotDetail);
            card.Child = layout;
            return card;
        }

        private UIElement BuildRestoreCard()
        {
            Border card = CreateCard();
            card.Padding = new Thickness(16, 14, 16, 14);
            Grid layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.Children.Add(CreateText("Browse and restore", 17, palette.TextPrimary, FontWeights.SemiBold));

            Grid browserControls = new Grid();
            browserControls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            browserControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            browserControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            treePathBox = CreateTextBox("/");
            AutomationProperties.SetName(treePathBox, "Snapshot folder path");
            browserControls.Children.Add(treePathBox);
            upButton = CreateButton("Up", false);
            upButton.Margin = new Thickness(6, 0, 0, 0);
            upButton.Click += OnUpClick;
            Grid.SetColumn(upButton, 1);
            browserControls.Children.Add(upButton);
            browseTreeButton = CreateButton("Browse", false);
            browseTreeButton.Margin = new Thickness(6, 0, 0, 0);
            browseTreeButton.Click += async delegate { await LoadTree(); };
            Grid.SetColumn(browseTreeButton, 2);
            browserControls.Children.Add(browseTreeButton);
            Grid.SetRow(browserControls, 2);
            layout.Children.Add(browserControls);

            treeGrid = CreateGrid();
            treeGrid.SelectionMode = DataGridSelectionMode.Extended;
            treeGrid.MouseDoubleClick += OnTreeDoubleClick;
            treeGrid.Columns.Add(CreateColumn("Name", "Name", 1.35));
            treeGrid.Columns.Add(CreateColumn("Type", "EntryType", 0.55));
            treeGrid.Columns.Add(CreateColumn("Size", "SizeDisplay", 0.65));
            Grid.SetRow(treeGrid, 4);
            layout.Children.Add(treeGrid);

            Grid selectionControls = new Grid();
            selectionControls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            selectionControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel includesPanel = new StackPanel();
            includesPanel.Children.Add(CreateText(
                "Selected paths (blank restores the entire snapshot)",
                11,
                palette.TextSecondary,
                FontWeights.SemiBold));
            includesBox = CreateTextBox(string.Empty);
            includesBox.AcceptsReturn = true;
            includesBox.Height = 58;
            includesBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            includesBox.Margin = new Thickness(0, 4, 0, 0);
            AutomationProperties.SetName(includesBox, "Paths to restore, one per line");
            includesPanel.Children.Add(includesBox);
            selectionControls.Children.Add(includesPanel);
            useSelectionButton = CreateButton("Use selection", false);
            useSelectionButton.Margin = new Thickness(8, 20, 0, 0);
            useSelectionButton.VerticalAlignment = VerticalAlignment.Top;
            useSelectionButton.Click += OnUseSelectionClick;
            Grid.SetColumn(useSelectionButton, 1);
            selectionControls.Children.Add(useSelectionButton);
            Grid.SetRow(selectionControls, 6);
            layout.Children.Add(selectionControls);

            Grid destination = new Grid();
            destination.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            destination.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel destinationCopy = new StackPanel();
            destinationCopy.Children.Add(CreateText(
                "New or empty destination folder",
                11,
                palette.TextSecondary,
                FontWeights.SemiBold));
            targetBox = CreateTextBox(string.Empty);
            targetBox.Margin = new Thickness(0, 4, 0, 0);
            AutomationProperties.SetName(targetBox, "Restore destination folder");
            destinationCopy.Children.Add(targetBox);
            destination.Children.Add(destinationCopy);
            chooseTargetButton = CreateButton("Choose folder", false);
            chooseTargetButton.Margin = new Thickness(8, 20, 0, 0);
            chooseTargetButton.VerticalAlignment = VerticalAlignment.Top;
            chooseTargetButton.Click += OnChooseTargetClick;
            Grid.SetColumn(chooseTargetButton, 1);
            destination.Children.Add(chooseTargetButton);
            Grid.SetRow(destination, 8);
            layout.Children.Add(destination);

            restoreButton = CreateButton("Restore and verify", true);
            restoreButton.HorizontalAlignment = HorizontalAlignment.Right;
            restoreButton.MinWidth = 144;
            restoreButton.Margin = new Thickness(0, 10, 0, 0);
            restoreButton.Click += async delegate { await RestoreSelectedSnapshot(); };
            Grid.SetRow(restoreButton, 9);
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.Children.Add(restoreButton);
            card.Child = layout;
            return card;
        }

        private async void OnLoaded(object sender, RoutedEventArgs args)
        {
            await LoadSnapshots();
        }

        private async Task LoadSnapshots()
        {
            if (operationInProgress)
            {
                return;
            }
            SetBusy(true, "Waiting for Windows approval", "Repository access is protected.", null);
            RestoreManagerResult result;
            try
            {
                result = await Task.Run(delegate
                {
                    return RestoreManagerLauncher.ListSnapshots(
                        configuration,
                        delegate
                        {
                            Dispatcher.BeginInvoke(new Action(delegate
                            {
                                SetBusy(true, "Reading snapshots", "Loading this backup plan's history.", null);
                            }));
                        },
                        OnProtectedProgress);
                });
            }
            catch (Exception error)
            {
                result = RestoreManagerResult.Failure(error.Message, -1);
            }
            if (result.UserCancelled)
            {
                SetBusy(false, "Windows approval cancelled", "No repository data was read.", false);
                return;
            }
            if (!result.Succeeded)
            {
                SetBusy(false, "Snapshots unavailable", result.ErrorMessage, false);
                return;
            }
            snapshotGrid.ItemsSource = result.Snapshots;
            int legacyCount = 0;
            foreach (RestoreSnapshot snapshot in result.Snapshots)
            {
                if (snapshot.IsLegacyUnbound)
                {
                    legacyCount++;
                }
            }
            if (result.Snapshots.Count > 0)
            {
                snapshotGrid.SelectedIndex = 0;
            }
            if (result.Snapshots.Count == 0)
            {
                SetBusy(
                    false,
                    "No matching snapshots found",
                    "No plan-bound or exact configuration-matched legacy snapshots are available.",
                    true);
            }
            else if (legacyCount > 0)
            {
                int planCount = result.Snapshots.Count - legacyCount;
                string detail = planCount == 0
                    ? legacyCount.ToString() +
                        " legacy / unbound snapshots matched this repository, computer, scheduled tag, and exact source set."
                    : planCount.ToString() + " plan-bound and " + legacyCount.ToString() +
                        " legacy / unbound snapshots are available.";
                SetBusy(false, "Snapshots ready - legacy history included", detail, true);
                statusTitle.Foreground = palette.Warning;
            }
            else
            {
                SetBusy(
                    false,
                    "Snapshots ready",
                    result.Snapshots.Count.ToString() + " plan-bound snapshots are available to browse.",
                    true);
            }
        }

        private async Task LoadTree()
        {
            RestoreSnapshot snapshot = snapshotGrid.SelectedItem as RestoreSnapshot;
            if (snapshot == null || operationInProgress)
            {
                SetBusy(false, "Choose a snapshot", "Select one snapshot before browsing files.", false);
                return;
            }
            string path = (treePathBox.Text ?? string.Empty).Trim();
            if (path.Length == 0)
            {
                path = "/";
            }
            SetBusy(true, "Waiting for Windows approval", "Preparing the protected snapshot browser.", null);
            RestoreManagerResult result;
            try
            {
                result = await Task.Run(delegate
                {
                    return RestoreManagerLauncher.ListTree(
                        configuration,
                        snapshot.Id,
                        path,
                        snapshot.IsLegacyUnbound,
                        null,
                        OnProtectedProgress);
                });
            }
            catch (Exception error)
            {
                result = RestoreManagerResult.Failure(error.Message, -1);
            }
            if (result.UserCancelled)
            {
                SetBusy(false, "Windows approval cancelled", "The snapshot folder was not read.", false);
                return;
            }
            if (!result.Succeeded)
            {
                SetBusy(false, "Folder unavailable", result.ErrorMessage, false);
                return;
            }
            treeGrid.ItemsSource = result.Entries;
            SetBusy(
                false,
                "Snapshot folder ready",
                result.Entries.Count.ToString() + " entries loaded from " + path + ".",
                true);
        }

        private async Task RestoreSelectedSnapshot()
        {
            RestoreSnapshot snapshot = snapshotGrid.SelectedItem as RestoreSnapshot;
            if (snapshot == null || operationInProgress)
            {
                SetBusy(false, "Choose a snapshot", "Select one immutable snapshot first.", false);
                return;
            }
            string target;
            try
            {
                target = SourceConfiguration.NormalizePath((targetBox.Text ?? string.Empty).Trim());
                if (!Path.IsPathRooted(target))
                {
                    throw new InvalidDataException("The restore destination must be an absolute local path.");
                }
                if (Directory.Exists(target) && Directory.GetFileSystemEntries(target).Length != 0)
                {
                    throw new InvalidDataException("The restore destination must be empty.");
                }
            }
            catch (Exception error)
            {
                SetBusy(false, "Choose a safe destination", error.Message, false);
                return;
            }
            IList<string> includes;
            try
            {
                includes = ParseIncludes(includesBox.Text);
            }
            catch (Exception error)
            {
                SetBusy(false, "Review selected paths", error.Message, false);
                return;
            }
            string scope = includes.Count == 0
                ? "the entire snapshot"
                : includes.Count.ToString() + " selected path(s)";
            if (snapshot.IsLegacyUnbound)
            {
                MessageBoxResult legacyConfirmation = MessageBox.Show(
                    this,
                    "This snapshot predates backup plan IDs and is labelled Legacy / unbound.\n\n" +
                    "It matched this repository, computer, scheduled tag, and exact source-path set, " +
                    "but it is not bound to the current plan ID.\n\n" +
                    "Exact immutable snapshot ID:\n" + snapshot.Id +
                    "\n\nContinue with this legacy snapshot?",
                    "Confirm legacy / unbound snapshot",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (legacyConfirmation != MessageBoxResult.Yes)
                {
                    return;
                }
            }
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                "Restore " + scope + " to:\n\n" + target +
                "\n\nLive files and the repository will not be overwritten. " +
                "If Restic stops, partial files remain in this alternate folder for inspection.",
                "Restore and verify",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);
            if (confirmation != MessageBoxResult.OK)
            {
                return;
            }
            completedTarget = string.Empty;
            openTargetButton.Visibility = Visibility.Collapsed;
            SetBusy(true, "Waiting for Windows approval", "The restore will write only to the alternate destination.", null);
            RestoreManagerResult result;
            try
            {
                result = await Task.Run(delegate
                {
                    return RestoreManagerLauncher.Restore(
                        configuration,
                        snapshot.Id,
                        target,
                        includes,
                        snapshot.IsLegacyUnbound,
                        delegate
                        {
                            Dispatcher.BeginInvoke(new Action(delegate
                            {
                                SetBusy(true, "Restore approved", "Restoring and verifying the selected snapshot.", null);
                            }));
                        },
                        OnProtectedProgress);
                });
            }
            catch (Exception error)
            {
                result = RestoreManagerResult.Failure(error.Message, -1);
            }
            if (result.UserCancelled)
            {
                SetBusy(false, "Windows approval cancelled", "No restore was started.", false);
                return;
            }
            if (result.Succeeded && result.Report != null && result.Report.Verified)
            {
                completedTarget = result.Report.Target;
                openTargetButton.Visibility = Visibility.Visible;
                SetBusy(
                    false,
                    "Restore complete and verified",
                    "The restored copy is ready at " + result.Report.Target + ".",
                    true);
                return;
            }
            if (result.Partial && result.Report != null)
            {
                completedTarget = result.Report.Target;
                openTargetButton.Visibility = Directory.Exists(completedTarget)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                SetBusy(
                    false,
                    "Restore incomplete — partial files retained",
                    result.ErrorMessage + " Inspect the alternate destination; live files were not changed.",
                    false);
                return;
            }
            SetBusy(false, "Restore did not start", result.ErrorMessage, false);
        }

        private void OnProtectedProgress(RestoreManagerProgress progress)
        {
            if (progress == null)
            {
                return;
            }
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (!operationInProgress)
                {
                    return;
                }
                statusTitle.Text = FriendlyStage(progress.Stage);
                statusDetail.Text = progress.Message;
                progressBar.IsIndeterminate = progress.Percent <= 5 ||
                    string.Equals(progress.Stage, "restoring", StringComparison.Ordinal);
                if (!progressBar.IsIndeterminate)
                {
                    progressBar.Value = progress.Percent;
                }
            }), DispatcherPriority.Background);
        }

        private void SetBusy(
            bool busy,
            string title,
            string detail,
            bool? success)
        {
            operationInProgress = busy;
            statusTitle.Text = title ?? string.Empty;
            statusDetail.Text = detail ?? string.Empty;
            statusTitle.Foreground = success.HasValue
                ? (success.Value ? palette.Success : palette.Danger)
                : palette.TextPrimary;
            progressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            progressBar.IsIndeterminate = busy;
            refreshButton.IsEnabled = !busy;
            browseTreeButton.IsEnabled = !busy;
            upButton.IsEnabled = !busy;
            useSelectionButton.IsEnabled = !busy;
            chooseTargetButton.IsEnabled = !busy;
            restoreButton.IsEnabled = !busy;
            closeButton.IsEnabled = !busy;
            snapshotGrid.IsEnabled = !busy;
            treeGrid.IsEnabled = !busy;
            AutomationProperties.SetName(statusTitle, title + ". " + detail);
            AutomationProperties.SetLiveSetting(
                statusTitle,
                success.HasValue && !success.Value
                    ? AutomationLiveSetting.Assertive
                    : AutomationLiveSetting.Polite);
        }

        private void OnSnapshotSelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            RestoreSnapshot snapshot = snapshotGrid.SelectedItem as RestoreSnapshot;
            treeGrid.ItemsSource = null;
            treePathBox.Text = "/";
            if (snapshot == null)
            {
                selectedSnapshotDetail.Text = "Select a snapshot to browse or restore.";
                return;
            }
            selectedSnapshotDetail.Text = snapshot.WhenDisplay + " - " +
                snapshot.GenerationDisplay + " - " + snapshot.FileCount.ToString() +
                " files - " + snapshot.SizeDisplay + "\n" + snapshot.SourceSummary;
            selectedSnapshotDetail.Foreground = snapshot.IsLegacyUnbound
                ? palette.Warning
                : palette.TextSecondary;
            if (snapshot.IsLegacyUnbound)
            {
                selectedSnapshotDetail.Text +=
                    "\nLegacy / unbound: exact repository, computer, scheduled-tag, and source-set match; explicit confirmation required.";
            }
        }

        private async void OnTreeDoubleClick(object sender, MouseButtonEventArgs args)
        {
            RestoreTreeEntry entry = treeGrid.SelectedItem as RestoreTreeEntry;
            if (entry == null || entry.EntryType != "dir" || operationInProgress)
            {
                return;
            }
            treePathBox.Text = entry.EntryPath;
            await LoadTree();
        }

        private async void OnUpClick(object sender, RoutedEventArgs args)
        {
            if (operationInProgress)
            {
                return;
            }
            string value = (treePathBox.Text ?? "/").TrimEnd('/');
            int separator = value.LastIndexOf('/');
            treePathBox.Text = separator <= 0 ? "/" : value.Substring(0, separator);
            await LoadTree();
        }

        private void OnUseSelectionClick(object sender, RoutedEventArgs args)
        {
            List<string> paths = new List<string>();
            foreach (object item in treeGrid.SelectedItems)
            {
                RestoreTreeEntry entry = item as RestoreTreeEntry;
                if (entry != null && !string.IsNullOrWhiteSpace(entry.EntryPath) &&
                    !paths.Contains(entry.EntryPath))
                {
                    paths.Add(entry.EntryPath);
                }
            }
            if (paths.Count == 0)
            {
                SetBusy(false, "Select files or folders", "Choose one or more snapshot entries first.", false);
                return;
            }
            includesBox.Text = string.Join(Environment.NewLine, paths.ToArray());
            SetBusy(false, "Restore selection updated", paths.Count.ToString() + " path(s) selected.", true);
        }

        private void OnChooseTargetClick(object sender, RoutedEventArgs args)
        {
            using (System.Windows.Forms.FolderBrowserDialog dialog =
                new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Choose an existing empty folder, or create a new restore folder.";
                dialog.ShowNewFolderButton = true;
                if (Directory.Exists(targetBox.Text))
                {
                    dialog.SelectedPath = targetBox.Text;
                }
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    targetBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void OnOpenTargetClick(object sender, RoutedEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(completedTarget) || !Directory.Exists(completedTarget))
            {
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + completedTarget.Replace("\"", string.Empty) + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception error)
            {
                SetBusy(false, "Could not open the folder", error.Message, false);
            }
        }

        private void OnClosing(object sender, CancelEventArgs args)
        {
            if (!operationInProgress)
            {
                return;
            }
            args.Cancel = true;
            statusTitle.Text = "Restore operation still running";
            statusDetail.Text = "Keep this window open until the protected operation reaches a final state.";
        }

        private static IList<string> ParseIncludes(string value)
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in (value ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
            {
                string item = line.Trim();
                if (item.Length == 0)
                {
                    continue;
                }
                if (item.Length > 1024 || item.IndexOf('\0') >= 0)
                {
                    throw new InvalidDataException("A selected snapshot path is invalid or too long.");
                }
                if (seen.Add(item))
                {
                    result.Add(item);
                }
            }
            if (result.Count > 64)
            {
                throw new InvalidDataException("Select at most 64 paths for one restore.");
            }
            return result;
        }

        private static string FriendlyStage(string stage)
        {
            switch ((stage ?? string.Empty).ToLowerInvariant())
            {
                case "preflight": return "Validating restore safety";
                case "reading": return "Reading protected snapshot data";
                case "restoring": return "Restoring and verifying";
                case "complete": return "Protected operation complete";
                case "failed": return "Protected operation needs attention";
                default: return "Protected restore operation";
            }
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

        private Button CreateButton(string text, bool primary)
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

        private TextBox CreateTextBox(string text)
        {
            TextBox box = new TextBox();
            box.Text = text;
            box.Padding = new Thickness(9, 7, 9, 7);
            box.MinHeight = 34;
            box.Background = palette.SurfaceSoft;
            box.Foreground = palette.TextPrimary;
            box.BorderBrush = palette.Border;
            box.CaretBrush = palette.TextPrimary;
            return box;
        }

        private DataGrid CreateGrid()
        {
            DataGrid grid = new DataGrid();
            grid.AutoGenerateColumns = false;
            grid.IsReadOnly = true;
            grid.CanUserAddRows = false;
            grid.CanUserDeleteRows = false;
            grid.CanUserResizeRows = false;
            grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            grid.HorizontalGridLinesBrush = palette.GridLine;
            grid.VerticalGridLinesBrush = Brushes.Transparent;
            grid.Background = Brushes.Transparent;
            grid.Foreground = palette.TableText;
            grid.BorderBrush = palette.Border;
            grid.RowBackground = palette.RowEven;
            grid.AlternatingRowBackground = palette.AlternatingRow;
            grid.SelectionMode = DataGridSelectionMode.Single;
            grid.SelectionUnit = DataGridSelectionUnit.FullRow;
            DashboardVisualStyle.ApplyDataGridChrome(grid, palette);
            return grid;
        }

        private static DataGridTextColumn CreateColumn(
            string header,
            string property,
            double width)
        {
            return new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(property),
                Width = new DataGridLength(width, DataGridLengthUnitType.Star)
            };
        }

        private static TextBlock CreateText(
            string text,
            double size,
            Brush foreground,
            FontWeight weight)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = foreground,
                FontWeight = weight
            };
        }
    }
}
