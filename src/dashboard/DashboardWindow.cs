using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace ResticBackuper.Dashboard
{
    public sealed partial class DashboardWindow : Window
    {
        private enum SourceOperationStage
        {
            Idle,
            AwaitingApproval,
            Applying,
            Verifying,
            Succeeded,
            Cancelled,
            Failed
        }

        private Brush BackgroundTop { get { return themeResolution.Palette.BackgroundTop; } }
        private Brush BackgroundBottom { get { return themeResolution.Palette.BackgroundBottom; } }
        private Brush CardBrush { get { return themeResolution.Palette.Surface; } }
        private Brush CardSoftBrush { get { return themeResolution.Palette.SurfaceSoft; } }
        private Brush CardBorderBrush { get { return themeResolution.Palette.Border; } }
        private Brush PrimaryText { get { return themeResolution.Palette.TextPrimary; } }
        private Brush MutedText { get { return themeResolution.Palette.TextSecondary; } }
        private Brush Teal { get { return themeResolution.Palette.AccentPrimary; } }
        private Brush Blue { get { return themeResolution.Palette.AccentInfo; } }
        private Brush BlueText { get { return themeResolution.Palette.AccentInfoText; } }
        private Brush Green { get { return themeResolution.Palette.Success; } }
        private Brush Rose { get { return themeResolution.Palette.Danger; } }
        private Brush Amber { get { return themeResolution.Palette.Warning; } }

        private readonly AppOptions options;
        private readonly TelemetryReader reader;
        private readonly EventWaitHandle showEvent;
        private readonly DispatcherTimer refreshTimer;
        private readonly Forms.NotifyIcon trayIcon;
        private Drawing.Icon trayApplicationIcon;
        private readonly List<Border> phaseMarkers = new List<Border>();
        private readonly List<TextBlock> phaseLabels = new List<TextBlock>();
        private readonly Dictionary<DashboardThemePreference, Button> themeButtons =
            new Dictionary<DashboardThemePreference, Button>();

        private DashboardThemePreference themePreference;
        private DashboardThemeResolution themeResolution;
        private bool themeChangeInProgress;
        private Grid headerLayout;
        private FrameworkElement headerTitlePanel;
        private FrameworkElement headerActions;
        private Grid shellLayout;
        private Border navigationRail;
        private TextBlock navigationBrand;
        private Button protectionNavButton;
        private Button activityNavButton;
        private Button restoreNavButton;
        private Button settingsNavButton;
        private Grid pageHost;
        private FrameworkElement settingsPage;
        private FrameworkElement restorePage;
        private string selectedDashboardPage = "Protection";
        private Grid protectionOverview;
        private FrameworkElement protectionHero;
        private FrameworkElement protectionSources;
        private UniformGrid metricsLayout;
        private FrameworkElement activeProgressPanel;
        private FrameworkElement activePhaseRail;
        private Grid historyLayout;
        private FrameworkElement historyChart;
        private FrameworkElement historyTable;
        private Grid footerLayout;
        private TextBlock footerDetails;
        private bool? compactLayout;

        private TextBlock statusBadgeText;
        private Ellipse statusDot;
        private Border statusBadge;
        private TextBlock heroTitle;
        private TextBlock heroDetail;
        private TextBlock progressPercent;
        private TextBlock estimateBadge;
        private Border progressTrack;
        private Border progressFill;
        private TranslateTransform shimmerTransform;
        private TextBlock etaTitle;
        private TextBlock etaValue;
        private TextBlock etaHint;
        private TextBlock filesValue;
        private TextBlock bytesValue;
        private TextBlock speedValue;
        private TextBlock elapsedValue;
        private TextBlock errorsValue;
        private TextBlock lastUpdated;
        private TextBlock runCount;
        private DataGrid historyGrid;
        private Button viewRunDetailsButton;
        private Button exportDiagnosticsButton;
        private StackPanel sourceList;
        private RunChart runChart;
        private Button previewButton;
        private Button backupNowButton;
        private Button cancelBackupButton;
        private Button reviewChangesButton;
        private Button editScheduleButton;
        private Button changeRepositoryButton;
        private Button settingsChangeRepositoryButton;
        private Button openRestoreCenterButton;
        private Button checkRecoveryReadinessButton;
        private TextBlock restoreReadinessTitle;
        private TextBlock restoreReadinessDetail;
        private Border protectionRepositoryRecoveryCard;
        private Border settingsRepositoryRecoveryCard;
        private TextBlock protectionRepositoryRecoveryMessage;
        private TextBlock settingsRepositoryRecoveryMessage;
        private TextBlock protectionRepositoryRecoveryState;
        private TextBlock settingsRepositoryRecoveryState;
        private Button protectionRepositoryRecoveryButton;
        private Button settingsRepositoryRecoveryButton;
        private ProgressBar protectionRepositoryRecoveryProgress;
        private ProgressBar settingsRepositoryRecoveryProgress;
        private Button addSourceButton;
        private readonly List<Button> removeSourceButtons = new List<Button>();
        private TextBlock sourceSummary;
        private TextBlock sourceStatus;
        private TextBlock headerSubtitle;
        private TextBlock repositoryValue;
        private TextBlock settingsRepositoryValue;
        private TextBlock settingsRepositoryVolume;
        private TextBlock metricsContext;
        private TextBlock cancelActionStatus;
        private TextBlock scheduleActionStatus;
        private TextBlock protectionFreshnessStatus;
        private TextBlock settingsFreshnessStatus;
        private TextBlock settingsFreshnessDetail;
        private TextBlock settingsOffsiteStatus;
        private TextBlock settingsOffsiteDetail;
        private TextBlock settingsOffsiteEvidence;
        private Border sourceOperationItem;
        private Border sourceOperationTag;
        private TextBlock sourceOperationTagText;
        private Border sourceOperationGlyphFrame;
        private TextBlock sourceOperationGlyph;
        private TextBlock sourceOperationStatusText;
        private TextBlock sourceOperationDetailText;
        private Border sourceOperationRail;
        private Border sourceOperationRailSegment;
        private TranslateTransform sourceOperationRailTransform;
        private Button sourceOperationRetryButton;
        private Button sourceOperationDismissButton;
        private readonly Dictionary<string, FrameworkElement> sourceRows =
            new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase);

        private SourceConfiguration currentSourceConfiguration;
        private string sourceSignature;
        private bool sourceOperationInProgress;
        private DateTime sourceNoticeExpiresUtc = DateTime.MinValue;
        private SourceOperationStage sourceOperationStage = SourceOperationStage.Idle;
        private string sourceOperationAction = string.Empty;
        private string sourceOperationPath = string.Empty;
        private string sourceOperationError = string.Empty;
        private string lastSourceOperationAnnouncement = string.Empty;
        private int sourceOperationGeneration;
        private DateTime sourceOperationExpiresUtc = DateTime.MinValue;
        private bool animateNextSourceOperationEntry;
        private bool animateNextResolvedSourceRow;
        private bool backupStartInProgress;
        private bool anomalyReviewInProgress;
        private bool diagnosticExportInProgress;
        private DateTime backupRequestPendingUntilUtc = DateTime.MinValue;
        private string backupRequestBaselineRunId = string.Empty;
        private bool cancellationRequestInProgress;
        private bool cancellationAwaitingTerminal;
        private string cancellationRequestedRunId = string.Empty;
        private string cancellationNotice = string.Empty;
        private Brush cancellationNoticeColor;
        private DateTime cancellationNoticeExpiresUtc = DateTime.MinValue;
        private string cancellationUnavailableRunId = string.Empty;
        private TaskSchedule currentTaskSchedule;
        private string scheduleReadError = string.Empty;
        private bool scheduleOperationInProgress;
        private bool repositoryOperationInProgress;
        private bool repositoryRecoveryInProgress;
        private RepositoryRecoveryStatus repositoryRecoveryStatus = RepositoryRecoveryStatus.None();
        private string repositoryRecoveryOperationMessage = string.Empty;
        private double? repositoryRecoveryOperationPercent;
        private string lastRepositoryRecoveryAnnouncement = string.Empty;
        private DateTime nextScheduleRefreshUtc = DateTime.MinValue;
        private BackupFreshnessState? lastAnnouncedFreshnessState;

        private bool allowClose;
        private bool previewEnabled;
        private DateTime previewStarted;
        private string lastStateKey;
        private double currentProgress;
        private TelemetrySnapshot lastSnapshot;
        private DateTime lastHeartbeatUtc = DateTime.MinValue;

        public DashboardWindow(AppOptions options, EventWaitHandle showEvent)
        {
            this.options = options;
            this.showEvent = showEvent;
            this.previewEnabled = options.Preview;
            this.previewStarted = DateTime.Now;
            this.reader = new TelemetryReader(
                options.StateDirectory,
                !options.UseIsolatedPresentationStore);
            if (options.UseIsolatedPresentationStore)
            {
                DashboardThemeManager.IsolatedSettingsRoot = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "ResticBackuperPresentationSettings");
            }
            this.themeResolution = DashboardThemeManager.LoadAndResolve();
            this.themePreference = this.themeResolution.Preference;

            Title = "Rewindle";
            Width = 1280;
            Height = 720;
            MinWidth = 900;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ApplyWindowPalette();
            FontFamily = DashboardVisualStyle.UiFont;
            Foreground = PrimaryText;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            Content = BuildInterface();
            Closing += OnClosing;
            Closed += OnClosed;
            Loaded += OnLoaded;
            SizeChanged += OnWindowSizeChanged;
            PreviewKeyDown += OnDashboardPreviewKeyDown;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
            Application.Current.SessionEnding += delegate { allowClose = true; };

            trayIcon = BuildTrayIcon();
            refreshTimer = new DispatcherTimer(DispatcherPriority.Background);
            refreshTimer.Interval = TimeSpan.FromSeconds(1);
            refreshTimer.Tick += OnRefreshTick;
            refreshTimer.Start();
        }

        private UIElement BuildInterface()
        {
            StopSourceOperationAnimation();
            phaseMarkers.Clear();
            phaseLabels.Clear();
            removeSourceButtons.Clear();
            sourceRows.Clear();
            themeButtons.Clear();
            compactLayout = null;
            sourceOperationItem = null;
            sourceOperationTag = null;
            sourceOperationTagText = null;
            sourceOperationGlyphFrame = null;
            sourceOperationGlyph = null;
            sourceOperationStatusText = null;
            sourceOperationDetailText = null;
            sourceOperationRail = null;
            sourceOperationRailSegment = null;
            sourceOperationRailTransform = null;
            sourceOperationRetryButton = null;
            sourceOperationDismissButton = null;

            shellLayout = new Grid();
            shellLayout.Background = BackgroundTop;
            shellLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(216) });
            shellLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            navigationRail = BuildNavigationRail();
            shellLayout.Children.Add(navigationRail);

            Grid root = new Grid();
            root.Margin = new Thickness(24, 18, 24, 12);
            root.HorizontalAlignment = HorizontalAlignment.Stretch;
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            UIElement header = BuildHeader();
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            Border headerDivider = new Border();
            headerDivider.Height = 1;
            headerDivider.Background = CardBorderBrush;
            headerDivider.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRow(headerDivider, 1);
            root.Children.Add(headerDivider);

            pageHost = new Grid();
            protectionOverview = BuildProtectionPage();
            pageHost.Children.Add(protectionOverview);
            historyLayout = BuildActivityPage();
            pageHost.Children.Add(historyLayout);
            restorePage = (FrameworkElement)BuildRestorePage();
            pageHost.Children.Add(restorePage);
            settingsPage = (FrameworkElement)BuildSettingsPage();
            pageHost.Children.Add(settingsPage);
            Grid.SetRow(pageHost, 2);
            root.Children.Add(pageHost);

            footerDetails = new TextBlock();
            footerDetails.Text = "Encrypted locally  •  Protected changes require Windows approval";
            footerDetails.Foreground = themeResolution.Palette.FooterText;
            footerDetails.FontSize = 12;
            footerDetails.FontWeight = FontWeights.SemiBold;
            footerDetails.VerticalAlignment = VerticalAlignment.Bottom;
            footerDetails.HorizontalAlignment = HorizontalAlignment.Left;
            footerDetails.TextWrapping = TextWrapping.Wrap;
            lastUpdated = new TextBlock();
            lastUpdated.Foreground = themeResolution.Palette.FooterText;
            lastUpdated.FontSize = 12;
            lastUpdated.HorizontalAlignment = HorizontalAlignment.Right;
            lastUpdated.VerticalAlignment = VerticalAlignment.Bottom;

            footerLayout = new Grid();
            footerLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footerLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            footerLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
            footerLayout.Children.Add(footerDetails);
            Grid.SetColumn(lastUpdated, 1);
            footerLayout.Children.Add(lastUpdated);
            Grid.SetRow(footerLayout, 4);
            root.Children.Add(footerLayout);

            Grid.SetColumn(root, 1);
            shellLayout.Children.Add(root);
            MountWebPresentation(shellLayout, root);
            AutomationProperties.SetName(shellLayout, "Restic Backup dashboard");
            ShowDashboardPage(selectedDashboardPage);
            ApplyResponsiveLayout();
            return shellLayout;
        }

        private Border BuildNavigationRail()
        {
            Border rail = new Border();
            rail.Background = BackgroundTop;
            rail.BorderBrush = CardBorderBrush;
            rail.BorderThickness = new Thickness(0, 0, 1, 0);
            rail.Padding = new Thickness(18, 22, 18, 16);

            Grid layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            navigationBrand = new TextBlock();
            navigationBrand.Text = "RESTIC";
            navigationBrand.FontSize = 13;
            navigationBrand.FontWeight = FontWeights.Bold;
            navigationBrand.Foreground = BlueText;
            navigationBrand.Margin = new Thickness(8, 2, 0, 0);
            AutomationProperties.SetName(navigationBrand, "Restic Backup navigation");
            layout.Children.Add(navigationBrand);

            StackPanel primary = new StackPanel();
            protectionNavButton = BuildNavigationButton("\uE83D", "Protection", "Protection");
            activityNavButton = BuildNavigationButton("\uE81C", "Activity", "Activity");
            restoreNavButton = BuildNavigationButton("\uE8F3", "Restore", "Restore");
            primary.Children.Add(protectionNavButton);
            primary.Children.Add(activityNavButton);
            primary.Children.Add(restoreNavButton);
            Grid.SetRow(primary, 2);
            layout.Children.Add(primary);

            settingsNavButton = BuildNavigationButton("\uE713", "Settings", "Settings");
            Grid.SetRow(settingsNavButton, 3);
            layout.Children.Add(settingsNavButton);

            rail.Child = layout;
            AutomationProperties.SetName(rail, "Dashboard navigation");
            return rail;
        }

        private Button BuildNavigationButton(string glyph, string label, string page)
        {
            Button button = CreateButton(label);
            button.Tag = new string[] { page, glyph, label };
            button.Content = BuildIconLabel(glyph, label);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 0, 0, 6);
            button.Padding = new Thickness(10, 8, 10, 8);
            button.MinHeight = 40;
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            button.Click += OnNavigationClick;
            AutomationProperties.SetName(button, "Open " + label);
            return button;
        }

        private void OnNavigationClick(object sender, RoutedEventArgs args)
        {
            Button button = sender as Button;
            string[] metadata = button == null ? null : button.Tag as string[];
            if (metadata == null || metadata.Length < 1)
            {
                return;
            }
            ShowDashboardPage(metadata[0]);
        }

        private void ShowDashboardPage(string page)
        {
            selectedDashboardPage = string.IsNullOrWhiteSpace(page) ? "Protection" : page;
            if (protectionOverview != null)
            {
                protectionOverview.Visibility = string.Equals(selectedDashboardPage, "Protection", StringComparison.Ordinal)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            if (historyLayout != null)
            {
                historyLayout.Visibility = string.Equals(selectedDashboardPage, "Activity", StringComparison.Ordinal)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            if (settingsPage != null)
            {
                settingsPage.Visibility = string.Equals(selectedDashboardPage, "Settings", StringComparison.Ordinal)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            if (restorePage != null)
            {
                restorePage.Visibility = string.Equals(selectedDashboardPage, "Restore", StringComparison.Ordinal)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            StyleNavigationButton(protectionNavButton, "Protection");
            StyleNavigationButton(activityNavButton, "Activity");
            StyleNavigationButton(restoreNavButton, "Restore");
            StyleNavigationButton(settingsNavButton, "Settings");
        }

        private void StyleNavigationButton(Button button, string page)
        {
            if (button == null)
            {
                return;
            }
            bool selected = string.Equals(selectedDashboardPage, page, StringComparison.Ordinal);
            button.Background = selected ? themeResolution.Palette.ButtonBackground : Brushes.Transparent;
            button.BorderBrush = selected ? CardBorderBrush : Brushes.Transparent;
            button.Foreground = selected ? PrimaryText : MutedText;
            AutomationProperties.SetItemStatus(button, selected ? "Selected" : "Not selected");
        }

        private Grid BuildProtectionPage()
        {
            Grid page = new Grid();
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 220 });

            Grid pageHeader = new Grid();
            pageHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pageHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel heading = new StackPanel();
            TextBlock title = new TextBlock();
            title.Text = "Protection";
            title.FontSize = 21;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = PrimaryText;
            heading.Children.Add(title);
            TextBlock description = new TextBlock();
            description.Text = "Your current backup state, schedule, and protected folders";
            description.FontSize = 11.5;
            description.Foreground = MutedText;
            description.Margin = new Thickness(0, 2, 0, 0);
            heading.Children.Add(description);
            pageHeader.Children.Add(heading);

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.VerticalAlignment = VerticalAlignment.Center;

            reviewChangesButton = CreateButton("Review changes");
            reviewChangesButton.MinWidth = 126;
            reviewChangesButton.MinHeight = 38;
            reviewChangesButton.Margin = new Thickness(0, 0, 8, 0);
            reviewChangesButton.Background = BrushFrom("#3A2F18");
            reviewChangesButton.BorderBrush = Amber;
            reviewChangesButton.Foreground = Amber;
            reviewChangesButton.Visibility = Visibility.Collapsed;
            reviewChangesButton.Click += OnReviewChangesClick;
            AutomationProperties.SetAutomationId(
                reviewChangesButton,
                "ReviewBackupChangesButton");
            AutomationProperties.SetName(
                reviewChangesButton,
                "Review suspicious backup changes");
            AutomationProperties.SetHelpText(
                reviewChangesButton,
                "Review and acknowledge only this exact generation. The deletion and retention hold remains; DriveFS upload is not paused.");
            actions.Children.Add(reviewChangesButton);

            backupNowButton = CreateButton("Back up now");
            backupNowButton.MinWidth = 116;
            backupNowButton.MinHeight = 38;
            backupNowButton.Background = Blue;
            backupNowButton.BorderBrush = Blue;
            backupNowButton.Foreground = themeResolution.Palette.TextOnAccent;
            backupNowButton.Click += OnBackupNowClick;
            AutomationProperties.SetAutomationId(backupNowButton, "BackupNowButton");
            AutomationProperties.SetName(backupNowButton, "Back up now");
            AutomationProperties.SetHelpText(
                backupNowButton,
                "Start the installed Restic backup now. Windows approval is required.");
            actions.Children.Add(backupNowButton);

            cancelBackupButton = CreateButton("Cancel backup");
            cancelBackupButton.Margin = new Thickness(8, 0, 0, 0);
            cancelBackupButton.MinWidth = 112;
            cancelBackupButton.MinHeight = 38;
            cancelBackupButton.Background = Brushes.Transparent;
            cancelBackupButton.BorderBrush = CardBorderBrush;
            cancelBackupButton.Foreground = MutedText;
            cancelBackupButton.Visibility = Visibility.Visible;
            cancelBackupButton.Click += OnCancelBackupClick;
            AutomationProperties.SetAutomationId(cancelBackupButton, "CancelBackupButton");
            AutomationProperties.SetName(cancelBackupButton, "Cancel running backup");
            AutomationProperties.SetHelpText(
                cancelBackupButton,
                "Available while an exact protected backup run is active.");
            actions.Children.Add(cancelBackupButton);
            Grid.SetColumn(actions, 1);
            pageHeader.Children.Add(actions);
            page.Children.Add(pageHeader);

            UIElement recoveryCard = BuildRepositoryRecoveryCard(true);
            Grid.SetRow(recoveryCard, 2);
            page.Children.Add(recoveryCard);

            protectionHero = (FrameworkElement)BuildHero();
            protectionHero.MinHeight = 168;
            Grid.SetRow(protectionHero, 3);
            page.Children.Add(protectionHero);

            protectionSources = (FrameworkElement)BuildSources();
            Grid.SetRow(protectionSources, 5);
            page.Children.Add(protectionSources);
            AutomationProperties.SetName(page, "Protection page");

            ScrollViewer scroller = new ScrollViewer();
            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroller.PanningMode = PanningMode.VerticalOnly;
            scroller.Content = page;
            AutomationProperties.SetName(scroller, "Scrollable Protection page");

            Grid container = new Grid();
            container.Children.Add(scroller);
            return container;
        }

        private Grid BuildActivityPage()
        {
            Grid page = new Grid();
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            StackPanel heading = new StackPanel();
            TextBlock title = new TextBlock();
            title.Text = "Activity";
            title.FontSize = 21;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = PrimaryText;
            heading.Children.Add(title);
            TextBlock description = new TextBlock();
            description.Text = "Verified local run history and recent trend";
            description.FontSize = 11.5;
            description.Foreground = MutedText;
            description.Margin = new Thickness(0, 2, 0, 0);
            heading.Children.Add(description);
            page.Children.Add(heading);
            UIElement history = BuildHistory();
            Grid.SetRow(history, 2);
            page.Children.Add(history);
            AutomationProperties.SetName(page, "Backup activity page");
            return page;
        }

        private UIElement BuildRestorePage()
        {
            Grid page = new Grid();
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            StackPanel heading = new StackPanel();
            TextBlock title = new TextBlock();
            title.Text = "Restore";
            title.FontSize = 21;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = PrimaryText;
            heading.Children.Add(title);
            TextBlock description = new TextBlock();
            description.Text = "Browse immutable snapshots and recover files without touching live folders";
            description.FontSize = 11.5;
            description.Foreground = MutedText;
            description.Margin = new Thickness(0, 2, 0, 0);
            heading.Children.Add(description);
            page.Children.Add(heading);

            Border readinessCard = CreateCard();
            readinessCard.Padding = new Thickness(20, 18, 20, 18);
            Grid readiness = new Grid();
            readiness.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            readiness.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel readinessCopy = new StackPanel();
            TextBlock readinessEyebrow = new TextBlock();
            readinessEyebrow.Text = "RECOVERY READINESS";
            readinessEyebrow.FontSize = 10;
            readinessEyebrow.FontWeight = FontWeights.Bold;
            readinessEyebrow.Foreground = BlueText;
            readinessCopy.Children.Add(readinessEyebrow);
            restoreReadinessTitle = new TextBlock();
            restoreReadinessTitle.Text = "Checking protected restore components";
            restoreReadinessTitle.FontSize = 18;
            restoreReadinessTitle.FontWeight = FontWeights.SemiBold;
            restoreReadinessTitle.Foreground = PrimaryText;
            restoreReadinessTitle.Margin = new Thickness(0, 5, 0, 0);
            readinessCopy.Children.Add(restoreReadinessTitle);
            restoreReadinessDetail = new TextBlock();
            restoreReadinessDetail.Text = "The dashboard is validating the stable backup plan and restore manager.";
            restoreReadinessDetail.FontSize = 12;
            restoreReadinessDetail.Foreground = MutedText;
            restoreReadinessDetail.TextWrapping = TextWrapping.Wrap;
            restoreReadinessDetail.Margin = new Thickness(0, 4, 16, 0);
            readinessCopy.Children.Add(restoreReadinessDetail);
            readiness.Children.Add(readinessCopy);
            StackPanel recoveryActions = new StackPanel();
            recoveryActions.VerticalAlignment = VerticalAlignment.Center;
            checkRecoveryReadinessButton = CreateButton("Check readiness");
            checkRecoveryReadinessButton.MinWidth = 164;
            checkRecoveryReadinessButton.MinHeight = 38;
            checkRecoveryReadinessButton.Margin = new Thickness(0, 0, 0, 8);
            checkRecoveryReadinessButton.Click += OnCheckRecoveryReadinessClick;
            AutomationProperties.SetName(checkRecoveryReadinessButton, "Check independent recovery readiness");
            AutomationProperties.SetHelpText(
                checkRecoveryReadinessButton,
                "Read-only checks validate the repository, both credentials, recovery bundle, capacity, locks, and restore drill.");
            recoveryActions.Children.Add(checkRecoveryReadinessButton);
            openRestoreCenterButton = CreateButton("Open Restore Center");
            openRestoreCenterButton.MinWidth = 164;
            openRestoreCenterButton.MinHeight = 42;
            openRestoreCenterButton.Background = Blue;
            openRestoreCenterButton.BorderBrush = Blue;
            openRestoreCenterButton.Foreground = themeResolution.Palette.TextOnAccent;
            openRestoreCenterButton.VerticalAlignment = VerticalAlignment.Center;
            openRestoreCenterButton.Click += OnOpenRestoreCenterClick;
            AutomationProperties.SetName(openRestoreCenterButton, "Open protected Restore Center");
            AutomationProperties.SetHelpText(
                openRestoreCenterButton,
                "Browse plan-bound snapshots and restore to a new or empty destination. Windows approval is required.");
            recoveryActions.Children.Add(openRestoreCenterButton);
            Grid.SetColumn(recoveryActions, 1);
            readiness.Children.Add(recoveryActions);
            readinessCard.Child = readiness;
            Grid.SetRow(readinessCard, 2);
            page.Children.Add(readinessCard);

            Border safetyCard = CreateCard();
            safetyCard.Padding = new Thickness(20, 18, 20, 18);
            StackPanel safety = new StackPanel();
            TextBlock safetyTitle = new TextBlock();
            safetyTitle.Text = "Restore safety contract";
            safetyTitle.FontSize = 16;
            safetyTitle.FontWeight = FontWeights.SemiBold;
            safetyTitle.Foreground = PrimaryText;
            safety.Children.Add(safetyTitle);
            string[] guarantees =
            {
                "Exact immutable snapshot ID — never a moving 'latest' selector",
                "Separate destination only — original folders and repository stay untouched",
                "Destination must be absent or empty, and Restic is told never to overwrite",
                "Every successful restore is verified; partial results are retained and clearly labelled"
            };
            foreach (string guarantee in guarantees)
            {
                Border row = new Border();
                row.Background = CardSoftBrush;
                row.BorderBrush = CardBorderBrush;
                row.BorderThickness = new Thickness(1);
                row.CornerRadius = new CornerRadius(10);
                row.Padding = new Thickness(12, 10, 12, 10);
                row.Margin = new Thickness(0, 10, 0, 0);
                TextBlock text = new TextBlock();
                text.Text = "✓  " + guarantee;
                text.FontSize = 12;
                text.Foreground = PrimaryText;
                text.TextWrapping = TextWrapping.Wrap;
                row.Child = text;
                safety.Children.Add(row);
            }
            safetyCard.Child = safety;
            Grid.SetRow(safetyCard, 4);
            page.Children.Add(safetyCard);
            AutomationProperties.SetName(page, "Restore page");
            return page;
        }

        private UIElement BuildSettingsPage()
        {
            ScrollViewer scroller = new ScrollViewer();
            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            StackPanel page = new StackPanel();
            page.MaxWidth = 720;
            page.HorizontalAlignment = HorizontalAlignment.Left;

            TextBlock title = new TextBlock();
            title.Text = "Settings";
            title.FontSize = 21;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = PrimaryText;
            page.Children.Add(title);
            TextBlock description = new TextBlock();
            description.Text = "Backup storage, off-site verification, appearance, and dashboard motion";
            description.FontSize = 11.5;
            description.Foreground = MutedText;
            description.Margin = new Thickness(0, 2, 0, 14);
            page.Children.Add(description);

            UIElement recoveryCard = BuildRepositoryRecoveryCard(false);
            page.Children.Add(recoveryCard);
            page.Children.Add(BuildStorageCard());
            page.Children.Add(BuildFreshnessCard());
            page.Children.Add(BuildOffsiteCard());

            Border appearanceCard = CreateCard();
            appearanceCard.Padding = new Thickness(18, 16, 18, 16);
            appearanceCard.Margin = new Thickness(0, 12, 0, 0);
            StackPanel appearance = new StackPanel();
            TextBlock appearanceTitle = new TextBlock();
            appearanceTitle.Text = "Appearance";
            appearanceTitle.FontSize = 16;
            appearanceTitle.FontWeight = FontWeights.SemiBold;
            appearanceTitle.Foreground = PrimaryText;
            appearance.Children.Add(appearanceTitle);
            TextBlock appearanceHint = new TextBlock();
            appearanceHint.Text = "Follow Windows or keep a fixed light or dark dashboard.";
            appearanceHint.FontSize = 12;
            appearanceHint.Foreground = MutedText;
            appearanceHint.Margin = new Thickness(0, 3, 0, 10);
            appearance.Children.Add(appearanceHint);
            appearance.Children.Add(BuildThemeSelector());
            appearanceCard.Child = appearance;
            page.Children.Add(appearanceCard);

            Border motionCard = CreateCard();
            motionCard.Padding = new Thickness(18, 16, 18, 16);
            motionCard.Margin = new Thickness(0, 12, 0, 0);
            Grid motion = new Grid();
            motion.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            motion.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel motionCopy = new StackPanel();
            TextBlock motionTitle = new TextBlock();
            motionTitle.Text = "Progress animation";
            motionTitle.FontSize = 16;
            motionTitle.FontWeight = FontWeights.SemiBold;
            motionTitle.Foreground = PrimaryText;
            motionCopy.Children.Add(motionTitle);
            TextBlock motionHint = new TextBlock();
            motionHint.Text = "Preview real dashboard states without starting a backup.";
            motionHint.FontSize = 12;
            motionHint.Foreground = MutedText;
            motionHint.Margin = new Thickness(0, 3, 18, 0);
            motionCopy.Children.Add(motionHint);
            motion.Children.Add(motionCopy);
            previewButton = CreateButton(previewEnabled ? "Stop preview" : "Preview animation");
            previewButton.MinHeight = 38;
            previewButton.Click += delegate
            {
                previewEnabled = !previewEnabled;
                previewStarted = DateTime.Now;
                previewButton.Content = previewEnabled ? "Stop preview" : "Preview animation";
                AutomationProperties.SetName(
                    previewButton,
                    previewEnabled ? "Stop backup flow preview" : "Preview backup flow");
                ShowDashboardPage("Protection");
                RefreshDashboard();
            };
            AutomationProperties.SetHelpText(
                previewButton,
                "Shows a clearly labeled animated example without starting a backup.");
            Grid.SetColumn(previewButton, 1);
            motion.Children.Add(previewButton);
            motionCard.Child = motion;
            page.Children.Add(motionCard);

            Border safetyCard = CreateCard();
            safetyCard.Padding = new Thickness(18, 16, 18, 16);
            safetyCard.Margin = new Thickness(0, 12, 0, 0);
            TextBlock safety = new TextBlock();
            safety.Text = "Folder, schedule, start, and cancellation requests use protected Windows flows. Closing this dashboard never terminates Restic.";
            safety.FontSize = 12;
            safety.Foreground = MutedText;
            safety.TextWrapping = TextWrapping.Wrap;
            safetyCard.Child = safety;
            page.Children.Add(safetyCard);

            scroller.Content = page;
            AutomationProperties.SetName(scroller, "Dashboard settings page");
            return scroller;
        }

        private UIElement BuildRepositoryRecoveryCard(bool protectionSurface)
        {
            Border card = CreateCard();
            card.BorderBrush = Rose;
            card.BorderThickness = new Thickness(2);
            card.Background = CardSoftBrush;
            card.Padding = new Thickness(16, 14, 16, 14);
            card.Margin = protectionSurface
                ? new Thickness(0, 0, 0, 10)
                : new Thickness(0, 0, 0, 12);
            card.Visibility = Visibility.Collapsed;

            Grid content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel copy = new StackPanel();
            TextBlock eyebrow = new TextBlock();
            eyebrow.Text = "ACTION REQUIRED  \u2022  BACKUPS PAUSED";
            eyebrow.FontSize = 10;
            eyebrow.FontWeight = FontWeights.Bold;
            eyebrow.Foreground = Rose;
            copy.Children.Add(eyebrow);

            TextBlock title = new TextBlock();
            title.Text = "Interrupted repository move";
            title.FontSize = 16;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = PrimaryText;
            title.Margin = new Thickness(0, 4, 12, 0);
            copy.Children.Add(title);

            TextBlock message = new TextBlock();
            message.Text = "Repair the protected repository state before another backup runs.";
            message.FontSize = 12;
            message.Foreground = MutedText;
            message.Margin = new Thickness(0, 3, 18, 0);
            message.TextWrapping = TextWrapping.Wrap;
            AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Assertive);
            copy.Children.Add(message);
            content.Children.Add(copy);

            Button repairButton = CreateButton("Repair interrupted move");
            repairButton.MinHeight = 38;
            repairButton.MinWidth = 178;
            repairButton.VerticalAlignment = VerticalAlignment.Center;
            repairButton.Background = Rose;
            repairButton.BorderBrush = Rose;
            repairButton.Foreground = themeResolution.Palette.TextOnAccent;
            repairButton.Click += OnRepairRepositoryClick;
            AutomationProperties.SetAutomationId(
                repairButton,
                protectionSurface
                    ? "ProtectionRepairRepositoryButton"
                    : "SettingsRepairRepositoryButton");
            AutomationProperties.SetName(repairButton, "Repair interrupted repository move");
            Grid.SetColumn(repairButton, 1);
            content.Children.Add(repairButton);

            StackPanel progressPanel = new StackPanel();
            progressPanel.Margin = new Thickness(0, 11, 0, 0);
            ProgressBar progress = new ProgressBar();
            progress.Minimum = 0;
            progress.Maximum = 100;
            progress.Height = 6;
            progress.Foreground = Blue;
            progress.Background = CardBorderBrush;
            progress.IsIndeterminate = true;
            progress.Visibility = Visibility.Collapsed;
            AutomationProperties.SetAutomationId(
                progress,
                protectionSurface
                    ? "ProtectionRepositoryRecoveryProgress"
                    : "SettingsRepositoryRecoveryProgress");
            progressPanel.Children.Add(progress);

            TextBlock state = new TextBlock();
            state.Text = "Waiting for protected recovery status.";
            state.FontSize = 11;
            state.FontWeight = FontWeights.SemiBold;
            state.Foreground = PrimaryText;
            state.Margin = new Thickness(0, 5, 0, 0);
            state.TextWrapping = TextWrapping.Wrap;
            AutomationProperties.SetLiveSetting(state, AutomationLiveSetting.Assertive);
            progressPanel.Children.Add(state);
            Grid.SetRow(progressPanel, 1);
            Grid.SetColumnSpan(progressPanel, 2);
            content.Children.Add(progressPanel);

            card.Child = content;
            AutomationProperties.SetAutomationId(
                card,
                protectionSurface
                    ? "ProtectionRepositoryRecoveryCard"
                    : "SettingsRepositoryRecoveryCard");
            AutomationProperties.SetName(
                card,
                "Interrupted repository move. Backups are paused until protected recovery succeeds.");
            AutomationProperties.SetHelpText(
                card,
                "Use the repair action. There is no dismiss or journal deletion action.");

            if (protectionSurface)
            {
                protectionRepositoryRecoveryCard = card;
                protectionRepositoryRecoveryMessage = message;
                protectionRepositoryRecoveryState = state;
                protectionRepositoryRecoveryButton = repairButton;
                protectionRepositoryRecoveryProgress = progress;
            }
            else
            {
                settingsRepositoryRecoveryCard = card;
                settingsRepositoryRecoveryMessage = message;
                settingsRepositoryRecoveryState = state;
                settingsRepositoryRecoveryButton = repairButton;
                settingsRepositoryRecoveryProgress = progress;
            }
            return card;
        }

        private UIElement BuildStorageCard()
        {
            Border card = CreateCard();
            card.Padding = new Thickness(18, 16, 18, 16);
            Grid content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel copy = new StackPanel();
            TextBlock title = new TextBlock();
            title.Text = "Backup storage";
            title.FontSize = 16;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = PrimaryText;
            copy.Children.Add(title);
            TextBlock hint = new TextBlock();
            hint.Text = "Where the encrypted Restic repository is stored.";
            hint.FontSize = 12;
            hint.Foreground = MutedText;
            hint.Margin = new Thickness(0, 3, 18, 0);
            copy.Children.Add(hint);
            settingsRepositoryValue = new TextBlock();
            settingsRepositoryValue.Text = "Loading destination…";
            settingsRepositoryValue.FontSize = 13;
            settingsRepositoryValue.FontWeight = FontWeights.SemiBold;
            settingsRepositoryValue.Foreground = PrimaryText;
            settingsRepositoryValue.TextWrapping = TextWrapping.Wrap;
            settingsRepositoryValue.Margin = new Thickness(0, 12, 18, 0);
            copy.Children.Add(settingsRepositoryValue);
            settingsRepositoryVolume = new TextBlock();
            settingsRepositoryVolume.Text = "Reading storage volume…";
            settingsRepositoryVolume.FontSize = 11;
            settingsRepositoryVolume.Foreground = MutedText;
            settingsRepositoryVolume.Margin = new Thickness(0, 3, 18, 0);
            copy.Children.Add(settingsRepositoryVolume);
            content.Children.Add(copy);

            settingsChangeRepositoryButton = CreateButton("Change location");
            settingsChangeRepositoryButton.MinHeight = 38;
            settingsChangeRepositoryButton.VerticalAlignment = VerticalAlignment.Center;
            settingsChangeRepositoryButton.Click += OnChangeRepositoryClick;
            AutomationProperties.SetAutomationId(
                settingsChangeRepositoryButton,
                "SettingsChangeRepositoryButton");
            AutomationProperties.SetName(
                settingsChangeRepositoryButton,
                "Change protected backup repository location");
            Grid.SetColumn(settingsChangeRepositoryButton, 1);
            content.Children.Add(settingsChangeRepositoryButton);
            card.Child = content;
            return card;
        }

        private UIElement BuildFreshnessCard()
        {
            Border card = CreateCard();
            card.Padding = new Thickness(18, 16, 18, 16);
            card.Margin = new Thickness(0, 12, 0, 0);
            StackPanel content = new StackPanel();

            TextBlock title = new TextBlock();
            title.Text = "Backup freshness";
            title.FontSize = 16;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = PrimaryText;
            content.Children.Add(title);

            TextBlock hint = new TextBlock();
            hint.Text = "Compares the last verified backup with the installed local schedule.";
            hint.FontSize = 12;
            hint.Foreground = MutedText;
            hint.Margin = new Thickness(0, 3, 0, 10);
            hint.TextWrapping = TextWrapping.Wrap;
            content.Children.Add(hint);

            settingsFreshnessStatus = new TextBlock();
            settingsFreshnessStatus.Text = "Checking freshness...";
            settingsFreshnessStatus.FontSize = 14;
            settingsFreshnessStatus.FontWeight = FontWeights.SemiBold;
            settingsFreshnessStatus.Foreground = BlueText;
            AutomationProperties.SetAutomationId(
                settingsFreshnessStatus,
                "SettingsBackupFreshnessStatus");
            AutomationProperties.SetName(
                settingsFreshnessStatus,
                "Backup freshness status: checking");
            AutomationProperties.SetLiveSetting(
                settingsFreshnessStatus,
                AutomationLiveSetting.Polite);
            content.Children.Add(settingsFreshnessStatus);

            settingsFreshnessDetail = new TextBlock();
            settingsFreshnessDetail.Text = "Waiting for protected backup telemetry and schedule metadata.";
            settingsFreshnessDetail.FontSize = 12;
            settingsFreshnessDetail.Foreground = MutedText;
            settingsFreshnessDetail.Margin = new Thickness(0, 3, 0, 0);
            settingsFreshnessDetail.TextWrapping = TextWrapping.Wrap;
            AutomationProperties.SetName(
                settingsFreshnessDetail,
                "Backup freshness detail");
            content.Children.Add(settingsFreshnessDetail);

            card.Child = content;
            AutomationProperties.SetName(card, "Backup freshness settings");
            AutomationProperties.SetHelpText(
                card,
                "Shows whether verified backups are keeping up with the installed daily or selected-weekday schedule.");
            return card;
        }

        private UIElement BuildOffsiteCard()
        {
            Border card = CreateCard();
            card.Padding = new Thickness(18, 16, 18, 16);
            card.Margin = new Thickness(0, 12, 0, 0);
            StackPanel content = new StackPanel();

            TextBlock title = new TextBlock();
            title.Text = "Google Drive verification";
            title.FontSize = 16;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = PrimaryText;
            content.Children.Add(title);

            TextBlock hint = new TextBlock();
            hint.Text =
                "Confirms the live My Drive repository through a read-only API inventory and independent direct-cloud restore. No local mirror is copied.";
            hint.FontSize = 12;
            hint.Foreground = MutedText;
            hint.Margin = new Thickness(0, 3, 0, 10);
            hint.TextWrapping = TextWrapping.Wrap;
            content.Children.Add(hint);

            settingsOffsiteStatus = new TextBlock();
            settingsOffsiteStatus.Text = "Checking direct cloud verification...";
            settingsOffsiteStatus.FontSize = 14;
            settingsOffsiteStatus.FontWeight = FontWeights.SemiBold;
            settingsOffsiteStatus.Foreground = BlueText;
            AutomationProperties.SetAutomationId(
                settingsOffsiteStatus,
                "SettingsOffsiteStatus");
            AutomationProperties.SetLiveSetting(
                settingsOffsiteStatus,
                AutomationLiveSetting.Polite);
            content.Children.Add(settingsOffsiteStatus);

            settingsOffsiteDetail = new TextBlock();
            settingsOffsiteDetail.Text = "Waiting for direct Google Drive evidence.";
            settingsOffsiteDetail.FontSize = 12;
            settingsOffsiteDetail.Foreground = MutedText;
            settingsOffsiteDetail.Margin = new Thickness(0, 3, 0, 0);
            settingsOffsiteDetail.TextWrapping = TextWrapping.Wrap;
            content.Children.Add(settingsOffsiteDetail);

            settingsOffsiteEvidence = new TextBlock();
            settingsOffsiteEvidence.Text =
                "No verified direct-cloud inventory or restore evidence is available yet.";
            settingsOffsiteEvidence.FontSize = 11;
            settingsOffsiteEvidence.Foreground = MutedText;
            settingsOffsiteEvidence.Margin = new Thickness(0, 8, 0, 0);
            settingsOffsiteEvidence.TextWrapping = TextWrapping.Wrap;
            content.Children.Add(settingsOffsiteEvidence);

            card.Child = content;
            AutomationProperties.SetName(card, "Google Drive verification status");
            AutomationProperties.SetHelpText(
                card,
                "Shows exact Drive API inventory and independent direct-cloud restore verification for the live My Drive repository.");
            return card;
        }

        private UIElement BuildHeader()
        {
            headerLayout = new Grid();
            headerLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            headerLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
            headerLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });

            StackPanel titlePanel = new StackPanel();
            headerTitlePanel = titlePanel;
            TextBlock title = new TextBlock();
            title.Text = "Rewindle";
            title.FontSize = 21;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = PrimaryText;
            titlePanel.Children.Add(title);
            headerSubtitle = new TextBlock();
            headerSubtitle.Text = "Loading backup scope  •  Reading installed schedule";
            headerSubtitle.FontSize = 11;
            headerSubtitle.Foreground = MutedText;
            headerSubtitle.Margin = new Thickness(1, 1, 0, 0);
            headerSubtitle.TextTrimming = TextTrimming.CharacterEllipsis;
            titlePanel.Children.Add(headerSubtitle);
            headerLayout.Children.Add(titlePanel);

            StackPanel actions = new StackPanel();
            headerActions = actions;
            actions.Orientation = Orientation.Horizontal;
            actions.VerticalAlignment = VerticalAlignment.Center;
            Button refresh = CreateButton(string.Empty);
            refresh.Width = 40;
            refresh.MinHeight = 38;
            TextBlock refreshIcon = new TextBlock();
            refreshIcon.Text = "\uE72C";
            refreshIcon.FontFamily = new FontFamily("Segoe MDL2 Assets");
            refreshIcon.FontSize = 15;
            refresh.Content = refreshIcon;
            refresh.ToolTip = "Refresh now (F5)";
            refresh.Click += delegate { RefreshDashboard(); };
            AutomationProperties.SetName(refresh, "Refresh backup dashboard");
            AutomationProperties.SetHelpText(
                refresh,
                "Refresh backup status, protected folders, schedule, and run history. Shortcut: F5.");
            actions.Children.Add(refresh);

            Grid.SetColumn(actions, 1);
            headerLayout.Children.Add(actions);
            return headerLayout;
        }

        private UIElement BuildIconLabel(string glyph, string label)
        {
            StackPanel content = new StackPanel();
            content.Orientation = Orientation.Horizontal;
            TextBlock icon = new TextBlock();
            icon.Text = glyph;
            icon.FontFamily = new FontFamily("Segoe MDL2 Assets");
            icon.FontSize = 13;
            icon.Margin = string.IsNullOrEmpty(label)
                ? new Thickness(0)
                : new Thickness(0, 0, 7, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(icon);
            if (!string.IsNullOrEmpty(label))
            {
                TextBlock text = new TextBlock();
                text.Text = label;
                text.VerticalAlignment = VerticalAlignment.Center;
                content.Children.Add(text);
            }
            return content;
        }

        private UIElement BuildThemeSelector()
        {
            StackPanel group = new StackPanel();
            group.VerticalAlignment = VerticalAlignment.Center;

            Border frame = new Border();
            frame.Background = CardSoftBrush;
            frame.BorderBrush = CardBorderBrush;
            frame.BorderThickness = new Thickness(1);
            frame.CornerRadius = new CornerRadius(18);
            frame.Padding = new Thickness(2);

            StackPanel segments = new StackPanel();
            segments.Orientation = Orientation.Horizontal;
            AddThemeSegment(segments, DashboardThemePreference.System, "System");
            AddThemeSegment(segments, DashboardThemePreference.Midnight, "Midnight");
            AddThemeSegment(segments, DashboardThemePreference.Daylight, "Daylight");
            frame.Child = segments;
            group.Children.Add(frame);

            AutomationProperties.SetName(group, "Dashboard theme");
            AutomationProperties.SetHelpText(
                group,
                "Choose whether the dashboard follows Windows or uses the Midnight or Daylight theme.");
            return group;
        }

        private void AddThemeSegment(
            Panel parent,
            DashboardThemePreference preference,
            string label)
        {
            Button button = new Button();
            button.Content = label;
            button.Tag = preference;
            button.MinWidth = label.Length > 7 ? 62 : 52;
            button.MinHeight = 32;
            button.Padding = new Thickness(7, 3, 7, 3);
            button.Margin = new Thickness(themeButtons.Count == 0 ? 0 : 2, 0, 0, 0);
            button.BorderThickness = new Thickness(1);
            button.FontSize = 11;
            button.FontWeight = FontWeights.SemiBold;
            button.Cursor = Cursors.Hand;
            ToolTipService.SetShowOnDisabled(button, true);
            ApplyButtonChrome(button);
            button.Click += OnThemeSegmentClick;
            button.GotKeyboardFocus += delegate
            {
                button.BorderBrush = themeResolution.Palette.Focus;
                button.BorderThickness = new Thickness(2);
            };
            button.LostKeyboardFocus += delegate
            {
                StyleThemeSegment(button, (DashboardThemePreference)button.Tag == themePreference);
                button.BorderThickness = new Thickness(1);
            };
            AutomationProperties.SetName(button, "Use " + label + " dashboard theme");
            AutomationProperties.SetHelpText(
                button,
                preference == DashboardThemePreference.System
                    ? "Follow the Windows light, dark, and high contrast setting."
                    : "Use the " + label + " dashboard theme.");
            themeButtons[preference] = button;
            StyleThemeSegment(button, preference == themePreference);
            parent.Children.Add(button);
        }

        private void StyleThemeSegment(Button button, bool selected)
        {
            button.Background = selected ? Teal : Brushes.Transparent;
            button.Foreground = selected
                ? themeResolution.Palette.TextOnAccent
                : PrimaryText;
            button.BorderBrush = selected ? Teal : Brushes.Transparent;
            button.FontWeight = selected ? FontWeights.Bold : FontWeights.SemiBold;
            AutomationProperties.SetItemStatus(button, selected ? "Selected" : "Not selected");
        }

        private UIElement BuildHero()
        {
            Border card = CreateCard();
            Grid grid = new Grid();
            grid.Margin = new Thickness(18, 14, 18, 12);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid summary = new Grid();

            StackPanel left = new StackPanel();
            Grid statusLine = new Grid();
            statusLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statusLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock heroEyebrow = new TextBlock();
            heroEyebrow.Text = "Protection status";
            heroEyebrow.FontSize = 11;
            heroEyebrow.FontWeight = FontWeights.SemiBold;
            heroEyebrow.Foreground = BlueText;
            heroEyebrow.VerticalAlignment = VerticalAlignment.Center;
            statusLine.Children.Add(heroEyebrow);
            statusBadge = BuildStatusBadge();
            Grid.SetColumn(statusBadge, 1);
            statusLine.Children.Add(statusBadge);
            left.Children.Add(statusLine);
            heroTitle = new TextBlock();
            heroTitle.Text = "Reading protected backup state…";
            heroTitle.FontSize = 20;
            heroTitle.FontWeight = FontWeights.SemiBold;
            heroTitle.Foreground = PrimaryText;
            heroTitle.Margin = new Thickness(0, 5, 0, 0);
            AutomationProperties.SetLiveSetting(heroTitle, AutomationLiveSetting.Polite);
            left.Children.Add(heroTitle);

            heroDetail = new TextBlock();
            heroDetail.Text = "Live progress will appear automatically when Restic starts.";
            heroDetail.FontSize = 12;
            heroDetail.Foreground = MutedText;
            heroDetail.Margin = new Thickness(0, 4, 16, 0);
            heroDetail.TextWrapping = TextWrapping.Wrap;
            left.Children.Add(heroDetail);

            cancelActionStatus = new TextBlock();
            cancelActionStatus.FontSize = 11;
            cancelActionStatus.Foreground = MutedText;
            cancelActionStatus.Margin = new Thickness(0, 6, 16, 0);
            cancelActionStatus.TextWrapping = TextWrapping.Wrap;
            cancelActionStatus.Visibility = Visibility.Collapsed;
            AutomationProperties.SetLiveSetting(cancelActionStatus, AutomationLiveSetting.Polite);
            left.Children.Add(cancelActionStatus);
            summary.Children.Add(left);

            StackPanel etaPanel = new StackPanel();
            etaPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            etaTitle = new TextBlock();
            etaTitle.Text = "Next automatic run";
            etaTitle.FontSize = 11;
            etaTitle.FontWeight = FontWeights.SemiBold;
            etaTitle.Foreground = MutedText;
            etaPanel.Children.Add(etaTitle);
            etaValue = new TextBlock();
            etaValue.Text = "—";
            etaValue.FontSize = 20;
            etaValue.FontWeight = FontWeights.SemiBold;
            etaValue.Foreground = PrimaryText;
            etaValue.Margin = new Thickness(0, 3, 0, 0);
            etaPanel.Children.Add(etaValue);
            etaHint = new TextBlock();
            etaHint.Text = "Waiting for backup state";
            etaHint.FontSize = 11;
            etaHint.Foreground = MutedText;
            etaHint.Margin = new Thickness(0, 2, 0, 0);
            etaHint.TextWrapping = TextWrapping.Wrap;
            etaPanel.Children.Add(etaHint);
            grid.Children.Add(summary);

            Border planFacts = new Border();
            planFacts.Background = CardSoftBrush;
            planFacts.BorderBrush = CardBorderBrush;
            planFacts.BorderThickness = new Thickness(0, 1, 0, 1);
            planFacts.Margin = new Thickness(0, 10, 0, 0);
            planFacts.Padding = new Thickness(10, 8, 10, 8);
            Grid scheduleMeta = new Grid();
            scheduleMeta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            scheduleMeta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            scheduleMeta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            scheduleMeta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel schedule = new StackPanel();
            schedule.Children.Add(etaPanel);
            scheduleActionStatus = new TextBlock();
            scheduleActionStatus.Text = "Reading installed schedule\u2026";
            scheduleActionStatus.FontSize = 10;
            scheduleActionStatus.Foreground = MutedText;
            scheduleActionStatus.Margin = new Thickness(0, 2, 0, 0);
            scheduleActionStatus.TextTrimming = TextTrimming.CharacterEllipsis;
            AutomationProperties.SetLiveSetting(
                scheduleActionStatus,
                AutomationLiveSetting.Polite);
            schedule.Children.Add(scheduleActionStatus);
            protectionFreshnessStatus = new TextBlock();
            protectionFreshnessStatus.Text = "Backup freshness: checking...";
            protectionFreshnessStatus.FontSize = 10;
            protectionFreshnessStatus.FontWeight = FontWeights.SemiBold;
            protectionFreshnessStatus.Foreground = BlueText;
            protectionFreshnessStatus.Margin = new Thickness(0, 3, 0, 0);
            protectionFreshnessStatus.TextTrimming = TextTrimming.CharacterEllipsis;
            AutomationProperties.SetAutomationId(
                protectionFreshnessStatus,
                "ProtectionBackupFreshnessStatus");
            AutomationProperties.SetName(
                protectionFreshnessStatus,
                "Backup freshness status: checking");
            AutomationProperties.SetHelpText(
                protectionFreshnessStatus,
                "Compares the last verified backup with the installed schedule.");
            AutomationProperties.SetLiveSetting(
                protectionFreshnessStatus,
                AutomationLiveSetting.Polite);
            schedule.Children.Add(protectionFreshnessStatus);
            scheduleMeta.Children.Add(schedule);
            StackPanel destination = new StackPanel();
            TextBlock repositoryLabel = new TextBlock();
            repositoryLabel.Text = "Destination";
            repositoryLabel.FontSize = 10;
            repositoryLabel.Foreground = MutedText;
            destination.Children.Add(repositoryLabel);
            repositoryValue = new TextBlock();
            repositoryValue.Text = "Loading repository…";
            repositoryValue.FontSize = 11;
            repositoryValue.Foreground = PrimaryText;
            repositoryValue.TextTrimming = TextTrimming.CharacterEllipsis;
            destination.Children.Add(repositoryValue);
            Grid.SetColumn(destination, 2);
            scheduleMeta.Children.Add(destination);
            StackPanel planActions = new StackPanel();
            planActions.Orientation = Orientation.Horizontal;
            planActions.Margin = new Thickness(12, 0, 0, 0);
            planActions.VerticalAlignment = VerticalAlignment.Center;
            changeRepositoryButton = CreateButton("Change location");
            changeRepositoryButton.MinHeight = 36;
            changeRepositoryButton.Background = Blue;
            changeRepositoryButton.BorderBrush = Blue;
            changeRepositoryButton.Foreground = themeResolution.Palette.TextOnAccent;
            changeRepositoryButton.Click += OnChangeRepositoryClick;
            AutomationProperties.SetAutomationId(changeRepositoryButton, "ChangeRepositoryButton");
            AutomationProperties.SetName(
                changeRepositoryButton,
                "Change protected backup repository location");
            AutomationProperties.SetHelpText(
                changeRepositoryButton,
                "Copy and verify the Restic repository in a new location. The old repository is retained and no backup starts.");
            planActions.Children.Add(changeRepositoryButton);
            editScheduleButton = CreateButton("Edit schedule");
            editScheduleButton.Margin = new Thickness(8, 0, 0, 0);
            editScheduleButton.MinHeight = 36;
            editScheduleButton.Click += OnEditScheduleClick;
            AutomationProperties.SetAutomationId(editScheduleButton, "EditScheduleButton");
            AutomationProperties.SetName(editScheduleButton, "Edit automatic backup schedule");
            AutomationProperties.SetHelpText(
                editScheduleButton,
                "Change the protected Windows backup schedule. Saving does not start a backup.");
            planActions.Children.Add(editScheduleButton);
            Grid.SetColumn(planActions, 3);
            scheduleMeta.Children.Add(planActions);
            planFacts.Child = scheduleMeta;
            Grid.SetRow(planFacts, 1);
            grid.Children.Add(planFacts);

            StackPanel progressPanel = new StackPanel();
            activeProgressPanel = progressPanel;
            progressPanel.Margin = new Thickness(0, 12, 0, 8);

            progressTrack = new Border();
            progressTrack.Height = 8;
            progressTrack.CornerRadius = new CornerRadius(4);
            progressTrack.Background = BrushFrom("#25334A");
            progressTrack.ClipToBounds = true;
            progressTrack.SizeChanged += delegate { AnimateProgress(currentProgress, false); };

            progressFill = new Border();
            progressFill.HorizontalAlignment = HorizontalAlignment.Left;
            progressFill.Width = 0;
            progressFill.CornerRadius = new CornerRadius(4);
            progressFill.Background = Teal;
            progressFill.ClipToBounds = true;
            Rectangle shimmer = new Rectangle();
            shimmer.Width = 170;
            shimmer.HorizontalAlignment = HorizontalAlignment.Left;
            shimmer.Fill = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(92, 255, 255, 255), 0.5),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)
                },
                new Point(0, 0.5),
                new Point(1, 0.5));
            shimmerTransform = new TranslateTransform(-190, 0);
            shimmer.RenderTransform = shimmerTransform;
            progressFill.Child = shimmer;
            progressTrack.Child = progressFill;
            progressPanel.Children.Add(progressTrack);

            Grid progressMeta = new Grid();
            progressMeta.Margin = new Thickness(0, 5, 0, 6);
            progressMeta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            progressMeta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            progressPercent = new TextBlock();
            progressPercent.Text = "0%";
            progressPercent.FontSize = 12;
            progressPercent.FontWeight = FontWeights.SemiBold;
            progressPercent.Foreground = PrimaryText;
            progressMeta.Children.Add(progressPercent);
            estimateBadge = new TextBlock();
            estimateBadge.Text = "ESTIMATE FROM VALIDATION BASELINE";
            estimateBadge.FontSize = 10;
            estimateBadge.FontWeight = FontWeights.SemiBold;
            estimateBadge.Foreground = Teal;
            estimateBadge.HorizontalAlignment = HorizontalAlignment.Right;
            estimateBadge.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(estimateBadge, 1);
            progressMeta.Children.Add(estimateBadge);
            progressPanel.Children.Add(progressMeta);

            activePhaseRail = (FrameworkElement)BuildPhaseRail();
            activePhaseRail.Visibility = Visibility.Collapsed;
            Grid.SetRow(progressPanel, 2);
            grid.Children.Add(progressPanel);

            metricsContext = new TextBlock();
            metricsContext.Text = "Latest recorded run";
            metricsContext.FontSize = 11;
            metricsContext.FontWeight = FontWeights.SemiBold;
            metricsContext.Foreground = MutedText;
            metricsContext.Margin = new Thickness(0, 6, 0, 2);
            Grid.SetRow(metricsContext, 3);
            grid.Children.Add(metricsContext);

            UIElement metrics = BuildMetrics();
            Grid.SetRow(metrics, 4);
            grid.Children.Add(metrics);

            card.Child = grid;
            return card;
        }

        private Border BuildStatusBadge()
        {
            statusDot = new Ellipse();
            statusDot.Width = 7;
            statusDot.Height = 7;
            statusDot.Fill = Blue;
            statusDot.Margin = new Thickness(0, 0, 7, 0);
            statusBadgeText = new TextBlock();
            statusBadgeText.Text = "LOADING";
            statusBadgeText.FontSize = 11;
            statusBadgeText.FontWeight = FontWeights.Bold;
            statusBadgeText.Foreground = PrimaryText;
            StackPanel badgeContent = new StackPanel();
            badgeContent.Orientation = Orientation.Horizontal;
            badgeContent.Children.Add(statusDot);
            badgeContent.Children.Add(statusBadgeText);
            Border badge = new Border();
            badge.Background = BrushFrom("#17243A");
            badge.BorderBrush = CardBorderBrush;
            badge.BorderThickness = new Thickness(1);
            badge.CornerRadius = new CornerRadius(12);
            badge.Padding = new Thickness(9, 4, 9, 4);
            badge.Child = badgeContent;
            AutomationProperties.SetName(badge, "Backup status");
            return badge;
        }

        private UIElement BuildPhaseRail()
        {
            Grid rail = new Grid();
            string[] labels = { "Backup", "Snapshot", "Repository", "Canary", "Complete" };
            for (int index = 0; index < labels.Length; index++)
            {
                rail.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                StackPanel step = new StackPanel();
                step.HorizontalAlignment = HorizontalAlignment.Center;
                Border marker = new Border();
                marker.Width = 18;
                marker.Height = 18;
                marker.CornerRadius = new CornerRadius(9);
                marker.Background = BrushFrom("#25334A");
                marker.BorderBrush = CardBorderBrush;
                marker.BorderThickness = new Thickness(1);
                TextBlock number = new TextBlock();
                number.Text = (index + 1).ToString(CultureInfo.InvariantCulture);
                number.FontSize = 10;
                number.FontWeight = FontWeights.Bold;
                number.Foreground = MutedText;
                number.HorizontalAlignment = HorizontalAlignment.Center;
                number.VerticalAlignment = VerticalAlignment.Center;
                marker.Child = number;
                phaseMarkers.Add(marker);
                step.Children.Add(marker);
                TextBlock label = new TextBlock();
                label.Text = labels[index];
                label.FontSize = 10;
                label.Foreground = MutedText;
                label.Margin = new Thickness(0, 2, 0, 0);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                phaseLabels.Add(label);
                step.Children.Add(label);
                Grid.SetColumn(step, index);
                rail.Children.Add(step);
            }
            return rail;
        }

        private UIElement BuildMetrics()
        {
            metricsLayout = new UniformGrid();
            metricsLayout.Rows = 1;
            metricsLayout.Columns = 5;
            metricsLayout.Margin = new Thickness(0, 8, 0, 0);

            filesValue = new TextBlock();
            bytesValue = new TextBlock();
            speedValue = new TextBlock();
            elapsedValue = new TextBlock();
            errorsValue = new TextBlock();
            AddMetric(metricsLayout, "Files", filesValue, "Items visited", BlueText);
            AddMetric(metricsLayout, "Processed", bytesValue, "Logical source data", Teal);
            AddMetric(metricsLayout, "Throughput", speedValue, "Current average", BlueText);
            AddMetric(metricsLayout, "Elapsed", elapsedValue, "Current or latest run", BlueText);
            AddMetric(metricsLayout, "Errors", errorsValue, "Protected Restic output", Rose);
            return metricsLayout;
        }

        private void AddMetric(
            Panel parent,
            string title,
            TextBlock value,
            string hint,
            Brush accent)
        {
            Border item = new Border();
            item.BorderBrush = CardBorderBrush;
            item.BorderThickness = new Thickness(parent.Children.Count == 0 ? 0 : 1, 0, 0, 0);
            item.Padding = new Thickness(12, 0, 8, 0);
            StackPanel panel = new StackPanel();
            TextBlock heading = new TextBlock();
            heading.Text = title;
            heading.FontSize = 10;
            heading.FontWeight = FontWeights.SemiBold;
            heading.Foreground = MutedText;
            panel.Children.Add(heading);
            value.Text = "—";
            value.FontSize = 15;
            value.FontWeight = FontWeights.SemiBold;
            value.Foreground = PrimaryText;
            value.Margin = new Thickness(0, 2, 0, 0);
            panel.Children.Add(value);
            item.ToolTip = hint;
            item.Child = panel;
            parent.Children.Add(item);
        }

        private UIElement BuildSources()
        {
            Border card = CreateCard();
            card.Padding = new Thickness(16, 14, 16, 12);

            Grid cardGrid = new Grid();
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 120 });
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel heading = new StackPanel();
            TextBlock title = new TextBlock();
            title.Text = "Protected folders";
            title.FontSize = 16;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = PrimaryText;
            heading.Children.Add(title);

            sourceSummary = new TextBlock();
            sourceSummary.Text = "Loading protected configuration...";
            sourceSummary.FontSize = 12;
            sourceSummary.Foreground = MutedText;
            sourceSummary.Margin = new Thickness(0, 2, 0, 0);
            heading.Children.Add(sourceSummary);
            header.Children.Add(heading);

            addSourceButton = CreateButton("Add folder");
            addSourceButton.Background = Teal;
            addSourceButton.Foreground = BrushFrom("#07151D");
            addSourceButton.BorderBrush = BrushFrom("#52D6C6");
            addSourceButton.BorderThickness = new Thickness(1.5);
            addSourceButton.MinHeight = 34;
            addSourceButton.Padding = new Thickness(14, 6, 14, 6);
            addSourceButton.FontSize = 12;
            addSourceButton.Click += OnAddSourceClick;
            AutomationProperties.SetName(addSourceButton, "Add a folder to future backups");
            AutomationProperties.SetHelpText(
                addSourceButton,
                "Choose a folder and request administrator approval to protect it in future backups.");
            Grid.SetColumn(addSourceButton, 1);
            header.Children.Add(addSourceButton);
            cardGrid.Children.Add(header);

            sourceStatus = new TextBlock();
            sourceStatus.Text = "Administrator approval is required to change protected folders.";
            sourceStatus.FontSize = 12;
            sourceStatus.Foreground = MutedText;
            sourceStatus.TextWrapping = TextWrapping.Wrap;
            sourceStatus.Margin = new Thickness(0, 7, 0, 0);
            AutomationProperties.SetLiveSetting(sourceStatus, AutomationLiveSetting.Polite);
            Grid.SetRow(sourceStatus, 1);
            cardGrid.Children.Add(sourceStatus);

            sourceList = new StackPanel();
            TextBlock loading = new TextBlock();
            loading.Text = "Loading protected folders...";
            loading.FontSize = 12;
            loading.Foreground = MutedText;
            loading.Margin = new Thickness(12, 14, 12, 14);
            sourceList.Children.Add(loading);

            Border sourceFrame = new Border();
            sourceFrame.Background = Brushes.Transparent;
            sourceFrame.BorderBrush = CardBorderBrush;
            sourceFrame.BorderThickness = new Thickness(1);
            sourceFrame.CornerRadius = new CornerRadius(10);
            sourceFrame.Padding = new Thickness(0);
            sourceFrame.Margin = new Thickness(0, 9, 0, 0);
            ScrollViewer sourceScroller = new ScrollViewer();
            sourceScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sourceScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sourceScroller.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            sourceScroller.PanningMode = PanningMode.VerticalOnly;
            sourceScroller.CanContentScroll = true;
            sourceScroller.Content = sourceList;
            sourceFrame.Child = sourceScroller;
            AutomationProperties.SetName(sourceFrame, "Folders included in future backups");
            Grid.SetRow(sourceFrame, 2);
            cardGrid.Children.Add(sourceFrame);

            Border safety = new Border();
            safety.Background = Brushes.Transparent;
            safety.BorderThickness = new Thickness(0);
            safety.Padding = new Thickness(0);
            safety.Margin = new Thickness(0, 8, 0, 0);
            TextBlock safetyText = new TextBlock();
            safetyText.Text = "Removing a folder stops future backups. Existing snapshots remain intact.";
            safetyText.FontSize = 12;
            safetyText.Foreground = MutedText;
            safetyText.TextWrapping = TextWrapping.Wrap;
            safety.Child = safetyText;
            AutomationProperties.SetName(safety, safetyText.Text);
            Grid.SetRow(safety, 3);
            cardGrid.Children.Add(safety);

            card.Child = cardGrid;
            AutomationProperties.SetName(card, "Protected folders");
            return card;
        }

        private UIElement BuildSourceRow(BackupSourceView source, int index, bool managerAvailable)
        {
            Border row = new Border();
            row.Background = Brushes.Transparent;
            row.BorderBrush = CardBorderBrush;
            row.BorderThickness = new Thickness(0, 0, 0, 1);
            row.Padding = new Thickness(12, 5, 8, 5);
            row.MinHeight = 44;
            row.ToolTip = source.SourcePath;

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Ellipse marker = new Ellipse();
            marker.Width = 7;
            marker.Height = 7;
            marker.Fill = source.IsProtectedCanary ? Blue : Green;
            marker.Margin = new Thickness(0, 5, 10, 0);
            marker.VerticalAlignment = VerticalAlignment.Top;
            grid.Children.Add(marker);

            StackPanel details = new StackPanel();
            TextBlock name = new TextBlock();
            name.Text = source.DisplayName;
            name.FontSize = 12;
            name.FontWeight = FontWeights.SemiBold;
            name.Foreground = PrimaryText;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            details.Children.Add(name);

            TextBlock path = new TextBlock();
            path.Text = source.ShortPath;
            path.FontSize = 11;
            path.Foreground = MutedText;
            path.TextTrimming = TextTrimming.CharacterEllipsis;
            path.ToolTip = source.SourcePath;
            path.Margin = new Thickness(0, 2, 10, 0);
            details.Children.Add(path);
            Grid.SetColumn(details, 1);
            grid.Children.Add(details);

            if (source.IsProtectedCanary)
            {
                Border required = new Border();
                required.Background = BrushFrom("#172E43");
                required.BorderBrush = BrushFrom("#3B5A70");
                required.BorderThickness = new Thickness(1);
                required.CornerRadius = new CornerRadius(12);
                required.Padding = new Thickness(7, 3, 7, 3);
                required.VerticalAlignment = VerticalAlignment.Center;
                TextBlock requiredText = new TextBlock();
                requiredText.Text = "Required";
                requiredText.FontSize = 10;
                requiredText.FontWeight = FontWeights.SemiBold;
                requiredText.Foreground = BlueText;
                required.Child = requiredText;
                AutomationProperties.SetName(required, "Required restore verification canary");
                Grid.SetColumn(required, 2);
                grid.Children.Add(required);
            }
            else
            {
                Button remove = CreateButton("×");
                remove.Tag = source;
                remove.Margin = new Thickness(10, 0, 0, 0);
                remove.Padding = new Thickness(0);
                remove.Width = 32;
                remove.Height = 32;
                remove.MinHeight = 32;
                remove.FontSize = 18;
                remove.Background = CardSoftBrush;
                remove.BorderBrush = CardBorderBrush;
                remove.Foreground = MutedText;
                remove.IsEnabled = managerAvailable && !sourceOperationInProgress;
                remove.Click += OnRemoveSourceClick;
                remove.MouseEnter += delegate
                {
                    remove.Background = BrushFrom("#2B1C2A");
                    remove.BorderBrush = Rose;
                    remove.Foreground = BrushFrom("#FFB2BC");
                };
                remove.MouseLeave += delegate
                {
                    if (!remove.IsKeyboardFocused)
                    {
                        remove.Background = CardSoftBrush;
                        remove.BorderBrush = CardBorderBrush;
                        remove.Foreground = MutedText;
                    }
                };
                remove.GotKeyboardFocus += delegate
                {
                    remove.BorderBrush = Rose;
                    remove.Foreground = Rose;
                };
                remove.LostKeyboardFocus += delegate
                {
                    remove.Background = CardSoftBrush;
                    remove.BorderBrush = CardBorderBrush;
                    remove.Foreground = MutedText;
                };
                AutomationProperties.SetName(
                    remove,
                    "Remove " + source.DisplayName + " from future backups");
                AutomationProperties.SetHelpText(
                    remove,
                    "Stops future backups of this folder. Existing snapshots remain intact.");
                removeSourceButtons.Add(remove);
                Grid.SetColumn(remove, 2);
                grid.Children.Add(remove);
            }

            row.Child = grid;
            AutomationProperties.SetName(
                row,
                source.DisplayName + ", " + source.SourcePath + ", " + source.RoleLabel);
            return row;
        }

        private bool ShouldShowPendingSourceItem(SourceConfiguration configuration)
        {
            if (!string.Equals(sourceOperationAction, "Add", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(sourceOperationPath) ||
                sourceOperationStage == SourceOperationStage.Idle ||
                sourceOperationStage == SourceOperationStage.Succeeded)
            {
                return false;
            }
            return configuration == null ||
                !configuration.ContainsUserSource(sourceOperationPath);
        }

        private FrameworkElement BuildSourceOperationItem()
        {
            BackupSourceView source = new BackupSourceView(sourceOperationPath, false);
            Border item = new Border();
            item.BorderThickness = new Thickness(2, 0, 0, 1);
            item.ClipToBounds = true;
            item.RenderTransform = new TranslateTransform(0, 0);

            Grid layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });

            Grid folder = new Grid();
            folder.MinHeight = 44;
            folder.Margin = new Thickness(12, 5, 8, 5);
            folder.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            folder.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            folder.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            sourceOperationGlyphFrame = new Border();
            sourceOperationGlyphFrame.Width = 24;
            sourceOperationGlyphFrame.Height = 24;
            sourceOperationGlyphFrame.CornerRadius = new CornerRadius(12);
            sourceOperationGlyphFrame.Margin = new Thickness(0, 4, 10, 0);
            sourceOperationGlyphFrame.VerticalAlignment = VerticalAlignment.Top;
            sourceOperationGlyph = new TextBlock();
            sourceOperationGlyph.HorizontalAlignment = HorizontalAlignment.Center;
            sourceOperationGlyph.VerticalAlignment = VerticalAlignment.Center;
            sourceOperationGlyph.FontSize = 12;
            sourceOperationGlyph.FontWeight = FontWeights.Bold;
            sourceOperationGlyphFrame.Child = sourceOperationGlyph;
            folder.Children.Add(sourceOperationGlyphFrame);

            StackPanel details = new StackPanel();
            TextBlock name = new TextBlock();
            name.Text = source.DisplayName;
            name.FontSize = 12;
            name.FontWeight = FontWeights.SemiBold;
            name.Foreground = PrimaryText;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            details.Children.Add(name);
            TextBlock path = new TextBlock();
            path.Text = source.ShortPath;
            path.FontSize = 11;
            path.Foreground = MutedText;
            path.TextTrimming = TextTrimming.CharacterEllipsis;
            path.ToolTip = source.SourcePath;
            path.Margin = new Thickness(0, 2, 10, 0);
            details.Children.Add(path);
            Grid.SetColumn(details, 1);
            folder.Children.Add(details);

            sourceOperationTag = new Border();
            sourceOperationTag.BorderThickness = new Thickness(1);
            sourceOperationTag.CornerRadius = new CornerRadius(5);
            sourceOperationTag.Padding = new Thickness(7, 3, 7, 3);
            sourceOperationTag.VerticalAlignment = VerticalAlignment.Center;
            sourceOperationTagText = new TextBlock();
            sourceOperationTagText.FontSize = 10;
            sourceOperationTagText.FontWeight = FontWeights.SemiBold;
            sourceOperationTag.Child = sourceOperationTagText;
            Grid.SetColumn(sourceOperationTag, 2);
            folder.Children.Add(sourceOperationTag);
            layout.Children.Add(folder);

            Border statusFrame = new Border();
            statusFrame.Padding = new Thickness(12, 5, 8, 7);
            Grid statusLayout = new Grid();
            statusLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statusLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel copy = new StackPanel();
            sourceOperationStatusText = new TextBlock();
            sourceOperationStatusText.FontSize = 11.5;
            sourceOperationStatusText.FontWeight = FontWeights.SemiBold;
            sourceOperationStatusText.TextTrimming = TextTrimming.CharacterEllipsis;
            AutomationProperties.SetLiveSetting(sourceOperationStatusText, AutomationLiveSetting.Polite);
            copy.Children.Add(sourceOperationStatusText);
            sourceOperationDetailText = new TextBlock();
            sourceOperationDetailText.FontSize = 10.5;
            sourceOperationDetailText.Foreground = MutedText;
            sourceOperationDetailText.TextTrimming = TextTrimming.CharacterEllipsis;
            sourceOperationDetailText.Margin = new Thickness(0, 2, 6, 0);
            copy.Children.Add(sourceOperationDetailText);
            statusLayout.Children.Add(copy);

            sourceOperationRetryButton = CreateButton("Retry");
            sourceOperationRetryButton.MinHeight = 28;
            sourceOperationRetryButton.Padding = new Thickness(10, 3, 10, 3);
            sourceOperationRetryButton.Margin = new Thickness(8, 0, 0, 0);
            sourceOperationRetryButton.VerticalAlignment = VerticalAlignment.Center;
            sourceOperationRetryButton.Click += OnRetrySourceOperationClick;
            AutomationProperties.SetAutomationId(sourceOperationRetryButton, "RetryAddFolderButton");
            Grid.SetColumn(sourceOperationRetryButton, 1);
            statusLayout.Children.Add(sourceOperationRetryButton);

            sourceOperationDismissButton = CreateButton("Dismiss");
            sourceOperationDismissButton.MinHeight = 28;
            sourceOperationDismissButton.Padding = new Thickness(10, 3, 10, 3);
            sourceOperationDismissButton.Margin = new Thickness(6, 0, 0, 0);
            sourceOperationDismissButton.VerticalAlignment = VerticalAlignment.Center;
            sourceOperationDismissButton.Click += OnDismissSourceOperationClick;
            AutomationProperties.SetAutomationId(sourceOperationDismissButton, "DismissAddFolderStatusButton");
            Grid.SetColumn(sourceOperationDismissButton, 2);
            statusLayout.Children.Add(sourceOperationDismissButton);
            statusFrame.Child = statusLayout;
            Grid.SetRow(statusFrame, 1);
            layout.Children.Add(statusFrame);

            sourceOperationRail = new Border();
            sourceOperationRail.Height = 2;
            sourceOperationRail.Background = BrushFrom("#25334A");
            sourceOperationRail.ClipToBounds = true;
            sourceOperationRail.IsHitTestVisible = false;
            sourceOperationRailSegment = new Border();
            sourceOperationRailSegment.Width = 56;
            sourceOperationRailSegment.Height = 2;
            sourceOperationRailSegment.HorizontalAlignment = HorizontalAlignment.Left;
            sourceOperationRailSegment.Background = Blue;
            sourceOperationRailTransform = new TranslateTransform(-56, 0);
            sourceOperationRailSegment.RenderTransform = sourceOperationRailTransform;
            sourceOperationRail.Child = sourceOperationRailSegment;
            Grid.SetRow(sourceOperationRail, 2);
            layout.Children.Add(sourceOperationRail);

            item.Child = layout;
            item.ToolTip = sourceOperationPath;
            sourceOperationItem = item;
            RenderSourceOperationItem(false, true);
            if (animateNextSourceOperationEntry)
            {
                animateNextSourceOperationEntry = false;
                AnimateSourceItemEntrance(item);
            }
            return item;
        }

        private FrameworkElement BuildResolvedSourceRow(
            FrameworkElement normalRow,
            BackupSourceView source)
        {
            Border wrapper = new Border();
            wrapper.Background = BrushFrom("#14352C");
            wrapper.BorderBrush = Green;
            wrapper.BorderThickness = new Thickness(2, 0, 0, 1);
            wrapper.ClipToBounds = true;
            wrapper.RenderTransform = new TranslateTransform(0, 0);

            StackPanel content = new StackPanel();
            content.Children.Add(normalRow);
            Border receipt = new Border();
            receipt.Padding = new Thickness(12, 5, 8, 6);
            Grid receiptLayout = new Grid();
            receiptLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            receiptLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            TextBlock check = new TextBlock();
            check.Text = "\u2713";
            check.FontSize = 13;
            check.FontWeight = FontWeights.Bold;
            check.Foreground = Green;
            check.Margin = new Thickness(0, 0, 8, 0);
            receiptLayout.Children.Add(check);
            TextBlock status = new TextBlock();
            status.Text = "Folder added  \u2022  Included in the next backup";
            status.FontSize = 11.5;
            status.FontWeight = FontWeights.SemiBold;
            status.Foreground = Green;
            status.TextTrimming = TextTrimming.CharacterEllipsis;
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            Grid.SetColumn(status, 1);
            receiptLayout.Children.Add(status);
            receipt.Child = receiptLayout;
            content.Children.Add(receipt);
            wrapper.Child = content;
            AutomationProperties.SetName(
                wrapper,
                source.DisplayName + " added to protected folders and included in the next backup");

            sourceOperationItem = wrapper;
            sourceOperationStatusText = status;
            sourceOperationDetailText = null;
            sourceOperationRail = null;
            sourceOperationRailSegment = null;
            sourceOperationRailTransform = null;
            sourceOperationRetryButton = null;
            sourceOperationDismissButton = null;
            if (animateNextResolvedSourceRow)
            {
                animateNextResolvedSourceRow = false;
                AnimateSourceItemEntrance(wrapper);
            }
            AnnounceSourceOperation(
                source.SourcePath + " was added to protected folders.",
                false);
            return wrapper;
        }

        private void AnimateSourceItemEntrance(FrameworkElement element)
        {
            if (element == null || !SourceMotionAllowed())
            {
                return;
            }
            TranslateTransform transform = element.RenderTransform as TranslateTransform;
            if (transform == null)
            {
                transform = new TranslateTransform();
                element.RenderTransform = transform;
            }
            element.Opacity = 0;
            transform.Y = -4;
            DoubleAnimation fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(167));
            fade.BeginTime = TimeSpan.FromMilliseconds(42);
            fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            element.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
            DoubleAnimation settle = new DoubleAnimation(-4, 0, TimeSpan.FromMilliseconds(167));
            settle.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            transform.BeginAnimation(TranslateTransform.YProperty, settle, HandoffBehavior.SnapshotAndReplace);
        }

        private bool SourceMotionAllowed()
        {
            return SystemParameters.ClientAreaAnimation &&
                themeResolution != null &&
                !themeResolution.IsHighContrast;
        }

        private void ResetSourceOperationVisualReferences()
        {
            sourceOperationItem = null;
            sourceOperationTag = null;
            sourceOperationTagText = null;
            sourceOperationGlyphFrame = null;
            sourceOperationGlyph = null;
            sourceOperationStatusText = null;
            sourceOperationDetailText = null;
            sourceOperationRail = null;
            sourceOperationRailSegment = null;
            sourceOperationRailTransform = null;
            sourceOperationRetryButton = null;
            sourceOperationDismissButton = null;
        }

        private void StopSourceOperationAnimation()
        {
            if (sourceOperationItem != null)
            {
                sourceOperationItem.BeginAnimation(UIElement.OpacityProperty, null);
                TranslateTransform transform = sourceOperationItem.RenderTransform as TranslateTransform;
                if (transform != null)
                {
                    transform.BeginAnimation(TranslateTransform.YProperty, null);
                }
            }
            if (sourceOperationRailTransform != null)
            {
                sourceOperationRailTransform.BeginAnimation(TranslateTransform.XProperty, null);
                sourceOperationRailTransform.X = -56;
            }
        }

        private void RenderSourceOperationItem(bool animate, bool announce)
        {
            if (sourceOperationItem == null)
            {
                return;
            }

            string tag;
            string glyph;
            string status;
            string detail;
            Brush tone;
            bool busy = false;
            bool retryVisible = false;
            bool dismissVisible = false;
            bool assertive = false;

            switch (sourceOperationStage)
            {
                case SourceOperationStage.AwaitingApproval:
                    tag = "Approval";
                    glyph = ">";
                    status = "Waiting for administrator approval";
                    detail = "Approve the Windows prompt to continue. No backup has started.";
                    tone = BlueText;
                    break;
                case SourceOperationStage.Applying:
                    tag = "Adding";
                    glyph = "...";
                    status = "Adding protected folder...";
                    detail = "Updating the protected folder list. No backup has started.";
                    tone = Teal;
                    busy = true;
                    break;
                case SourceOperationStage.Verifying:
                    tag = "Checking";
                    glyph = "?";
                    status = "Checking the protected configuration...";
                    detail = "The folder is not shown as protected until this check passes.";
                    tone = BlueText;
                    busy = true;
                    break;
                case SourceOperationStage.Cancelled:
                    tag = "Cancelled";
                    glyph = "-";
                    status = "Administrator approval was cancelled";
                    detail = "No folders were changed.";
                    tone = Amber;
                    dismissVisible = true;
                    break;
                case SourceOperationStage.Failed:
                    tag = "Needs attention";
                    glyph = "!";
                    status = "Could not add folder";
                    detail = string.IsNullOrWhiteSpace(sourceOperationError)
                        ? "The protected list was not changed."
                        : sourceOperationError + " Protected list unchanged.";
                    tone = Rose;
                    retryVisible = true;
                    dismissVisible = true;
                    assertive = true;
                    break;
                default:
                    tag = "Pending";
                    glyph = ".";
                    status = "Folder change pending";
                    detail = "No backup has started.";
                    tone = MutedText;
                    break;
            }

            sourceOperationItem.Background = BrushFrom("#111D2E");
            sourceOperationItem.BorderBrush = tone;
            if (sourceOperationTag != null)
            {
                sourceOperationTag.Background = BrushFrom("#17243A");
                sourceOperationTag.BorderBrush = tone;
            }
            if (sourceOperationTagText != null)
            {
                sourceOperationTagText.Text = tag.ToUpperInvariant();
                sourceOperationTagText.Foreground = tone;
            }
            if (sourceOperationGlyphFrame != null)
            {
                sourceOperationGlyphFrame.Background = BrushFrom("#17243A");
                sourceOperationGlyphFrame.BorderBrush = tone;
            }
            if (sourceOperationGlyph != null)
            {
                sourceOperationGlyph.Text = glyph;
                sourceOperationGlyph.Foreground = tone;
            }
            if (sourceOperationStatusText != null)
            {
                sourceOperationStatusText.Text = status;
                sourceOperationStatusText.Foreground = tone;
                if (animate && SourceMotionAllowed())
                {
                    sourceOperationStatusText.BeginAnimation(UIElement.OpacityProperty, null);
                    sourceOperationStatusText.Opacity = 0.35;
                    DoubleAnimation fade = new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(167));
                    fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                    sourceOperationStatusText.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
                }
            }
            if (sourceOperationDetailText != null)
            {
                sourceOperationDetailText.Text = detail;
                sourceOperationDetailText.Foreground = MutedText;
            }
            if (sourceOperationRetryButton != null)
            {
                sourceOperationRetryButton.Visibility = retryVisible ? Visibility.Visible : Visibility.Collapsed;
                sourceOperationRetryButton.IsEnabled = retryVisible &&
                    !sourceOperationInProgress &&
                    !RepositoryRecoveryBlocksMutations();
                AutomationProperties.SetName(sourceOperationRetryButton, "Retry adding folder");
                AutomationProperties.SetHelpText(sourceOperationRetryButton, sourceOperationPath);
            }
            if (sourceOperationDismissButton != null)
            {
                sourceOperationDismissButton.Visibility = dismissVisible ? Visibility.Visible : Visibility.Collapsed;
                AutomationProperties.SetName(sourceOperationDismissButton, "Dismiss folder add status");
                AutomationProperties.SetHelpText(sourceOperationDismissButton, sourceOperationPath);
            }
            AutomationProperties.SetName(sourceOperationItem, status + ". " + detail + " " + sourceOperationPath);
            AutomationProperties.SetLiveSetting(
                sourceOperationItem,
                assertive ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);

            if (busy)
            {
                StartSourceOperationActivity();
            }
            else
            {
                StopSourceOperationAnimation();
                if (sourceOperationRail != null)
                {
                    sourceOperationRail.Background = BrushFrom("#25334A");
                }
                if (sourceOperationRailSegment != null)
                {
                    sourceOperationRailSegment.Visibility = Visibility.Collapsed;
                }
            }
            if (announce)
            {
                AnnounceSourceOperation(status + ". " + detail, assertive);
            }
        }

        private void StartSourceOperationActivity()
        {
            if (sourceOperationRail == null || sourceOperationRailSegment == null)
            {
                return;
            }
            if (!SourceMotionAllowed())
            {
                StopSourceOperationAnimation();
                sourceOperationRail.Background = Blue;
                sourceOperationRailSegment.Visibility = Visibility.Collapsed;
                return;
            }

            sourceOperationRail.Background = BrushFrom("#25334A");
            sourceOperationRailSegment.Visibility = Visibility.Visible;
            sourceOperationRailSegment.Background = Teal;
            if (sourceOperationRailTransform == null)
            {
                sourceOperationRailTransform = new TranslateTransform(-56, 0);
                sourceOperationRailSegment.RenderTransform = sourceOperationRailTransform;
            }
            sourceOperationRailTransform.BeginAnimation(TranslateTransform.XProperty, null);
            sourceOperationRailTransform.X = -56;

            DoubleAnimationUsingKeyFrames travel = new DoubleAnimationUsingKeyFrames();
            travel.BeginTime = TimeSpan.FromSeconds(1);
            travel.Duration = TimeSpan.FromMilliseconds(1500);
            travel.RepeatBehavior = RepeatBehavior.Forever;
            travel.KeyFrames.Add(new DiscreteDoubleKeyFrame(-56, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            travel.KeyFrames.Add(new LinearDoubleKeyFrame(420, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))));
            travel.KeyFrames.Add(new DiscreteDoubleKeyFrame(-56, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(901))));
            travel.KeyFrames.Add(new DiscreteDoubleKeyFrame(-56, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1500))));
            sourceOperationRailTransform.BeginAnimation(
                TranslateTransform.XProperty,
                travel,
                HandoffBehavior.SnapshotAndReplace);
        }

        private void AnnounceSourceOperation(string message, bool assertive)
        {
            if (string.IsNullOrWhiteSpace(message) ||
                string.Equals(message, lastSourceOperationAnnouncement, StringComparison.Ordinal))
            {
                return;
            }
            lastSourceOperationAnnouncement = message;
            FrameworkElement target = sourceOperationStatusText as FrameworkElement ?? sourceOperationItem;
            if (target == null)
            {
                return;
            }
            AutomationProperties.SetName(target, message);
            AutomationProperties.SetLiveSetting(
                target,
                assertive ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
            Dispatcher.BeginInvoke(
                new Action(delegate
                {
                    AutomationPeer peer = UIElementAutomationPeer.FromElement(target) ??
                        UIElementAutomationPeer.CreatePeerForElement(target);
                    if (peer != null)
                    {
                        peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                    }
                }),
                DispatcherPriority.Background);
        }

        private void SetSourceOperationStage(
            SourceOperationStage stage,
            string error,
            DateTime expiresUtc,
            bool rebuild,
            bool animate)
        {
            sourceOperationStage = stage;
            sourceOperationError = error ?? string.Empty;
            sourceOperationExpiresUtc = expiresUtc;
            if (rebuild)
            {
                sourceSignature = null;
                RefreshSources(true);
                return;
            }
            RenderSourceOperationItem(animate, true);
        }

        private void ResetSourceOperationState()
        {
            sourceOperationStage = SourceOperationStage.Idle;
            sourceOperationAction = string.Empty;
            sourceOperationPath = string.Empty;
            sourceOperationError = string.Empty;
            sourceOperationExpiresUtc = DateTime.MinValue;
            lastSourceOperationAnnouncement = string.Empty;
            animateNextSourceOperationEntry = false;
            animateNextResolvedSourceRow = false;
            sourceOperationGeneration++;
        }

        private async void OnRetrySourceOperationClick(object sender, RoutedEventArgs args)
        {
            if (sourceOperationInProgress ||
                !string.Equals(sourceOperationAction, "Add", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(sourceOperationPath))
            {
                return;
            }
            string path = sourceOperationPath;
            SourceConfiguration configuration = currentSourceConfiguration ?? SourceConfiguration.Load();
            if (configuration.ContainsUserSource(path))
            {
                sourceOperationStage = SourceOperationStage.Succeeded;
                sourceOperationExpiresUtc = DateTime.UtcNow.AddSeconds(4);
                animateNextResolvedSourceRow = true;
                RefreshSources(true);
                return;
            }
            if (BackupBlocksSourceChanges())
            {
                SetSourceOperationStage(
                    SourceOperationStage.Failed,
                    "A backup is starting or already running.",
                    DateTime.MaxValue,
                    false,
                    true);
                return;
            }
            await ApplySourceChange("Add", path);
        }

        private void OnDismissSourceOperationClick(object sender, RoutedEventArgs args)
        {
            if (sourceOperationInProgress)
            {
                return;
            }
            ResetSourceOperationState();
            sourceSignature = null;
            RefreshSources(true);
        }

        private UIElement BuildHistory()
        {
            historyLayout = new Grid();
            historyLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border tableCard = CreateCard();
            tableCard.MinHeight = 300;
            historyTable = tableCard;
            tableCard.Padding = new Thickness(16, 12, 16, 10);
            Grid tableGrid = new Grid();
            tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            tableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid tableHeader = new Grid();
            tableHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tableHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock tableTitle = new TextBlock();
            tableTitle.Text = "Recent backups";
            tableTitle.FontSize = 15;
            tableTitle.FontWeight = FontWeights.SemiBold;
            tableHeader.Children.Add(tableTitle);
            StackPanel activityMeta = new StackPanel();
            activityMeta.Orientation = Orientation.Horizontal;
            activityMeta.VerticalAlignment = VerticalAlignment.Center;
            runChart = new RunChart(themeResolution.Palette);
            historyChart = runChart;
            runChart.Width = 150;
            runChart.Height = 28;
            runChart.Margin = new Thickness(0, 0, 12, 0);
            activityMeta.Children.Add(runChart);
            runCount = new TextBlock();
            runCount.Text = "0 runs";
            runCount.FontSize = 11;
            runCount.FontWeight = FontWeights.SemiBold;
            runCount.Foreground = MutedText;
            runCount.VerticalAlignment = VerticalAlignment.Center;
            activityMeta.Children.Add(runCount);
            viewRunDetailsButton = CreateButton("View details");
            viewRunDetailsButton.MinHeight = 34;
            viewRunDetailsButton.Margin = new Thickness(12, 0, 0, 0);
            viewRunDetailsButton.IsEnabled = false;
            viewRunDetailsButton.Click += OnViewRunDetailsClick;
            AutomationProperties.SetAutomationId(
                viewRunDetailsButton,
                "ViewRunDetailsButton");
            AutomationProperties.SetName(
                viewRunDetailsButton,
                "View details for the selected backup run");
            activityMeta.Children.Add(viewRunDetailsButton);
            exportDiagnosticsButton = CreateButton("Export diagnostics");
            exportDiagnosticsButton.MinHeight = 34;
            exportDiagnosticsButton.Margin = new Thickness(8, 0, 0, 0);
            exportDiagnosticsButton.Click += OnExportDiagnosticsClick;
            AutomationProperties.SetAutomationId(
                exportDiagnosticsButton,
                "ExportDiagnosticsButton");
            AutomationProperties.SetName(
                exportDiagnosticsButton,
                "Export a redacted diagnostic support bundle");
            AutomationProperties.SetHelpText(
                exportDiagnosticsButton,
                "Creates a new ZIP outside protected paths and removes personal paths, identities, commands, and credential-like values.");
            activityMeta.Children.Add(exportDiagnosticsButton);
            Grid.SetColumn(activityMeta, 1);
            tableHeader.Children.Add(activityMeta);
            tableGrid.Children.Add(tableHeader);

            historyGrid = CreateHistoryGrid();
            historyGrid.Margin = new Thickness(0, 8, 0, 0);
            historyGrid.SelectionChanged += OnHistorySelectionChanged;
            historyGrid.MouseDoubleClick += OnHistoryMouseDoubleClick;
            historyGrid.PreviewKeyDown += OnHistoryPreviewKeyDown;
            Grid.SetRow(historyGrid, 1);
            tableGrid.Children.Add(historyGrid);

            tableCard.Child = tableGrid;
            historyLayout.Children.Add(tableCard);
            return historyLayout;
        }

        private DataGrid CreateHistoryGrid()
        {
            DataGrid grid = new DataGrid();
            grid.AutoGenerateColumns = false;
            grid.IsReadOnly = true;
            grid.CanUserAddRows = false;
            grid.CanUserDeleteRows = false;
            grid.CanUserResizeRows = false;
            grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            grid.HorizontalGridLinesBrush = BrushFrom("#223149");
            grid.VerticalGridLinesBrush = Brushes.Transparent;
            grid.Background = Brushes.Transparent;
            grid.Foreground = PrimaryText;
            grid.BorderThickness = new Thickness(0);
            grid.RowBackground = Brushes.Transparent;
            grid.AlternatingRowBackground = BrushFrom("#0F1A2B");
            grid.AlternationCount = 2;
            grid.RowHeight = 36;
            grid.SelectionMode = DataGridSelectionMode.Single;
            grid.SelectionUnit = DataGridSelectionUnit.FullRow;
            grid.SelectionBrushCompat(
                themeResolution.Palette.Selection,
                themeResolution.Palette.TextPrimary,
                themeResolution.Palette.SelectionText);

            Style headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, MutedText));
            headerStyle.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold));
            headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
            headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, CardBorderBrush));
            headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 0, 5, 7)));
            grid.ColumnHeaderStyle = headerStyle;

            grid.Columns.Add(TextColumn("When", "StartedDisplay", 1.35));
            grid.Columns.Add(TextColumn("Result", "StateLabel", 1.0));
            grid.Columns.Add(TextColumn("Duration", "DurationDisplay", 0.9));
            grid.Columns.Add(TextColumn("Files", "FilesDisplay", 0.9));
            grid.Columns.Add(TextColumn("Processed", "ProcessedDisplay", 1.05));
            return grid;
        }

        private void OnHistorySelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            UpdateActivityActions();
        }

        private void OnHistoryMouseDoubleClick(object sender, MouseButtonEventArgs args)
        {
            if (historyGrid != null && historyGrid.SelectedItem is RunMetricView)
            {
                OpenSelectedRunDetails();
                args.Handled = true;
            }
        }

        private void OnHistoryPreviewKeyDown(object sender, KeyEventArgs args)
        {
            if ((args.Key == Key.Enter || args.Key == Key.Return) &&
                historyGrid != null && historyGrid.SelectedItem is RunMetricView)
            {
                OpenSelectedRunDetails();
                args.Handled = true;
            }
        }

        private void OnViewRunDetailsClick(object sender, RoutedEventArgs args)
        {
            OpenSelectedRunDetails();
        }

        private void OpenSelectedRunDetails()
        {
            RunMetricView run = historyGrid == null
                ? null
                : historyGrid.SelectedItem as RunMetricView;
            if (run == null)
            {
                UpdateActivityActions();
                return;
            }
            try
            {
                SourceConfiguration configuration = currentSourceConfiguration ??
                    SourceConfiguration.Load();
                RunDetails details = RunDetailsReader.Load(configuration, run);
                RunDetailsWindow window = new RunDetailsWindow(
                    details,
                    themeResolution.Palette);
                window.Owner = this;
                window.ShowDialog();
            }
            catch (Exception error)
            {
                MessageBox.Show(
                    this,
                    "Run details could not be opened.\n\n" + error.Message,
                    "Run details unavailable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void OnExportDiagnosticsClick(object sender, RoutedEventArgs args)
        {
            UpdateActivityActions();
            if (diagnosticExportInProgress || currentSourceConfiguration == null ||
                exportDiagnosticsButton == null || !exportDiagnosticsButton.IsEnabled)
            {
                return;
            }

            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "Export redacted Rewindle diagnostics";
            dialog.Filter = "ZIP archive (*.zip)|*.zip";
            dialog.DefaultExt = ".zip";
            dialog.AddExtension = true;
            dialog.CheckPathExists = true;
            dialog.CheckFileExists = false;
            dialog.OverwritePrompt = false;
            dialog.FileName = "restic-backup-diagnostics-" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
                ".zip";
            string desktop = Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrWhiteSpace(desktop) && Directory.Exists(desktop))
            {
                dialog.InitialDirectory = desktop;
            }
            bool? accepted = dialog.ShowDialog(this);
            if (!accepted.HasValue || !accepted.Value)
            {
                return;
            }

            SourceConfiguration configuration = currentSourceConfiguration;
            TaskSchedule schedule = currentTaskSchedule;
            TelemetrySnapshot snapshot = lastSnapshot;
            diagnosticExportInProgress = true;
            UpdateActivityActions();
            DiagnosticExportResult result;
            try
            {
                result = await Task.Run(delegate
                {
                    return DiagnosticExporter.Export(
                        configuration,
                        schedule,
                        snapshot,
                        dialog.FileName);
                });
            }
            catch (Exception error)
            {
                result = DiagnosticExportResult.Failure(error.Message);
            }
            finally
            {
                diagnosticExportInProgress = false;
            }
            UpdateActivityActions();
            if (result.Succeeded)
            {
                MessageBox.Show(
                    this,
                    "A redacted, read-only support bundle was created.\n\n" +
                        result.ExportPath + "\n\n" +
                        "Personal paths, identities, command lines, and credential-like values were removed.",
                    "Diagnostics exported",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    this,
                    result.ErrorMessage ?? "The diagnostic bundle was not created.",
                    "Diagnostic export failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void UpdateActivityActions()
        {
            if (viewRunDetailsButton != null)
            {
                bool selected = historyGrid != null &&
                    historyGrid.SelectedItem is RunMetricView;
                viewRunDetailsButton.IsEnabled = selected;
                viewRunDetailsButton.ToolTip = selected
                    ? "Open structured details and bounded protected log events for this run."
                    : "Select a backup run first.";
            }
            if (exportDiagnosticsButton != null)
            {
                bool conflict = BackupBlocksSourceChanges();
                bool enabled = currentSourceConfiguration != null &&
                    !diagnosticExportInProgress && !conflict;
                exportDiagnosticsButton.Content = diagnosticExportInProgress
                    ? "Exporting..."
                    : "Export diagnostics";
                exportDiagnosticsButton.IsEnabled = enabled;
                string help = diagnosticExportInProgress
                    ? "The redacted diagnostic ZIP is being created."
                    : conflict
                        ? "Wait for the protected operation to finish before exporting a consistent bundle."
                        : "Create a new redacted support ZIP outside protected backup paths.";
                exportDiagnosticsButton.ToolTip = help;
                AutomationProperties.SetHelpText(exportDiagnosticsButton, help);
                AutomationProperties.SetItemStatus(exportDiagnosticsButton, help);
            }
        }

        private DataGridTextColumn TextColumn(string header, string path, double width)
        {
            DataGridTextColumn column = new DataGridTextColumn();
            column.Header = header;
            column.Binding = new Binding(path);
            column.Width = new DataGridLength(width, DataGridLengthUnitType.Star);
            ElementStyleHolder.Apply(column);
            return column;
        }

        private void ApplyWindowPalette()
        {
            Background = BackgroundTop;
            Foreground = PrimaryText;
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs args)
        {
            ApplyResponsiveLayout();
        }

        private void OnDashboardPreviewKeyDown(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.F5)
            {
                RefreshDashboard();
                args.Handled = true;
            }
        }

        private void ApplyResponsiveLayout()
        {
            if (headerLayout == null || shellLayout == null || navigationRail == null || footerLayout == null)
            {
                return;
            }

            double availableWidth = ActualWidth > 0 ? ActualWidth : Width;
            bool compact = availableWidth < 1320;
            if (compactLayout.HasValue && compactLayout.Value == compact)
            {
                return;
            }
            compactLayout = compact;
            shellLayout.ColumnDefinitions[0].Width = new GridLength(compact ? 56 : 216);
            navigationRail.Padding = compact
                ? new Thickness(6, 18, 6, 14)
                : new Thickness(18, 22, 18, 16);
            if (navigationBrand != null)
            {
                navigationBrand.Text = compact ? "R" : "RESTIC";
                navigationBrand.HorizontalAlignment = compact
                    ? HorizontalAlignment.Center
                    : HorizontalAlignment.Left;
                navigationBrand.Margin = compact
                    ? new Thickness(0, 4, 0, 0)
                    : new Thickness(8, 2, 0, 0);
            }
            ConfigureNavigationButton(protectionNavButton, compact);
            ConfigureNavigationButton(activityNavButton, compact);
            ConfigureNavigationButton(restoreNavButton, compact);
            ConfigureNavigationButton(settingsNavButton, compact);

            footerLayout.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            footerLayout.ColumnDefinitions[1].Width = GridLength.Auto;
            footerLayout.RowDefinitions[0].Height = GridLength.Auto;
            footerLayout.RowDefinitions[1].Height = new GridLength(0);
            Grid.SetColumn(footerDetails, 0);
            Grid.SetRow(footerDetails, 0);
            Grid.SetColumn(lastUpdated, 1);
            Grid.SetRow(lastUpdated, 0);
            lastUpdated.HorizontalAlignment = HorizontalAlignment.Right;
            lastUpdated.Margin = new Thickness(0);
        }

        private void ConfigureNavigationButton(Button button, bool compact)
        {
            if (button == null)
            {
                return;
            }
            string[] metadata = button.Tag as string[];
            if (metadata == null || metadata.Length < 3)
            {
                return;
            }
            button.Content = compact
                ? BuildIconLabel(metadata[1], string.Empty)
                : BuildIconLabel(metadata[1], metadata[2]);
            button.HorizontalContentAlignment = compact
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Left;
            button.Padding = compact
                ? new Thickness(0, 8, 0, 8)
                : new Thickness(10, 8, 10, 8);
        }

        private void OnThemeSegmentClick(object sender, RoutedEventArgs args)
        {
            Button button = sender as Button;
            if (button == null || !(button.Tag is DashboardThemePreference))
            {
                return;
            }
            DashboardThemePreference preference = (DashboardThemePreference)button.Tag;
            if (preference == themePreference || themeChangeInProgress)
            {
                return;
            }

            themePreference = preference;
            themeResolution = DashboardThemeManager.Resolve(preference);
            DashboardThemeManager.TrySavePreference(preference);
            RebuildVisualTreeForTheme(preference);
        }

        private bool RefreshEffectiveTheme()
        {
            if (themeChangeInProgress)
            {
                return false;
            }
            DashboardThemeResolution resolution = DashboardThemeManager.Resolve(themePreference);
            if (SameEffectiveTheme(resolution, themeResolution))
            {
                return false;
            }
            themeResolution = resolution;
            RebuildVisualTreeForTheme(null);
            return true;
        }

        private static bool SameEffectiveTheme(
            DashboardThemeResolution first,
            DashboardThemeResolution second)
        {
            if (first == null || second == null ||
                first.EffectiveKind != second.EffectiveKind ||
                first.IsHighContrast != second.IsHighContrast)
            {
                return false;
            }
            DashboardThemePalette firstPalette = first.Palette;
            DashboardThemePalette secondPalette = second.Palette;
            return firstPalette.BackgroundTop.Color == secondPalette.BackgroundTop.Color &&
                firstPalette.TextPrimary.Color == secondPalette.TextPrimary.Color &&
                firstPalette.ButtonBackground.Color == secondPalette.ButtonBackground.Color &&
                firstPalette.ButtonText.Color == secondPalette.ButtonText.Color &&
                firstPalette.Selection.Color == secondPalette.Selection.Color &&
                firstPalette.SelectionText.Color == secondPalette.SelectionText.Color &&
                firstPalette.Focus.Color == secondPalette.Focus.Color;
        }

        private void RebuildVisualTreeForTheme(
            DashboardThemePreference? themeToFocus)
        {
            if (themeChangeInProgress)
            {
                return;
            }

            ScrollViewer oldPage = Content as ScrollViewer;
            double oldOffset = oldPage == null ? 0 : oldPage.VerticalOffset;
            themeChangeInProgress = true;
            try
            {
                StopStatusPulse();
                ApplyWindowPalette();
                sourceSignature = null;
                Content = BuildInterface();
                RefreshDashboard();
                StartShimmer();
                ScrollViewer newPage = Content as ScrollViewer;
                if (newPage != null && oldOffset > 0)
                {
                    Dispatcher.BeginInvoke(
                        new Action(delegate { newPage.ScrollToVerticalOffset(oldOffset); }),
                        DispatcherPriority.Loaded);
                }
                Button newThemeButton;
                if (themeToFocus.HasValue &&
                    themeButtons.TryGetValue(themeToFocus.Value, out newThemeButton))
                {
                    Dispatcher.BeginInvoke(
                        new Action(delegate { newThemeButton.Focus(); }),
                        DispatcherPriority.Input);
                }
            }
            finally
            {
                themeChangeInProgress = false;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs args)
        {
            ApplyResponsiveLayout();
            StartShimmer();
            RefreshDashboard();
            if (options.StartMinimized)
            {
                HideToTray();
            }
        }

        private void OnRefreshTick(object sender, EventArgs args)
        {
            if (showEvent.WaitOne(0))
            {
                ShowDashboard();
            }
            RefreshDashboard();
        }

        private void OnUserPreferenceChanged(
            object sender,
            UserPreferenceChangedEventArgs args)
        {
            QueueEffectiveThemeRefresh();
        }

        private void OnSystemParametersChanged(
            object sender,
            PropertyChangedEventArgs args)
        {
            QueueEffectiveThemeRefresh();
        }

        private void QueueEffectiveThemeRefresh()
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }
            Dispatcher.BeginInvoke(
                new Action(delegate
                {
                    bool rebuilt = RefreshEffectiveTheme();
                    if (!rebuilt)
                    {
                        RefreshMotionEffects();
                    }
                }),
                DispatcherPriority.Background);
        }

        private void RefreshMotionEffects()
        {
            if (SourceMotionAllowed())
            {
                StartShimmer();
                if (lastSnapshot != null &&
                    (lastSnapshot.IsActive || previewEnabled))
                {
                    StartStatusPulse();
                }
                if (sourceOperationStage == SourceOperationStage.Applying ||
                    sourceOperationStage == SourceOperationStage.Verifying)
                {
                    StartSourceOperationActivity();
                }
                return;
            }
            StopShimmer();
            StopStatusPulse();
            StopSourceOperationAnimation();
            if (sourceOperationRail != null &&
                (sourceOperationStage == SourceOperationStage.Applying ||
                    sourceOperationStage == SourceOperationStage.Verifying))
            {
                sourceOperationRail.Background = Blue;
            }
            if (sourceOperationRailSegment != null)
            {
                sourceOperationRailSegment.Visibility = Visibility.Collapsed;
            }
        }

        private bool RepositoryRecoveryBlocksMutations()
        {
            return repositoryRecoveryInProgress ||
                (repositoryRecoveryStatus != null && repositoryRecoveryStatus.Exists);
        }

        private bool RepositoryRecoveryHasConflictingOperation()
        {
            return repositoryOperationInProgress ||
                sourceOperationInProgress ||
                scheduleOperationInProgress ||
                anomalyReviewInProgress ||
                backupStartInProgress ||
                CancellationBlocksMutations(lastSnapshot) ||
                DateTime.UtcNow < backupRequestPendingUntilUtc ||
                (lastSnapshot != null && lastSnapshot.IsActive);
        }

        private static bool RepositoryRecoveryManagerAvailable()
        {
            string managerPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "ResticBackuper",
                "Manage-Repository.ps1");
            return File.Exists(managerPath) && !Directory.Exists(managerPath);
        }

        private void RefreshRepositoryRecoveryStatus()
        {
            RepositoryRecoveryStatus status;
            try
            {
                status = RepositoryManagerLauncher.GetRecoveryStatus();
            }
            catch
            {
                status = RepositoryRecoveryStatus.Blocked(
                    "The interrupted-move journal could not be inspected. Backups remain paused until the protected installation is repaired.");
            }
            repositoryRecoveryStatus = status ?? RepositoryRecoveryStatus.Blocked(
                "The interrupted-move recovery status is unavailable. Backups remain paused.");
            if (!repositoryRecoveryInProgress && !repositoryRecoveryStatus.Exists)
            {
                repositoryRecoveryOperationMessage = string.Empty;
                repositoryRecoveryOperationPercent = null;
            }
            UpdateRepositoryRecoverySurface();
        }

        private void UpdateRepositoryRecoverySurface()
        {
            RepositoryRecoveryStatus status = repositoryRecoveryStatus ??
                RepositoryRecoveryStatus.None();
            bool visible = repositoryRecoveryInProgress || status.Exists;
            bool managerAvailable = RepositoryRecoveryManagerAvailable();
            bool conflict = RepositoryRecoveryHasConflictingOperation();
            bool canRepair = visible &&
                !repositoryRecoveryInProgress &&
                status.Exists &&
                status.IsRecoverable &&
                managerAvailable &&
                !previewEnabled &&
                !conflict;

            string message = status.Exists
                ? status.Message
                : "Protected repository recovery is completing.";
            string state;
            if (repositoryRecoveryInProgress)
            {
                state = string.IsNullOrWhiteSpace(repositoryRecoveryOperationMessage)
                    ? "Repairing the interrupted repository move..."
                    : repositoryRecoveryOperationMessage;
            }
            else if (!status.IsRecoverable)
            {
                state = string.IsNullOrWhiteSpace(repositoryRecoveryOperationMessage)
                    ? "Automatic and manual backups remain blocked. Repair the protected installation before continuing."
                    : repositoryRecoveryOperationMessage;
            }
            else if (!managerAvailable)
            {
                state = "The protected repository recovery helper is unavailable. Backups remain blocked.";
            }
            else if (previewEnabled)
            {
                state = "Stop the dashboard preview before starting protected recovery.";
            }
            else if (conflict)
            {
                state = "Wait for the current protected operation to finish, then repair the interrupted move.";
            }
            else if (!string.IsNullOrWhiteSpace(repositoryRecoveryOperationMessage))
            {
                state = repositoryRecoveryOperationMessage;
            }
            else
            {
                state = "Ready to repair. Windows administrator approval is required; no backup will start.";
            }

            string label = repositoryRecoveryInProgress
                ? "Repairing..."
                : status.IsRecoverable
                    ? "Repair interrupted move"
                    : "Repair unavailable";
            string helpText = canRepair
                ? "Validate and repair the protected interrupted-move journal. No backup starts and no snapshot is deleted."
                : state;

            ApplyRepositoryRecoverySurface(
                protectionRepositoryRecoveryCard,
                protectionRepositoryRecoveryMessage,
                protectionRepositoryRecoveryState,
                protectionRepositoryRecoveryButton,
                protectionRepositoryRecoveryProgress,
                visible,
                message,
                state,
                label,
                helpText,
                canRepair);
            ApplyRepositoryRecoverySurface(
                settingsRepositoryRecoveryCard,
                settingsRepositoryRecoveryMessage,
                settingsRepositoryRecoveryState,
                settingsRepositoryRecoveryButton,
                settingsRepositoryRecoveryProgress,
                visible,
                message,
                state,
                label,
                helpText,
                canRepair);

            string announcement = visible ? message + " " + state : string.Empty;
            if (!string.Equals(
                announcement,
                lastRepositoryRecoveryAnnouncement,
                StringComparison.Ordinal))
            {
                lastRepositoryRecoveryAnnouncement = announcement;
                TextBlock announcementTarget = protectionRepositoryRecoveryState ??
                    settingsRepositoryRecoveryState;
                if (announcementTarget != null && visible)
                {
                    AutomationPeer peer = UIElementAutomationPeer.FromElement(announcementTarget) ??
                        UIElementAutomationPeer.CreatePeerForElement(announcementTarget);
                    if (peer != null)
                    {
                        peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                    }
                }
            }
        }

        private void ApplyRepositoryRecoverySurface(
            Border card,
            TextBlock messageBlock,
            TextBlock stateBlock,
            Button repairButton,
            ProgressBar progress,
            bool visible,
            string message,
            string state,
            string label,
            string helpText,
            bool canRepair)
        {
            if (card == null)
            {
                return;
            }
            card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible)
            {
                return;
            }
            messageBlock.Text = message;
            stateBlock.Text = state;
            stateBlock.Foreground = repositoryRecoveryInProgress
                ? BlueText
                : repositoryRecoveryStatus != null && !repositoryRecoveryStatus.IsRecoverable
                    ? Rose
                    : PrimaryText;
            repairButton.Content = label;
            repairButton.IsEnabled = canRepair;
            repairButton.ToolTip = helpText;
            AutomationProperties.SetName(repairButton, label);
            AutomationProperties.SetHelpText(repairButton, helpText);
            AutomationProperties.SetItemStatus(repairButton, helpText);
            progress.Visibility = repositoryRecoveryInProgress
                ? Visibility.Visible
                : Visibility.Collapsed;
            progress.IsIndeterminate = !repositoryRecoveryOperationPercent.HasValue;
            if (repositoryRecoveryOperationPercent.HasValue)
            {
                progress.Value = Math.Max(
                    progress.Minimum,
                    Math.Min(progress.Maximum, repositoryRecoveryOperationPercent.Value));
                AutomationProperties.SetName(
                    progress,
                    "Repository recovery progress " +
                        progress.Value.ToString("0", CultureInfo.CurrentCulture) + " percent");
            }
            else
            {
                AutomationProperties.SetName(progress, "Repository recovery in progress");
            }
            AutomationProperties.SetName(
                card,
                "Action required. Backups paused. " + message + " " + state);
            AutomationProperties.SetHelpText(card, helpText);
        }

        private void SetRepositoryRecoveryOperationStatus(
            string message,
            double? percent)
        {
            repositoryRecoveryOperationMessage = message ?? string.Empty;
            repositoryRecoveryOperationPercent = percent;
            UpdateRepositoryRecoverySurface();
        }

        private void ApplyRepositoryRecoveryProgress(RepositoryProgress progress)
        {
            if (!repositoryRecoveryInProgress || progress == null)
            {
                return;
            }
            string message = string.IsNullOrWhiteSpace(progress.Message)
                ? "Repairing protected repository state..."
                : progress.Message;
            if (progress.Percent.HasValue)
            {
                message += "  " + progress.Percent.Value.ToString("0", CultureInfo.CurrentCulture) + "%";
            }
            SetRepositoryRecoveryOperationStatus(message, progress.Percent);
        }

        private void RefreshDashboard()
        {
            RefreshRepositoryRecoveryStatus();
            RefreshSchedule(false);
            try
            {
                TelemetrySnapshot snapshot = reader.Load();
                lastSnapshot = snapshot;
                webTelemetryError = string.Empty;
                ApplySnapshot(snapshot);
                if (previewEnabled)
                {
                    ApplyPreview(snapshot);
                }
                HandleStateTransition(snapshot);
            }
            catch (Exception error)
            {
                webTelemetryError = error.Message;
                heroTitle.Text = "Dashboard data is temporarily unavailable";
                heroDetail.Text = error.Message;
                SetBadge("DATA RETRY", Amber, BrushFrom("#3A2F18"));
                lastUpdated.Text = "Retrying automatically";
            }
            RefreshSources(false);
            PublishWebPresentationState();
        }

        private void RefreshSchedule(bool force)
        {
            if (scheduleOperationInProgress)
            {
                return;
            }
            if (!force && DateTime.UtcNow < nextScheduleRefreshUtc)
            {
                return;
            }
            nextScheduleRefreshUtc = DateTime.UtcNow.AddSeconds(
                cancellationAwaitingTerminal ? 1 : 30);
            TaskScheduleReadResult result = TaskScheduleReader.ReadInstalled();
            if (result.Succeeded && result.Schedule != null)
            {
                currentTaskSchedule = result.Schedule;
                scheduleReadError = string.Empty;
                if (scheduleActionStatus != null)
                {
                    scheduleActionStatus.Text = result.Schedule.Enabled
                        ? "Installed: " + result.Schedule.Summary
                        : "Automatic backups paused";
                    scheduleActionStatus.Foreground = result.Schedule.Enabled ? MutedText : Amber;
                }
            }
            else
            {
                currentTaskSchedule = null;
                scheduleReadError = result.ErrorMessage ?? "The installed schedule could not be verified.";
                if (scheduleActionStatus != null)
                {
                    scheduleActionStatus.Text = "Schedule unavailable";
                    scheduleActionStatus.Foreground = Rose;
                    AutomationProperties.SetHelpText(scheduleActionStatus, scheduleReadError);
                }
            }
            UpdateHeaderSubtitle();
            UpdateScheduleButton();
        }

        private void UpdateHeaderSubtitle()
        {
            if (headerSubtitle == null)
            {
                return;
            }
            int userSourceCount = currentSourceConfiguration == null
                ? 0
                : currentSourceConfiguration.Sources.Count(source => !source.IsProtectedCanary);
            bool hasCanarySource = currentSourceConfiguration != null &&
                currentSourceConfiguration.Sources.Any(source => source.IsProtectedCanary);
            string scope = currentSourceConfiguration == null
                ? "Loading backup scope"
                : userSourceCount.ToString(CultureInfo.CurrentCulture) +
                    (userSourceCount == 1 ? " protected folder" : " protected folders") +
                    (hasCanarySource ? " + restore canary" : " (restore canary missing)");
            string schedule = currentTaskSchedule == null
                ? "Schedule unavailable"
                : currentTaskSchedule.Summary;
            string repository = currentSourceConfiguration == null
                ? "Loading destination"
                : currentSourceConfiguration.RepositoryPath;
            headerSubtitle.Text = scope + "  \u2022  " + repository + "  \u2022  " + schedule;
            if (repositoryValue != null)
            {
                repositoryValue.Text = repository;
                repositoryValue.ToolTip = currentSourceConfiguration == null
                    ? null
                    : currentSourceConfiguration.RepositoryPath;
                AutomationProperties.SetName(repositoryValue, "Backup destination " + repository);
            }
            if (settingsRepositoryValue != null)
            {
                settingsRepositoryValue.Text = repository;
                settingsRepositoryValue.ToolTip = currentSourceConfiguration == null
                    ? null
                    : currentSourceConfiguration.RepositoryPath;
                AutomationProperties.SetName(
                    settingsRepositoryValue,
                    "Current backup destination " + repository);
            }
            if (settingsRepositoryVolume != null)
            {
                settingsRepositoryVolume.Text = currentSourceConfiguration == null
                    ? "Storage volume unavailable"
                    : DescribeRepositoryVolume(currentSourceConfiguration.RepositoryPath);
            }
            UpdateRepositoryButtons();
        }

        private static string DescribeRepositoryVolume(string repositoryPath)
        {
            try
            {
                string root = System.IO.Path.GetPathRoot(repositoryPath);
                if (string.IsNullOrWhiteSpace(root))
                {
                    return "Storage volume unavailable";
                }
                DriveInfo drive = new DriveInfo(root);
                if (!drive.IsReady)
                {
                    return root + "  •  Volume unavailable";
                }
                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? drive.DriveType.ToString()
                    : drive.VolumeLabel;
                return root + "  •  " + label + "  •  " +
                    FormatStorageBytes(drive.AvailableFreeSpace) + " free";
            }
            catch
            {
                return "Storage volume unavailable";
            }
        }

        private static string FormatStorageBytes(long value)
        {
            string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
            double amount = Math.Max(0, value);
            int unit = 0;
            while (amount >= 1024 && unit < units.Length - 1)
            {
                amount /= 1024;
                unit++;
            }
            return amount.ToString(unit == 0 ? "N0" : "N1", CultureInfo.CurrentCulture) +
                " " + units[unit];
        }

        private void RefreshSources(bool force)
        {
            if (!sourceOperationInProgress &&
                sourceOperationStage != SourceOperationStage.Idle &&
                sourceOperationExpiresUtc != DateTime.MinValue &&
                sourceOperationExpiresUtc != DateTime.MaxValue &&
                DateTime.UtcNow >= sourceOperationExpiresUtc)
            {
                ResetSourceOperationState();
                force = true;
            }
            try
            {
                SourceConfiguration configuration = SourceConfiguration.Load();
                bool managerAvailable = File.Exists(configuration.ManagerPath);
                string signature = string.Join(
                    "\u001f",
                    configuration.Sources.Select(source =>
                        (source.IsProtectedCanary ? "canary:" : "user:") + source.SourcePath).ToArray()) +
                    "\u001fmanager:" + (managerAvailable ? "1" : "0");

                currentSourceConfiguration = configuration;
                if (force || !string.Equals(signature, sourceSignature, StringComparison.Ordinal))
                {
                    sourceSignature = signature;
                    RebuildSourceList(configuration, managerAvailable);
                }

                int userSources = configuration.Sources.Count(source => !source.IsProtectedCanary);
                bool hasCanarySource = configuration.Sources.Any(source => source.IsProtectedCanary);
                UpdateHeaderSubtitle();
                sourceSummary.Text = userSources.ToString(CultureInfo.CurrentCulture) +
                    (userSources == 1 ? " folder" : " folders") +
                    (hasCanarySource ? "  |  protected restore canary" : "  |  restore canary missing");
                sourceSummary.Foreground = hasCanarySource ? MutedText : Rose;

                if (sourceOperationStage != SourceOperationStage.Idle &&
                    sourceOperationStage != SourceOperationStage.Succeeded)
                {
                    sourceStatus.Text = "Folder changes are shown inline below. No backup has started.";
                    sourceStatus.Foreground = MutedText;
                }
                else if (DateTime.UtcNow >= sourceNoticeExpiresUtc)
                {
                    if (!hasCanarySource)
                    {
                        sourceStatus.Text = "The required restore canary is missing; backups cannot be verified.";
                        sourceStatus.Foreground = Rose;
                    }
                    else if (!managerAvailable)
                    {
                        sourceStatus.Text = "Folder management is unavailable because the protected manager is missing.";
                        sourceStatus.Foreground = Rose;
                    }
                    else
                    {
                        sourceStatus.Text = "Administrator approval is required to change protected folders.";
                        sourceStatus.Foreground = MutedText;
                    }
                }
            }
            catch (Exception error)
            {
                currentSourceConfiguration = null;
                sourceSignature = null;
                removeSourceButtons.Clear();
                sourceList.Children.Clear();
                TextBlock unavailable = new TextBlock();
                unavailable.Text = "Protected folders are temporarily unavailable.";
                unavailable.FontSize = 12;
                unavailable.Foreground = Rose;
                unavailable.Margin = new Thickness(12, 14, 12, 14);
                sourceList.Children.Add(unavailable);
                sourceSummary.Text = "Protected configuration unavailable";
                if (DateTime.UtcNow >= sourceNoticeExpiresUtc)
                {
                    sourceStatus.Text = error.Message;
                    sourceStatus.Foreground = Rose;
                }
            }
            UpdateSourceButtons();
            UpdateActivityActions();
            UpdateRestoreReadiness();
            UpdateHeaderSubtitle();
        }

        private void UpdateRestoreReadiness()
        {
            if (restoreReadinessTitle == null || restoreReadinessDetail == null ||
                openRestoreCenterButton == null || checkRecoveryReadinessButton == null)
            {
                return;
            }
            bool managerAvailable = currentSourceConfiguration != null &&
                File.Exists(System.IO.Path.Combine(
                    currentSourceConfiguration.InstallRoot,
                    "Manage-Restore.ps1")) &&
                File.Exists(System.IO.Path.Combine(
                    currentSourceConfiguration.InstallRoot,
                    "restore.py"));
            bool hasCanary = currentSourceConfiguration != null &&
                currentSourceConfiguration.Sources.Any(source => source.IsProtectedCanary);
            bool healthAvailable = currentSourceConfiguration != null &&
                File.Exists(System.IO.Path.Combine(
                    currentSourceConfiguration.InstallRoot,
                    "recovery_health.py"));
            bool blocked = BackupBlocksSourceChanges() || repositoryOperationInProgress ||
                repositoryRecoveryInProgress || repositoryRecoveryStatus.Exists;
            bool ready = managerAvailable && hasCanary && !blocked;
            openRestoreCenterButton.IsEnabled = ready;
            checkRecoveryReadinessButton.IsEnabled = healthAvailable && !blocked;
            if (currentSourceConfiguration == null)
            {
                restoreReadinessTitle.Text = "Protected configuration unavailable";
                restoreReadinessDetail.Text = "Repair or refresh the protected installation before browsing snapshots.";
                restoreReadinessTitle.Foreground = Rose;
            }
            else if (!managerAvailable)
            {
                restoreReadinessTitle.Text = "Restore Center is not installed";
                restoreReadinessDetail.Text = "The protected restore manager or backend is missing from the runtime manifest.";
                restoreReadinessTitle.Foreground = Rose;
            }
            else if (!hasCanary)
            {
                restoreReadinessTitle.Text = "Restore canary is missing";
                restoreReadinessDetail.Text = "A verified restore canary is required before app-guided recovery is trusted.";
                restoreReadinessTitle.Foreground = Rose;
            }
            else if (repositoryRecoveryStatus.Exists)
            {
                restoreReadinessTitle.Text = "Repair the interrupted repository move first";
                restoreReadinessDetail.Text = repositoryRecoveryStatus.Message;
                restoreReadinessTitle.Foreground = Amber;
            }
            else if (blocked)
            {
                restoreReadinessTitle.Text = "Restore access is temporarily paused";
                restoreReadinessDetail.Text = "Wait for the active backup or protected configuration change to finish.";
                restoreReadinessTitle.Foreground = Amber;
            }
            else
            {
                restoreReadinessTitle.Text = "Restore Center ready";
                restoreReadinessDetail.Text = "Plan " +
                    currentSourceConfiguration.PlanId.Substring(0, 8) + " · generation " +
                    currentSourceConfiguration.ConfigGeneration.ToString(CultureInfo.CurrentCulture) +
                    " · " + currentSourceConfiguration.RepositoryPath;
                restoreReadinessTitle.Foreground = Green;
            }
            AutomationProperties.SetHelpText(
                openRestoreCenterButton,
                ready
                    ? "Browse snapshots and restore to a separate destination. Windows approval is required."
                    : restoreReadinessDetail.Text);
            AutomationProperties.SetHelpText(
                checkRecoveryReadinessButton,
                checkRecoveryReadinessButton.IsEnabled
                    ? "Run read-only independent repository and recovery checks."
                    : restoreReadinessDetail.Text);
        }

        private void OnCheckRecoveryReadinessClick(object sender, RoutedEventArgs args)
        {
            UpdateRestoreReadiness();
            if (checkRecoveryReadinessButton == null || !checkRecoveryReadinessButton.IsEnabled)
            {
                return;
            }
            try
            {
                SourceConfiguration configuration = SourceConfiguration.Load();
                RecoveryReadinessWindow window = new RecoveryReadinessWindow(
                    configuration,
                    themeResolution.Palette);
                window.Owner = this;
                window.ShowDialog();
                RefreshDashboard();
            }
            catch (Exception error)
            {
                MessageBox.Show(
                    this,
                    "Recovery readiness could not open.\n\n" + error.Message,
                    "Recovery readiness unavailable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnOpenRestoreCenterClick(object sender, RoutedEventArgs args)
        {
            UpdateRestoreReadiness();
            if (openRestoreCenterButton == null || !openRestoreCenterButton.IsEnabled)
            {
                return;
            }
            try
            {
                SourceConfiguration configuration = SourceConfiguration.Load();
                RestoreWindow window = new RestoreWindow(configuration, themeResolution.Palette);
                window.Owner = this;
                window.ShowDialog();
                RefreshDashboard();
            }
            catch (Exception error)
            {
                MessageBox.Show(
                    this,
                    "The Restore Center could not open.\n\n" + error.Message,
                    "Restore Center unavailable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void RebuildSourceList(SourceConfiguration configuration, bool managerAvailable)
        {
            StopSourceOperationAnimation();
            sourceList.Children.Clear();
            removeSourceButtons.Clear();
            sourceRows.Clear();
            ResetSourceOperationVisualReferences();
            bool showPendingItem = ShouldShowPendingSourceItem(configuration);
            bool pendingInserted = false;
            for (int index = 0; index < configuration.Sources.Count; index++)
            {
                BackupSourceView source = configuration.Sources[index];
                FrameworkElement row = (FrameworkElement)BuildSourceRow(source, index, managerAvailable);
                FrameworkElement displayedRow = row;
                if (sourceOperationStage == SourceOperationStage.Succeeded &&
                    string.Equals(source.SourcePath, sourceOperationPath, StringComparison.OrdinalIgnoreCase))
                {
                    displayedRow = BuildResolvedSourceRow(row, source);
                }
                sourceRows[source.SourcePath] = displayedRow;
                sourceList.Children.Add(displayedRow);
                if (showPendingItem && source.IsProtectedCanary)
                {
                    sourceList.Children.Add(BuildSourceOperationItem());
                    pendingInserted = true;
                }
            }
            if (showPendingItem && !pendingInserted)
            {
                sourceList.Children.Insert(0, BuildSourceOperationItem());
            }
        }

        private void UpdateSourceButtons()
        {
            bool managerAvailable = currentSourceConfiguration != null &&
                File.Exists(currentSourceConfiguration.ManagerPath);
            bool backupConflict = scheduleOperationInProgress ||
                repositoryOperationInProgress ||
                RepositoryRecoveryBlocksMutations() ||
                CancellationBlocksMutations(lastSnapshot) ||
                anomalyReviewInProgress ||
                backupStartInProgress ||
                DateTime.UtcNow < backupRequestPendingUntilUtc ||
                (lastSnapshot != null && lastSnapshot.IsActive);
            if (addSourceButton != null)
            {
                addSourceButton.IsEnabled = !sourceOperationInProgress && !backupConflict && managerAvailable;
            }
            foreach (Button removeButton in removeSourceButtons)
            {
                BackupSourceView source = removeButton.Tag as BackupSourceView;
                removeButton.IsEnabled = !sourceOperationInProgress &&
                    !backupConflict &&
                    managerAvailable &&
                    source != null &&
                    !source.IsProtectedCanary;
            }
            UpdateBackupButton(lastSnapshot);
            UpdateCancelButton(lastSnapshot);
            UpdateAnomalyReviewButton(lastSnapshot);
            UpdateScheduleButton();
            UpdateRepositoryButtons();
            UpdateRepositoryRecoverySurface();
        }

        private void UpdateBackupButton(TelemetrySnapshot snapshot)
        {
            if (backupNowButton == null)
            {
                return;
            }

            string label;
            string helpText;
            bool enabled = false;
            bool hasCanary = currentSourceConfiguration != null &&
                currentSourceConfiguration.Sources.Any(source => source.IsProtectedCanary);
            bool pending = DateTime.UtcNow < backupRequestPendingUntilUtc;
            if (pending && HasNewerBackupTelemetry(snapshot))
            {
                backupRequestPendingUntilUtc = DateTime.MinValue;
                backupRequestBaselineRunId = string.Empty;
                pending = false;
            }
            else if (!pending && backupRequestPendingUntilUtc != DateTime.MinValue)
            {
                backupRequestPendingUntilUtc = DateTime.MinValue;
                backupRequestBaselineRunId = string.Empty;
            }

            if (previewEnabled)
            {
                label = "Stop preview first";
                helpText = "Stop the telemetry preview before starting a real backup.";
            }
            else if (backupStartInProgress)
            {
                label = "Waiting for approval…";
                helpText = "Waiting for Windows approval to request the installed backup task.";
            }
            else if (anomalyReviewInProgress)
            {
                label = "Reviewing changes...";
                helpText = "Wait for the exact anomaly-review approval to finish before starting a backup.";
            }
            else if (CancellationBlocksMutations(snapshot))
            {
                label = "Stopping backup…";
                helpText = "The exact active Restic run is handling a cooperative cancellation request.";
            }
            else if (snapshot != null && snapshot.IsActive)
            {
                backupRequestPendingUntilUtc = DateTime.MinValue;
                label = "Backup running";
                helpText = "The protected Restic backup is already running.";
            }
            else if (pending)
            {
                label = "Request sent…";
                helpText = "Windows accepted the request. Waiting for protected backup telemetry.";
            }
            else if (RepositoryRecoveryBlocksMutations())
            {
                label = repositoryRecoveryInProgress
                    ? "Repairing storage…"
                    : "Backup paused";
                helpText = repositoryRecoveryInProgress
                    ? "Wait for protected repository recovery to finish."
                    : "Repair the interrupted repository move before starting another backup.";
            }
            else if (scheduleOperationInProgress)
            {
                label = "Schedule updating…";
                helpText = "Wait for the protected schedule change to be verified before starting a backup.";
            }
            else if (sourceOperationInProgress)
            {
                label = "Folders updating…";
                helpText = "Wait for the protected folder change to finish before starting a backup.";
            }
            else if (repositoryOperationInProgress)
            {
                label = "Storage relocating…";
                helpText = "Wait for the protected repository relocation to finish before starting a backup.";
            }
            else if (!hasCanary)
            {
                label = "Backup unavailable";
                helpText = "The required restore canary must be present before a backup can be requested.";
            }
            else
            {
                label = "Back up now";
                helpText = "Start the installed Restic backup now. Windows approval is required.";
                enabled = true;
            }

            backupNowButton.Content = label;
            backupNowButton.IsEnabled = enabled;
            backupNowButton.ToolTip = helpText;
            AutomationProperties.SetName(backupNowButton, label);
            AutomationProperties.SetHelpText(backupNowButton, helpText);
            AutomationProperties.SetItemStatus(backupNowButton, helpText);
            UpdateCancelButton(snapshot);
        }

        private bool CancellationBlocksMutations(TelemetrySnapshot snapshot)
        {
            return cancellationRequestInProgress ||
                cancellationAwaitingTerminal ||
                (snapshot != null && string.Equals(
                    snapshot.StateKey,
                    "cancelling",
                    StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateCancelButton(TelemetrySnapshot snapshot)
        {
            if (cancelBackupButton == null)
            {
                return;
            }

            bool hasActiveRun = !previewEnabled &&
                snapshot != null &&
                snapshot.IsActive &&
                !string.IsNullOrWhiteSpace(snapshot.RunId);
            bool unavailableForRun = hasActiveRun && string.Equals(
                cancellationUnavailableRunId,
                snapshot.RunId,
                StringComparison.OrdinalIgnoreCase);
            bool taskRunning = currentTaskSchedule != null &&
                currentTaskSchedule.State == BackupTaskState.Running;
            string label;
            string helpText;
            bool enabled = false;

            if (cancellationRequestInProgress)
            {
                label = "Waiting for approval…";
                helpText = "Waiting for Windows approval to signal only the exact active backup run.";
            }
            else if (cancellationAwaitingTerminal ||
                (snapshot != null && string.Equals(
                    snapshot.StateKey,
                    "cancelling",
                    StringComparison.OrdinalIgnoreCase)))
            {
                label = snapshot != null && snapshot.IsCancelled
                    ? "Checking final state…"
                    : "Stopping safely…";
                helpText = "Restic is handling a cooperative Ctrl-Break request. No task or process is being hard-killed.";
            }
            else if (hasActiveRun && unavailableForRun)
            {
                label = "Cancellation unavailable";
                helpText = "The cooperative signal could not be delivered to this run. The backup is still running.";
            }
            else if (hasActiveRun)
            {
                label = "Cancel backup";
                if (!taskRunning)
                {
                    helpText = "The protected task is not independently confirmed as Running yet.";
                }
                else if (sourceOperationInProgress || scheduleOperationInProgress ||
                    repositoryOperationInProgress || backupStartInProgress)
                {
                    helpText = "Wait for the current protected operation to finish before requesting cancellation.";
                }
                else
                {
                    helpText = "Ask the exact active Restic run to stop cooperatively. Existing verified snapshots are not changed.";
                    enabled = true;
                }
            }
            else
            {
                label = "Cancel backup";
                helpText = "Available while an exact protected backup run is active.";
            }

            cancelBackupButton.Visibility = Visibility.Visible;
            cancelBackupButton.Content = label;
            cancelBackupButton.IsEnabled = enabled;
            cancelBackupButton.ToolTip = helpText;
            bool stopping = cancellationRequestInProgress || cancellationAwaitingTerminal ||
                (snapshot != null && string.Equals(
                    snapshot.StateKey,
                    "cancelling",
                    StringComparison.OrdinalIgnoreCase));
            cancelBackupButton.Background = enabled ? Brushes.Transparent : CardSoftBrush;
            cancelBackupButton.BorderBrush = enabled ? Rose : stopping ? Amber : CardBorderBrush;
            cancelBackupButton.Foreground = enabled ? Rose : stopping ? Amber : MutedText;
            AutomationProperties.SetName(cancelBackupButton, label);
            AutomationProperties.SetHelpText(cancelBackupButton, helpText);
            AutomationProperties.SetItemStatus(cancelBackupButton, helpText);

            if (cancelActionStatus != null)
            {
                if (!string.IsNullOrWhiteSpace(cancellationNotice) &&
                    (cancellationNoticeExpiresUtc == DateTime.MaxValue ||
                        DateTime.UtcNow < cancellationNoticeExpiresUtc))
                {
                    cancelActionStatus.Text = cancellationNotice;
                    cancelActionStatus.Foreground = cancellationNoticeColor ?? MutedText;
                    cancelActionStatus.Visibility = Visibility.Visible;
                }
                else
                {
                    cancellationNotice = string.Empty;
                    cancellationNoticeExpiresUtc = DateTime.MinValue;
                    cancelActionStatus.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void UpdateAnomalyReviewButton(TelemetrySnapshot snapshot)
        {
            if (reviewChangesButton == null)
            {
                return;
            }

            bool needsReview = snapshot != null && snapshot.NeedsAnomalyReview;
            bool helperAvailable = currentSourceConfiguration != null &&
                File.Exists(System.IO.Path.Combine(
                    currentSourceConfiguration.InstallRoot,
                    "anomaly_review.py")) &&
                File.Exists(System.IO.Path.Combine(
                    currentSourceConfiguration.StateDirectory,
                    "last-success.json"));
            bool conflict = previewEnabled || backupStartInProgress ||
                CancellationBlocksMutations(snapshot) ||
                sourceOperationInProgress || scheduleOperationInProgress ||
                repositoryOperationInProgress || RepositoryRecoveryBlocksMutations() ||
                (snapshot != null && snapshot.IsActive);
            bool enabled = needsReview && !anomalyReviewInProgress &&
                helperAvailable && !conflict;
            string label = anomalyReviewInProgress
                ? "Waiting for approval..."
                : "Review changes";
            string helpText;
            if (anomalyReviewInProgress)
            {
                helpText = "Windows is recording a plan-bound approval for this exact verified generation.";
            }
            else if (!helperAvailable)
            {
                helpText = "The protected anomaly-review helper or latest verified evidence is unavailable.";
            }
            else if (conflict)
            {
                helpText = "Wait for the current protected operation to finish before reviewing this generation.";
            }
            else
            {
                helpText = "Review and acknowledge only this exact generation. The deletion and retention hold remains; DriveFS upload is not paused.";
            }

            reviewChangesButton.Visibility = needsReview || anomalyReviewInProgress
                ? Visibility.Visible
                : Visibility.Collapsed;
            reviewChangesButton.Content = label;
            reviewChangesButton.IsEnabled = enabled;
            reviewChangesButton.ToolTip = helpText;
            reviewChangesButton.Background = enabled
                ? BrushFrom("#3A2F18")
                : CardSoftBrush;
            reviewChangesButton.BorderBrush = enabled ? Amber : CardBorderBrush;
            reviewChangesButton.Foreground = enabled ? Amber : MutedText;
            AutomationProperties.SetName(reviewChangesButton, label);
            AutomationProperties.SetHelpText(reviewChangesButton, helpText);
            AutomationProperties.SetItemStatus(reviewChangesButton, helpText);
        }

        private async void OnReviewChangesClick(object sender, RoutedEventArgs args)
        {
            TelemetrySnapshot snapshot = lastSnapshot;
            UpdateAnomalyReviewButton(snapshot);
            if (anomalyReviewInProgress || snapshot == null ||
                !snapshot.NeedsAnomalyReview || currentSourceConfiguration == null ||
                !reviewChangesButton.IsEnabled)
            {
                return;
            }

            string snapshotShort = snapshot.SnapshotId.Length > 12
                ? snapshot.SnapshotId.Substring(0, 12)
                : snapshot.SnapshotId;
            string detail = string.IsNullOrWhiteSpace(snapshot.MaintenanceHoldDetail)
                ? "Restic detected an unusually large change set."
                : snapshot.MaintenanceHoldDetail;
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                "Acknowledge this exact verified change set?\n\n" +
                    detail + "\n\n" +
                    "Run: " + snapshot.RunId + "\n" +
                    "Snapshot: " + snapshotShort + "\n\n" +
                    "This acknowledgement:\n" +
                    "\u2022 records a review bound only to this exact generation\n" +
                    "\u2022 does not pause or gate Google Drive upload from the live DriveFS repository\n" +
                    "\u2022 leaves the maintenance hold in place for deletion and retention actions\n" +
                    "\u2022 does not modify snapshots or repository data\n\n" +
                    "Windows administrator approval is required.",
                "Review suspicious backup changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            if (lastSnapshot == null || !lastSnapshot.NeedsAnomalyReview ||
                !string.Equals(lastSnapshot.RunId, snapshot.RunId, StringComparison.Ordinal) ||
                !string.Equals(
                    lastSnapshot.SnapshotId,
                    snapshot.SnapshotId,
                    StringComparison.Ordinal) ||
                currentSourceConfiguration == null)
            {
                RefreshDashboard();
                return;
            }

            SourceConfiguration configuration = currentSourceConfiguration;
            anomalyReviewInProgress = true;
            UpdateSourceButtons();
            AnomalyReviewResult result;
            try
            {
                result = await Task.Run(delegate
                {
                    return AnomalyReviewLauncher.AcknowledgeReview(
                        configuration,
                        snapshot,
                        delegate
                        {
                            Dispatcher.BeginInvoke(
                                new Action(delegate
                                {
                                    if (anomalyReviewInProgress && reviewChangesButton != null)
                                    {
                                        reviewChangesButton.Content = "Recording approval...";
                                        AutomationProperties.SetName(
                                            reviewChangesButton,
                                            "Recording exact anomaly approval");
                                    }
                                }),
                                DispatcherPriority.Background);
                        });
                });
            }
            catch (Exception error)
            {
                result = AnomalyReviewResult.Failure(error.Message);
            }
            finally
            {
                anomalyReviewInProgress = false;
            }

            RefreshDashboard();
            UpdateSourceButtons();
            if (result.UserCancelled)
            {
                MessageBox.Show(
                    this,
                    "Windows approval was canceled. No review acknowledgement was recorded, and no snapshot or repository data was changed. DriveFS upload was never paused.",
                    "Review canceled",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else if (!result.Succeeded)
            {
                MessageBox.Show(
                    this,
                    result.ErrorMessage ??
                        "The exact change set was not acknowledged.",
                    "Changes not acknowledged",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            else
            {
                ShowTrayMessage(
                    "Changes acknowledged",
                    "This exact verified generation was reviewed. The deletion and retention hold remains; DriveFS upload is not paused.",
                    Forms.ToolTipIcon.Info);
            }
        }

        private void SetCancellationStatus(
            string message,
            Brush foreground,
            int visibleSeconds)
        {
            cancellationNotice = message ?? string.Empty;
            cancellationNoticeColor = foreground ?? MutedText;
            cancellationNoticeExpiresUtc = visibleSeconds > 0
                ? DateTime.UtcNow.AddSeconds(visibleSeconds)
                : DateTime.MaxValue;
            if (cancelActionStatus == null)
            {
                return;
            }
            cancelActionStatus.Text = cancellationNotice;
            cancelActionStatus.Foreground = cancellationNoticeColor;
            cancelActionStatus.Visibility = string.IsNullOrWhiteSpace(cancellationNotice)
                ? Visibility.Collapsed
                : Visibility.Visible;
            AutomationProperties.SetName(cancelActionStatus, cancellationNotice);
            AutomationProperties.SetLiveSetting(
                cancelActionStatus,
                foreground == Rose
                    ? AutomationLiveSetting.Assertive
                    : AutomationLiveSetting.Polite);
            if (SystemParameters.ClientAreaAnimation &&
                cancelActionStatus.Visibility == Visibility.Visible)
            {
                DoubleAnimation reveal = new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(260));
                cancelActionStatus.BeginAnimation(UIElement.OpacityProperty, reveal);
            }
            AutomationPeer peer = UIElementAutomationPeer.CreatePeerForElement(cancelActionStatus);
            if (peer != null)
            {
                peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
        }

        private void ObserveCancellationState(TelemetrySnapshot snapshot)
        {
            if (!cancellationAwaitingTerminal || snapshot == null)
            {
                return;
            }

            if (!string.Equals(
                snapshot.RunId,
                cancellationRequestedRunId,
                StringComparison.OrdinalIgnoreCase))
            {
                cancellationAwaitingTerminal = false;
                cancellationRequestedRunId = string.Empty;
                SetCancellationStatus(
                    "The protected run changed before cancellation could be independently confirmed.",
                    Rose,
                    18);
                return;
            }

            if (string.Equals(
                snapshot.CancelOutcome,
                "ctrl_break_signal_failed",
                StringComparison.OrdinalIgnoreCase) && snapshot.IsActive)
            {
                cancellationAwaitingTerminal = false;
                cancellationUnavailableRunId = snapshot.RunId;
                cancellationRequestedRunId = string.Empty;
                SetCancellationStatus(
                    "The safe stop signal could not be delivered. The backup is still running and was not hard-killed.",
                    Rose,
                    0);
                return;
            }

            if (string.Equals(
                snapshot.StateKey,
                "cancelling",
                StringComparison.OrdinalIgnoreCase))
            {
                SetCancellationStatus(
                    "Restic is stopping safely…",
                    Amber,
                    0);
                return;
            }

            if (snapshot.IsCancelled)
            {
                nextScheduleRefreshUtc = DateTime.MinValue;
                if (currentTaskSchedule == null ||
                    currentTaskSchedule.State == BackupTaskState.Running ||
                    currentTaskSchedule.State == BackupTaskState.Unknown)
                {
                    SetCancellationStatus(
                        "Restic stopped. Checking that the protected task and process tree have exited…",
                        BlueText,
                        0);
                    return;
                }

                cancellationAwaitingTerminal = false;
                cancellationRequestedRunId = string.Empty;
                SetCancellationStatus(
                    "Backup canceled safely. Previously verified snapshots remain available.",
                    Green,
                    18);
                ShowTrayMessage(
                    "Backup canceled safely",
                    "The active run stopped cooperatively. Previously verified snapshots remain available.",
                    Forms.ToolTipIcon.Info);
                return;
            }

            if (snapshot.IsSuccess)
            {
                cancellationAwaitingTerminal = false;
                cancellationRequestedRunId = string.Empty;
                SetCancellationStatus(
                    "The backup finished and was verified before cancellation took effect.",
                    Green,
                    18);
                return;
            }

            if (snapshot.IsFailure)
            {
                cancellationAwaitingTerminal = false;
                cancellationRequestedRunId = string.Empty;
                SetCancellationStatus(
                    "The run stopped with an error and was not marked successful. Review the protected log.",
                    Rose,
                    0);
                return;
            }

            SetCancellationStatus(
                "Cancellation was requested. Waiting for Restic and the protected task to report their final state…",
                Amber,
                0);
        }

        private async void OnCancelBackupClick(object sender, RoutedEventArgs args)
        {
            TelemetrySnapshot snapshot = lastSnapshot;
            if (cancellationRequestInProgress || cancellationAwaitingTerminal ||
                previewEnabled || sourceOperationInProgress || scheduleOperationInProgress ||
                backupStartInProgress || snapshot == null || !snapshot.IsActive ||
                string.IsNullOrWhiteSpace(snapshot.RunId))
            {
                UpdateCancelButton(snapshot);
                return;
            }

            nextScheduleRefreshUtc = DateTime.MinValue;
            RefreshSchedule(true);
            if (currentTaskSchedule == null ||
                currentTaskSchedule.State != BackupTaskState.Running)
            {
                SetCancellationStatus(
                    "Cancellation was not requested because the protected task is no longer confirmed as Running.",
                    Amber,
                    12);
                UpdateCancelButton(snapshot);
                return;
            }

            MessageBoxResult confirmation = MessageBox.Show(
                this,
                "Cancel this backup?\n\n" +
                    "Restic will be asked to stop cleanly. Previously verified snapshots are not changed, " +
                    "and this run will not be marked successful. A run that is already completing may finish " +
                    "before cancellation takes effect.",
                "Cancel running backup?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            snapshot = lastSnapshot;
            if (cancellationRequestInProgress || cancellationAwaitingTerminal ||
                snapshot == null || !snapshot.IsActive ||
                string.IsNullOrWhiteSpace(snapshot.RunId))
            {
                UpdateCancelButton(snapshot);
                return;
            }

            string runId = snapshot.RunId;
            cancellationRequestInProgress = true;
            cancellationRequestedRunId = runId;
            SetCancellationStatus("Waiting for Windows administrator approval…", BlueText, 0);
            UpdateSourceButtons();

            BackupCancellationResult result;
            try
            {
                result = await Task.Run(delegate
                {
                    return BackupCancellationController.RequestCancel(
                        runId,
                        delegate
                        {
                            Dispatcher.BeginInvoke(
                                new Action(delegate
                                {
                                    SetCancellationStatus(
                                        "Sending a protected cancellation request…",
                                        BlueText,
                                        0);
                                    UpdateCancelButton(lastSnapshot);
                                }),
                                DispatcherPriority.Background);
                        });
                });
            }
            catch (Exception error)
            {
                result = BackupCancellationResult.Failure(error.Message, -1);
            }

            cancellationRequestInProgress = false;
            if (result.UserCancelled)
            {
                cancellationRequestedRunId = string.Empty;
                SetCancellationStatus(
                    "Backup kept running — administrator approval was canceled.",
                    MutedText,
                    10);
            }
            else if (result.AlreadyFinished)
            {
                cancellationRequestedRunId = string.Empty;
                SetCancellationStatus(
                    "The backup finished before cancellation could be requested.",
                    Green,
                    12);
            }
            else if (result.Requested)
            {
                cancellationAwaitingTerminal = true;
                nextScheduleRefreshUtc = DateTime.MinValue;
                SetCancellationStatus(
                    "Restic is stopping safely…",
                    Amber,
                    0);
            }
            else
            {
                cancellationRequestedRunId = string.Empty;
                SetCancellationStatus(
                    "Safe cancellation was not requested. The backup is still running.",
                    Rose,
                    15);
                MessageBox.Show(
                    this,
                    "The cooperative cancellation request was rejected. The backup was not hard-stopped.\n\n" +
                        (result.ErrorMessage ?? "The protected cancellation helper did not return a verified result."),
                    "Cancellation not requested",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            UpdateSourceButtons();
        }

        private void UpdateScheduleButton()
        {
            if (editScheduleButton == null)
            {
                return;
            }
            bool backupConflict = backupStartInProgress || anomalyReviewInProgress ||
                CancellationBlocksMutations(lastSnapshot) ||
                DateTime.UtcNow < backupRequestPendingUntilUtc ||
                (lastSnapshot != null && lastSnapshot.IsActive);
            string helpText;
            if (scheduleOperationInProgress)
            {
                helpText = "The protected schedule change is still being applied and verified.";
            }
            else if (previewEnabled)
            {
                helpText = "Stop the telemetry preview before changing the automatic schedule.";
            }
            else if (backupConflict)
            {
                helpText = "Wait for the current backup to finish before changing its schedule.";
            }
            else if (sourceOperationInProgress)
            {
                helpText = "Wait for the protected folder change to finish before changing the schedule.";
            }
            else if (RepositoryRecoveryBlocksMutations())
            {
                helpText = repositoryRecoveryInProgress
                    ? "Wait for protected repository recovery to finish before changing the schedule."
                    : "Repair the interrupted repository move before changing the schedule.";
            }
            else if (repositoryOperationInProgress)
            {
                helpText = "Wait for the protected repository relocation to finish before changing the schedule.";
            }
            else if (currentTaskSchedule == null)
            {
                helpText = string.IsNullOrWhiteSpace(scheduleReadError)
                    ? "The installed backup schedule is unavailable."
                    : scheduleReadError;
            }
            else
            {
                helpText = "Change the protected Windows backup schedule. Saving does not start a backup.";
            }
            editScheduleButton.Content = scheduleOperationInProgress
                ? "Updating schedule…"
                : "Edit schedule";
            editScheduleButton.IsEnabled = !scheduleOperationInProgress &&
                !previewEnabled &&
                !backupConflict &&
                !sourceOperationInProgress &&
                !repositoryOperationInProgress &&
                !RepositoryRecoveryBlocksMutations() &&
                currentTaskSchedule != null;
            editScheduleButton.ToolTip = helpText;
            AutomationProperties.SetHelpText(editScheduleButton, helpText);
            AutomationProperties.SetItemStatus(editScheduleButton, helpText);
        }

        private void SetScheduleStatus(string message, Brush foreground)
        {
            if (scheduleActionStatus == null)
            {
                return;
            }
            scheduleActionStatus.Text = message;
            scheduleActionStatus.Foreground = foreground ?? MutedText;
            AutomationProperties.SetName(scheduleActionStatus, message);
            AutomationPeer peer = UIElementAutomationPeer.CreatePeerForElement(scheduleActionStatus);
            if (peer != null)
            {
                peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
        }

        private void UpdateRepositoryButtons()
        {
            string label = repositoryRecoveryInProgress
                ? "Repairing…"
                : repositoryOperationInProgress
                    ? "Relocating…"
                    : "Change location";
            bool managerAvailable = currentSourceConfiguration != null &&
                File.Exists(System.IO.Path.Combine(
                    currentSourceConfiguration.InstallRoot,
                    "Manage-Repository.ps1"));
            bool backupConflict = backupStartInProgress || anomalyReviewInProgress ||
                CancellationBlocksMutations(lastSnapshot) ||
                DateTime.UtcNow < backupRequestPendingUntilUtc ||
                (lastSnapshot != null && lastSnapshot.IsActive);
            bool enabled = !repositoryOperationInProgress &&
                !RepositoryRecoveryBlocksMutations() &&
                !previewEnabled &&
                !sourceOperationInProgress &&
                !scheduleOperationInProgress &&
                !backupConflict &&
                managerAvailable;
            string helpText;
            if (repositoryRecoveryInProgress)
            {
                helpText = "The protected interrupted-move recovery is still running.";
            }
            else if (repositoryOperationInProgress)
            {
                helpText = "The protected repository is being copied, verified, and activated.";
            }
            else if (RepositoryRecoveryBlocksMutations())
            {
                helpText = repositoryRecoveryInProgress
                    ? "Wait for protected repository recovery to finish before changing storage."
                    : "Repair the interrupted repository move before changing storage.";
            }
            else if (previewEnabled)
            {
                helpText = "Stop the telemetry preview before changing backup storage.";
            }
            else if (backupConflict)
            {
                helpText = "Wait for the current backup operation to finish before changing storage.";
            }
            else if (sourceOperationInProgress)
            {
                helpText = "Wait for the protected folder change to finish before changing storage.";
            }
            else if (scheduleOperationInProgress)
            {
                helpText = "Wait for the protected schedule change to finish before changing storage.";
            }
            else if (currentSourceConfiguration == null)
            {
                helpText = "The protected backup configuration is unavailable.";
            }
            else if (!managerAvailable)
            {
                helpText = "The protected repository manager is not installed.";
            }
            else
            {
                helpText = "Copy and verify the Restic repository in a new location. " +
                    "The old repository is retained and no backup starts.";
            }
            ApplyRepositoryButtonState(changeRepositoryButton, label, enabled, helpText);
            ApplyRepositoryButtonState(settingsChangeRepositoryButton, label, enabled, helpText);
        }

        private static void ApplyRepositoryButtonState(
            Button button,
            string label,
            bool enabled,
            string helpText)
        {
            if (button == null)
            {
                return;
            }
            button.Content = label;
            button.IsEnabled = enabled;
            button.ToolTip = helpText;
            AutomationProperties.SetHelpText(button, helpText);
            AutomationProperties.SetItemStatus(button, helpText);
        }

        private async void OnRepairRepositoryClick(object sender, RoutedEventArgs args)
        {
            RefreshRepositoryRecoveryStatus();
            if (repositoryRecoveryInProgress ||
                repositoryOperationInProgress ||
                repositoryRecoveryStatus == null ||
                !repositoryRecoveryStatus.Exists ||
                !repositoryRecoveryStatus.IsRecoverable ||
                !RepositoryRecoveryManagerAvailable() ||
                previewEnabled ||
                RepositoryRecoveryHasConflictingOperation())
            {
                UpdateRepositoryRecoverySurface();
                return;
            }

            MessageBoxResult confirmation = MessageBox.Show(
                this,
                "Repair the interrupted repository move now?\n\n" +
                    "The protected recovery helper will validate the journal and restore a verified safe " +
                    "repository and configuration state. Backups remain paused until the protected journal " +
                    "is removed. No backup starts and no snapshot is deleted.",
                "Repair interrupted repository move?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            RefreshRepositoryRecoveryStatus();
            if (repositoryRecoveryStatus == null ||
                !repositoryRecoveryStatus.Exists ||
                !repositoryRecoveryStatus.IsRecoverable ||
                !RepositoryRecoveryManagerAvailable() ||
                previewEnabled ||
                RepositoryRecoveryHasConflictingOperation())
            {
                SetRepositoryRecoveryOperationStatus(
                    "Protected recovery was not started because the recovery state changed. Review this warning and try again.",
                    null);
                UpdateSourceButtons();
                return;
            }

            repositoryRecoveryInProgress = true;
            repositoryOperationInProgress = true;
            SetRepositoryRecoveryOperationStatus(
                "Waiting for Windows administrator approval...",
                null);
            UpdateSourceButtons();

            RepositoryManagerResult result;
            try
            {
                result = await Task.Run(delegate
                {
                    return RepositoryManagerLauncher.Recover(
                        delegate
                        {
                            Dispatcher.BeginInvoke(
                                new Action(delegate
                                {
                                    if (repositoryRecoveryInProgress)
                                    {
                                        SetRepositoryRecoveryOperationStatus(
                                            "Protected repository recovery is running...",
                                            null);
                                    }
                                }),
                                DispatcherPriority.Background);
                        },
                        delegate(RepositoryProgress progress)
                        {
                            Dispatcher.BeginInvoke(
                                new Action(delegate
                                {
                                    ApplyRepositoryRecoveryProgress(progress);
                                }),
                                DispatcherPriority.Background);
                        });
                });
            }
            catch (Exception error)
            {
                result = RepositoryManagerResult.Failure(error.Message, -1);
            }
            finally
            {
                repositoryRecoveryInProgress = false;
                repositoryOperationInProgress = false;
                repositoryRecoveryOperationPercent = null;
            }

            RefreshRepositoryRecoveryStatus();
            bool journalCleared = repositoryRecoveryStatus != null &&
                !repositoryRecoveryStatus.Exists;
            if (result.UserCancelled)
            {
                repositoryRecoveryOperationMessage =
                    "Administrator approval was canceled. Backups remain paused until repair completes.";
            }
            else if (!result.Succeeded)
            {
                string detail = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "The protected recovery helper did not return a verified result."
                    : result.ErrorMessage;
                repositoryRecoveryOperationMessage =
                    "Repair failed: " + detail + " Backups remain paused.";
            }
            else if (!journalCleared)
            {
                repositoryRecoveryOperationMessage =
                    "Protected recovery returned success, but the interruption journal still exists. Backups remain paused.";
            }
            else
            {
                repositoryRecoveryOperationMessage = string.Empty;
            }

            sourceSignature = null;
            nextScheduleRefreshUtc = DateTime.MinValue;
            RefreshDashboard();

            if (result.UserCancelled)
            {
                MessageBox.Show(
                    this,
                    "Windows approval was canceled. Nothing was bypassed or dismissed; backups remain paused until repair succeeds.",
                    "Repository repair canceled",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else if (!result.Succeeded)
            {
                MessageBox.Show(
                    this,
                    repositoryRecoveryOperationMessage,
                    "Repository repair failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            else if (!journalCleared)
            {
                MessageBox.Show(
                    this,
                    repositoryRecoveryOperationMessage,
                    "Repository repair not confirmed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            else
            {
                ShowTrayMessage(
                    "Repository move repaired",
                    "The protected repository state was verified. Backup controls are available again.",
                    Forms.ToolTipIcon.Info);
            }
        }

        private void OnChangeRepositoryClick(object sender, RoutedEventArgs args)
        {
            UpdateRepositoryButtons();
            if (repositoryOperationInProgress || anomalyReviewInProgress || previewEnabled ||
                RepositoryRecoveryBlocksMutations() ||
                sourceOperationInProgress || scheduleOperationInProgress ||
                backupStartInProgress || CancellationBlocksMutations(lastSnapshot) ||
                DateTime.UtcNow < backupRequestPendingUntilUtc ||
                currentSourceConfiguration == null ||
                (lastSnapshot != null && lastSnapshot.IsActive))
            {
                return;
            }
            string managerPath = System.IO.Path.Combine(
                currentSourceConfiguration.InstallRoot,
                "Manage-Repository.ps1");
            if (!File.Exists(managerPath))
            {
                UpdateRepositoryButtons();
                return;
            }

            repositoryOperationInProgress = true;
            UpdateSourceButtons();
            bool changed = false;
            try
            {
                RepositoryLocationWindow window = new RepositoryLocationWindow(
                    currentSourceConfiguration,
                    themeResolution.Palette);
                window.Owner = this;
                window.ShowDialog();
                changed = window.RepositoryChanged;
            }
            finally
            {
                repositoryOperationInProgress = false;
                sourceSignature = null;
                RefreshSources(true);
                UpdateSourceButtons();
            }
            if (changed)
            {
                ShowTrayMessage(
                    "Backup location changed",
                    "The new Restic repository is active. The previous repository was retained.",
                    Forms.ToolTipIcon.Info);
            }
        }

        private async void OnEditScheduleClick(object sender, RoutedEventArgs args)
        {
            if (scheduleOperationInProgress || anomalyReviewInProgress || previewEnabled || sourceOperationInProgress ||
                repositoryOperationInProgress ||
                RepositoryRecoveryBlocksMutations() ||
                CancellationBlocksMutations(lastSnapshot) ||
                backupStartInProgress || DateTime.UtcNow < backupRequestPendingUntilUtc ||
                (lastSnapshot != null && lastSnapshot.IsActive))
            {
                UpdateScheduleButton();
                return;
            }

            nextScheduleRefreshUtc = DateTime.MinValue;
            RefreshSchedule(true);
            if (currentTaskSchedule == null)
            {
                MessageBox.Show(
                    this,
                    "The installed backup schedule could not be verified.\n\n" + scheduleReadError,
                    "Schedule unavailable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            ScheduleEditorWindow editor = new ScheduleEditorWindow(
                currentTaskSchedule,
                themeResolution.Palette);
            editor.Owner = this;
            bool? accepted = editor.ShowDialog();
            ScheduleChangeRequest requested = editor.RequestedChange;
            if (!accepted.HasValue || !accepted.Value || requested == null)
            {
                return;
            }

            if (scheduleOperationInProgress || anomalyReviewInProgress || sourceOperationInProgress ||
                repositoryOperationInProgress || RepositoryRecoveryBlocksMutations() ||
                backupStartInProgress ||
                CancellationBlocksMutations(lastSnapshot) ||
                DateTime.UtcNow < backupRequestPendingUntilUtc ||
                (lastSnapshot != null && lastSnapshot.IsActive))
            {
                SetScheduleStatus("Schedule not changed because a protected operation started.", Amber);
                UpdateScheduleButton();
                return;
            }

            scheduleOperationInProgress = true;
            SetScheduleStatus("Waiting for Windows administrator approval…", BlueText);
            UpdateSourceButtons();

            ScheduleManagerResult managerResult;
            try
            {
                managerResult = await Task.Run(delegate
                {
                    return ScheduleManagerLauncher.Run(
                        requested,
                        delegate
                        {
                            Dispatcher.BeginInvoke(
                                new Action(delegate
                                {
                                    SetScheduleStatus("Updating the protected Windows schedule…", BlueText);
                                }),
                                DispatcherPriority.Background);
                        });
                });
            }
            catch (Exception error)
            {
                managerResult = ScheduleManagerResult.Failure(error.Message, -1);
            }

            if (managerResult.UserCancelled)
            {
                scheduleOperationInProgress = false;
                SetScheduleStatus("Schedule unchanged — administrator approval was cancelled.", MutedText);
                nextScheduleRefreshUtc = DateTime.MinValue;
                RefreshSchedule(true);
                UpdateSourceButtons();
                return;
            }

            SetScheduleStatus("Verifying the installed schedule…", BlueText);
            TaskScheduleReadResult independentRead = TaskScheduleReader.ReadInstalled();
            bool independentlyVerified = managerResult.Succeeded &&
                independentRead.Succeeded &&
                independentRead.Schedule != null &&
                independentRead.Schedule.Matches(requested);
            scheduleOperationInProgress = false;
            nextScheduleRefreshUtc = DateTime.UtcNow.AddSeconds(30);

            if (independentlyVerified)
            {
                currentTaskSchedule = independentRead.Schedule;
                scheduleReadError = string.Empty;
                SetScheduleStatus("Schedule updated and independently verified.", Green);
            }
            else
            {
                string detail = !managerResult.Succeeded
                    ? managerResult.ErrorMessage
                    : !independentRead.Succeeded
                        ? independentRead.ErrorMessage
                        : "The installed task did not match the reviewed settings.";
                scheduleReadError = detail ?? "The schedule could not be verified.";
                currentTaskSchedule = independentRead.Succeeded
                    ? independentRead.Schedule
                    : null;
                SetScheduleStatus("Schedule change was not verified.", Rose);
                MessageBox.Show(
                    this,
                    "The schedule was not reported as changed.\n\n" + scheduleReadError,
                    "Schedule change failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            UpdateHeaderSubtitle();
            if (lastSnapshot != null)
            {
                ApplySnapshot(lastSnapshot);
            }
            UpdateSourceButtons();
        }

        private bool HasNewerBackupTelemetry(TelemetrySnapshot snapshot)
        {
            if (snapshot == null)
            {
                return false;
            }
            if (!string.IsNullOrWhiteSpace(snapshot.RunId) &&
                !string.Equals(
                    snapshot.RunId,
                    backupRequestBaselineRunId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        private bool BackupBlocksSourceChanges()
        {
            return scheduleOperationInProgress ||
                repositoryOperationInProgress ||
                RepositoryRecoveryBlocksMutations() ||
                CancellationBlocksMutations(lastSnapshot) ||
                anomalyReviewInProgress ||
                backupStartInProgress ||
                DateTime.UtcNow < backupRequestPendingUntilUtc ||
                (lastSnapshot != null && lastSnapshot.IsActive);
        }

        private async void OnBackupNowClick(object sender, RoutedEventArgs args)
        {
            if (backupStartInProgress || anomalyReviewInProgress || previewEnabled || sourceOperationInProgress ||
                repositoryOperationInProgress ||
                RepositoryRecoveryBlocksMutations() ||
                CancellationBlocksMutations(lastSnapshot) ||
                scheduleOperationInProgress ||
                DateTime.UtcNow < backupRequestPendingUntilUtc ||
                currentSourceConfiguration == null ||
                !currentSourceConfiguration.Sources.Any(source => source.IsProtectedCanary) ||
                (lastSnapshot != null && lastSnapshot.IsActive))
            {
                return;
            }

            MessageBoxResult confirmation = MessageBox.Show(
                this,
                "Start a real backup now?\n\n" +
                    "This reads all protected folders and may take a while. " +
                    "Your automatic schedule is unchanged.",
                "Start backup now?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }
            if (backupStartInProgress || anomalyReviewInProgress || previewEnabled || sourceOperationInProgress ||
                repositoryOperationInProgress ||
                RepositoryRecoveryBlocksMutations() ||
                CancellationBlocksMutations(lastSnapshot) ||
                scheduleOperationInProgress ||
                DateTime.UtcNow < backupRequestPendingUntilUtc ||
                currentSourceConfiguration == null ||
                !currentSourceConfiguration.Sources.Any(source => source.IsProtectedCanary) ||
                (lastSnapshot != null && lastSnapshot.IsActive))
            {
                UpdateBackupButton(lastSnapshot);
                return;
            }

            string baselineRunId = lastSnapshot == null ? string.Empty : lastSnapshot.RunId;
            backupStartInProgress = true;
            UpdateBackupButton(lastSnapshot);
            BackupTaskRequestResult result;
            try
            {
                result = await Task.Run(delegate { return BackupTaskController.RequestStart(); });
            }
            catch (Exception error)
            {
                result = BackupTaskRequestResult.Failure(error.Message, -1);
            }
            finally
            {
                backupStartInProgress = false;
            }

            if (result.UserCancelled)
            {
                MessageBox.Show(
                    this,
                    "Windows approval was cancelled. No backup was requested.",
                    "Backup request cancelled",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else if (!result.Succeeded)
            {
                string detail = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "Windows Task Scheduler could not accept the request."
                    : result.ErrorMessage;
                MessageBox.Show(
                    this,
                    "The backup was not requested.\n\n" + detail,
                    "Backup request failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            else
            {
                backupRequestBaselineRunId = baselineRunId ?? string.Empty;
                backupRequestPendingUntilUtc = DateTime.UtcNow.AddSeconds(45);
                ShowTrayMessage(
                    "Backup requested",
                    "Windows accepted the request. Live progress will appear when the task starts.",
                    Forms.ToolTipIcon.Info);
            }

            RefreshDashboard();
            UpdateBackupButton(lastSnapshot);
        }

        private async void OnAddSourceClick(object sender, RoutedEventArgs args)
        {
            if (sourceOperationInProgress || BackupBlocksSourceChanges() || currentSourceConfiguration == null)
            {
                return;
            }

            string sourcePath;
            using (Forms.FolderBrowserDialog dialog = new Forms.FolderBrowserDialog())
            {
                dialog.Description = "Choose a folder to include in future Restic backup runs.";
                dialog.RootFolder = Environment.SpecialFolder.MyComputer;
                dialog.ShowNewFolderButton = true;
                if (dialog.ShowDialog() != Forms.DialogResult.OK)
                {
                    return;
                }
                sourcePath = SourceConfiguration.NormalizePath(dialog.SelectedPath);
            }

            if (currentSourceConfiguration.ContainsUserSource(sourcePath))
            {
                SetSourceNotice("That folder is already backed up.", Amber, 8);
                return;
            }
            if (BackupBlocksSourceChanges())
            {
                SetSourceNotice("A backup is starting or already running. Try the folder change again afterward.", Amber, 10);
                return;
            }
            await ApplySourceChange("Add", sourcePath);
        }

        private async void OnRemoveSourceClick(object sender, RoutedEventArgs args)
        {
            if (sourceOperationInProgress || BackupBlocksSourceChanges() || currentSourceConfiguration == null)
            {
                return;
            }
            Button removeButton = sender as Button;
            BackupSourceView selected = removeButton == null
                ? null
                : removeButton.Tag as BackupSourceView;
            if (selected == null || selected.IsProtectedCanary)
            {
                SetSourceNotice("The protected restore canary cannot be removed.", Amber, 8);
                return;
            }

            if (!ConfirmSourceRemoval(selected))
            {
                return;
            }
            if (BackupBlocksSourceChanges())
            {
                SetSourceNotice("A backup is starting or already running. Try the folder change again afterward.", Amber, 10);
                return;
            }
            await ApplySourceChange("Remove", selected.SourcePath);
        }

        private bool ConfirmSourceRemoval(BackupSourceView source)
        {
            Window dialog = new Window();
            dialog.Title = "Remove folder from future backups?";
            dialog.Owner = this;
            dialog.Width = 530;
            dialog.SizeToContent = SizeToContent.Height;
            dialog.ResizeMode = ResizeMode.NoResize;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dialog.ShowInTaskbar = false;
            dialog.Background = BackgroundTop;
            dialog.Foreground = PrimaryText;
            dialog.FontFamily = FontFamily;

            Border frame = new Border();
            frame.Background = CardBrush;
            frame.BorderBrush = CardBorderBrush;
            frame.BorderThickness = new Thickness(1);
            frame.Padding = new Thickness(24, 22, 24, 20);

            StackPanel content = new StackPanel();
            TextBlock title = new TextBlock();
            title.Text = "Remove folder from future backups?";
            title.FontSize = 20;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = PrimaryText;
            content.Children.Add(title);

            TextBlock warning = new TextBlock();
            warning.Text = "Removing a folder stops future backups. Existing snapshots remain intact.";
            warning.FontSize = 13;
            warning.Foreground = BrushFrom("#B7C5D1");
            warning.TextWrapping = TextWrapping.Wrap;
            warning.Margin = new Thickness(0, 8, 0, 16);
            content.Children.Add(warning);

            Border folder = new Border();
            folder.Background = CardSoftBrush;
            folder.BorderBrush = CardBorderBrush;
            folder.BorderThickness = new Thickness(1);
            folder.CornerRadius = new CornerRadius(8);
            folder.Padding = new Thickness(13, 10, 13, 10);
            StackPanel folderDetails = new StackPanel();
            TextBlock folderName = new TextBlock();
            folderName.Text = source.DisplayName;
            folderName.FontSize = 14;
            folderName.FontWeight = FontWeights.SemiBold;
            folderName.Foreground = PrimaryText;
            folderDetails.Children.Add(folderName);
            TextBlock folderPath = new TextBlock();
            folderPath.Text = source.SourcePath;
            folderPath.FontSize = 12;
            folderPath.Foreground = MutedText;
            folderPath.TextWrapping = TextWrapping.Wrap;
            folderPath.Margin = new Thickness(0, 3, 0, 0);
            folderDetails.Children.Add(folderPath);
            folder.Child = folderDetails;
            content.Children.Add(folder);

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            actions.Margin = new Thickness(0, 19, 0, 0);

            Button keep = CreateButton("Keep folder");
            keep.MinHeight = 36;
            keep.IsCancel = true;
            keep.Click += delegate { dialog.DialogResult = false; };
            AutomationProperties.SetName(keep, "Keep folder");
            actions.Children.Add(keep);

            Button remove = CreateButton("Remove from future backups");
            remove.Margin = new Thickness(10, 0, 0, 0);
            remove.MinHeight = 36;
            remove.Background = Rose;
            remove.BorderBrush = BrushFrom("#FF9AAA");
            remove.Foreground = BrushFrom("#201016");
            remove.Click += delegate { dialog.DialogResult = true; };
            AutomationProperties.SetName(remove, "Remove from future backups");
            AutomationProperties.SetHelpText(remove, warning.Text);
            actions.Children.Add(remove);
            content.Children.Add(actions);

            frame.Child = content;
            dialog.Content = frame;
            AutomationProperties.SetName(dialog, "Remove folder from future backups");
            bool? result = dialog.ShowDialog();
            return result.HasValue && result.Value;
        }

        private async Task ApplySourceChange(string action, string sourcePath)
        {
            if (BackupBlocksSourceChanges())
            {
                SetSourceNotice("A backup is starting or already running. No folder change was requested.", Amber, 10);
                return;
            }
            bool isAdd = string.Equals(action, "Add", StringComparison.Ordinal);
            SourceConfiguration configuration = currentSourceConfiguration;
            int operationGeneration = sourceOperationGeneration + 1;
            sourceOperationInProgress = true;
            if (isAdd)
            {
                sourceOperationGeneration = operationGeneration;
                sourceOperationAction = action;
                sourceOperationPath = sourcePath;
                sourceOperationError = string.Empty;
                sourceOperationStage = SourceOperationStage.AwaitingApproval;
                sourceOperationExpiresUtc = DateTime.MaxValue;
                animateNextSourceOperationEntry = true;
                sourceNoticeExpiresUtc = DateTime.MinValue;
                sourceSignature = null;
                RefreshSources(true);
            }
            else
            {
                SetSourceNotice("Waiting for Windows approval...", BlueText, 600);
            }
            UpdateSourceButtons();

            SourceManagerResult result;
            try
            {
                result = await Task.Run(delegate
                {
                    return SourceManagerLauncher.Run(
                        configuration,
                        action,
                        sourcePath,
                        delegate
                        {
                            if (!isAdd)
                            {
                                return;
                            }
                            Dispatcher.BeginInvoke(
                                new Action(delegate
                                {
                                    if (sourceOperationGeneration == operationGeneration &&
                                        sourceOperationInProgress &&
                                        sourceOperationStage == SourceOperationStage.AwaitingApproval)
                                    {
                                        SetSourceOperationStage(
                                            SourceOperationStage.Applying,
                                            string.Empty,
                                            DateTime.MaxValue,
                                            false,
                                            true);
                                    }
                                }),
                                DispatcherPriority.Background);
                        });
                });
            }
            catch (Exception error)
            {
                result = SourceManagerResult.Failure(error.Message, -1);
            }
            finally
            {
                if (!isAdd || sourceOperationGeneration == operationGeneration)
                {
                    sourceOperationInProgress = false;
                }
            }

            if (result.UserCancelled)
            {
                if (isAdd && sourceOperationGeneration == operationGeneration)
                {
                    SetSourceOperationStage(
                        SourceOperationStage.Cancelled,
                        string.Empty,
                        DateTime.UtcNow.AddSeconds(3),
                        false,
                        true);
                }
                else
                {
                    SetSourceNotice("Windows approval was cancelled. No folders were changed.", Amber, 12);
                    MessageBox.Show(
                        this,
                        "Windows approval was cancelled. The backup folder list was not changed.",
                        "Folder change cancelled",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                RefreshSources(false);
                return;
            }
            if (!result.Succeeded)
            {
                string detail = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "The protected source manager could not complete the request."
                    : result.ErrorMessage;
                if (isAdd && sourceOperationGeneration == operationGeneration)
                {
                    SetSourceOperationStage(
                        SourceOperationStage.Failed,
                        detail,
                        DateTime.MaxValue,
                        false,
                        true);
                }
                else
                {
                    SetSourceNotice("Folder change failed. The protected configuration was not updated.", Rose, 15);
                    MessageBox.Show(
                        this,
                        "The folder list was not changed.\n\n" + detail,
                        "Folder change failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                RefreshSources(false);
                return;
            }

            if (isAdd && sourceOperationGeneration == operationGeneration)
            {
                SetSourceOperationStage(
                    SourceOperationStage.Verifying,
                    string.Empty,
                    DateTime.MaxValue,
                    false,
                    true);
                await Dispatcher.Yield(DispatcherPriority.Render);
            }
            RefreshSources(true);
            bool isPresent = currentSourceConfiguration != null &&
                currentSourceConfiguration.ContainsUserSource(sourcePath);
            bool planConfirmed = currentSourceConfiguration != null &&
                string.Equals(result.PlanId, configuration.PlanId, StringComparison.Ordinal) &&
                string.Equals(currentSourceConfiguration.PlanId, configuration.PlanId, StringComparison.Ordinal) &&
                result.PreviousConfigGeneration == configuration.ConfigGeneration &&
                configuration.ConfigGeneration < long.MaxValue &&
                result.ConfigGeneration == configuration.ConfigGeneration + 1 &&
                currentSourceConfiguration.ConfigGeneration == result.ConfigGeneration;
            bool confirmed = planConfirmed &&
                (isAdd
                    ? isPresent
                    : !isPresent);
            if (!confirmed)
            {
                string detail = "The manager finished, but the protected configuration did not confirm the change.";
                if (isAdd && sourceOperationGeneration == operationGeneration)
                {
                    SetSourceOperationStage(
                        SourceOperationStage.Failed,
                        detail,
                        DateTime.MaxValue,
                        true,
                        true);
                }
                else
                {
                    SetSourceNotice(detail, Rose, 15);
                    MessageBox.Show(
                        this,
                        "The protected configuration did not confirm the requested folder change. " +
                            "Refresh the dashboard and check the installation logs.",
                        "Folder change not confirmed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                return;
            }

            if (isAdd && sourceOperationGeneration == operationGeneration)
            {
                sourceOperationStage = SourceOperationStage.Succeeded;
                sourceOperationExpiresUtc = DateTime.UtcNow.AddSeconds(4);
                animateNextResolvedSourceRow = true;
                sourceSignature = null;
                RefreshSources(true);
                if (!IsVisible)
                {
                    ShowTrayMessage("Backup folder added", sourcePath, Forms.ToolTipIcon.Info);
                }
            }
            else
            {
                SetSourceNotice("Folder removed from future backups. Existing snapshots were not deleted.", Green, 12);
                ShowTrayMessage("Backup folder removed", sourcePath, Forms.ToolTipIcon.Info);
            }
            UpdateSourceButtons();
        }

        private void SetSourceNotice(string message, Brush color, int seconds)
        {
            sourceStatus.Text = message;
            sourceStatus.Foreground = color;
            sourceNoticeExpiresUtc = DateTime.UtcNow.AddSeconds(seconds);
            AutomationProperties.SetName(sourceStatus, message);
            AutomationProperties.SetLiveSetting(
                sourceStatus,
                color == Rose ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
            Dispatcher.BeginInvoke(
                new Action(delegate
                {
                    AutomationPeer peer = UIElementAutomationPeer.FromElement(sourceStatus) ??
                        UIElementAutomationPeer.CreatePeerForElement(sourceStatus);
                    if (peer != null)
                    {
                        peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                    }
                }),
                DispatcherPriority.Background);
        }

        private void ApplySnapshot(TelemetrySnapshot snapshot)
        {
            ObserveCancellationState(snapshot);
            heroTitle.Text = snapshot.StatusLabel;
            heroDetail.Text = snapshot.StatusDetail;
            bool showLiveProgress = snapshot.IsActive || snapshot.IsFailure;
            activeProgressPanel.Visibility = showLiveProgress ? Visibility.Visible : Visibility.Collapsed;
            bool showRunMetrics = snapshot.IsActive;
            metricsLayout.Visibility = showRunMetrics ? Visibility.Visible : Visibility.Collapsed;
            metricsContext.Visibility = showRunMetrics ? Visibility.Visible : Visibility.Collapsed;
            bool firstBackupPending = string.Equals(
                snapshot.StateKey,
                "ready",
                StringComparison.OrdinalIgnoreCase);
            progressPercent.Text = firstBackupPending
                ? "First backup pending"
                : (snapshot.Percent * 100).ToString("0.0", CultureInfo.CurrentCulture) + "%";
            estimateBadge.Text = firstBackupPending
                ? "DRY-RUN BASELINE  •  NOT YET BACKED UP"
                : string.Equals(snapshot.StateKey, "cancelling", StringComparison.OrdinalIgnoreCase)
                    ? "CANCELLATION REQUESTED  •  COOPERATIVE STOP"
                : snapshot.IsSuccess
                    ? "FINAL VERIFIED RESULT"
                : snapshot.ProgressIsEstimated
                    ? "ESTIMATED  •  " + snapshot.ConfidenceLabel.ToUpperInvariant()
                    : "RESTIC COUNTERS  •  COMPOSED OVERALL";
            estimateBadge.Foreground = firstBackupPending
                ? Amber
                : snapshot.ProgressIsEstimated ? Teal : BlueText;
            AnimateProgress(snapshot.Percent, true);

            if (string.Equals(snapshot.StateKey, "cancelling", StringComparison.OrdinalIgnoreCase))
            {
                etaTitle.Text = "Stopping safely";
                etaValue.Text = "Please wait";
                etaHint.Text = "Restic is finishing its current operation and releasing the repository lock";
            }
            else
            {
                etaTitle.Text = snapshot.ProgressIsEstimated
                    ? "Estimated time remaining"
                    : "Time remaining";
                if (snapshot.Eta.HasValue)
                {
                    etaValue.Text = FormatDuration(snapshot.Eta.Value);
                    string completion = snapshot.EstimatedCompletion.HasValue
                        ? "Likely around " + snapshot.EstimatedCompletion.Value.ToString("HH:mm", CultureInfo.CurrentCulture)
                        : "Updates as Restic advances";
                    etaHint.Text = snapshot.ProgressIsEstimated
                        ? snapshot.ConfidenceLabel + "  •  " + completion
                        : completion;
                }
                else if (snapshot.IsSuccess)
                {
                    if (snapshot.AnomalyReviewAcknowledged)
                    {
                        heroTitle.Text = "Backup verified — changes acknowledged";
                        etaTitle.Text = "Review status";
                        etaValue.Text = "Acknowledged";
                        etaHint.Text = "This exact generation was reviewed; the deletion and retention hold remains";
                    }
                    else
                    {
                    heroTitle.Text = snapshot.NeedsAnomalyReview
                        ? "Backup verified — review changes"
                        : "Backup verified";
                    etaTitle.Text = snapshot.NeedsAnomalyReview
                        ? "Safety hold"
                        : "Next automatic run";
                    etaValue.Text = snapshot.NeedsAnomalyReview
                        ? "Review required"
                        : currentTaskSchedule == null
                            ? "Unavailable"
                            : currentTaskSchedule.NextRunDisplay;
                    etaHint.Text = snapshot.NeedsAnomalyReview
                        ? "The snapshot is safe; review is required for future deletion or retention actions. DriveFS upload is not paused"
                        : currentTaskSchedule == null
                            ? "Installed schedule could not be verified"
                            : "Latest snapshot and restore canary verified  •  " + currentTaskSchedule.Summary;
                }
                    }
                else if (snapshot.IsFailure)
                {
                    etaTitle.Text = "Backup result";
                    etaValue.Text = "Stopped";
                    etaHint.Text = "The incomplete run was recorded in Activity";
                }
                else if (snapshot.IsActive)
                {
                    etaValue.Text = "Calculating";
                    etaHint.Text = "Building an estimate from live progress and run history";
                }
                else
                {
                    etaTitle.Text = "Next automatic run";
                    etaValue.Text = currentTaskSchedule == null
                        ? "Unavailable"
                        : currentTaskSchedule.NextRunDisplay;
                    etaHint.Text = currentTaskSchedule == null
                        ? "Installed schedule could not be verified"
                        : currentTaskSchedule.SettingsSummary;
                }
            }

            bool hasActualFileCount = snapshot.FilesDone > 0 || snapshot.IsSuccess;
            bool hasActualByteCount = snapshot.BytesDone > 0 || snapshot.IsSuccess;
            filesValue.Text = hasActualFileCount ? FormatNumber(Math.Max(0, snapshot.FilesDone)) : "—";
            bytesValue.Text = hasActualByteCount ? FormatBytes(Math.Max(0, snapshot.BytesDone)) : "—";
            speedValue.Text = snapshot.TransferRateBytesPerSecond > 0
                ? FormatBytes(snapshot.TransferRateBytesPerSecond) + "/s"
                : "—";
            elapsedValue.Text = snapshot.Elapsed > TimeSpan.Zero ? FormatDuration(snapshot.Elapsed) : "—";
            errorsValue.Text = snapshot.ErrorCount.ToString("N0", CultureInfo.CurrentCulture);
            errorsValue.Foreground = snapshot.ErrorCount > 0 ? Rose : PrimaryText;
            if (metricsContext != null)
            {
                metricsContext.Text = snapshot.IsActive
                    ? "Current run"
                    : snapshot.IsSuccess
                        ? "Latest verified run"
                        : "Latest recorded run";
            }

            SetPhase(snapshot.PhaseIndex, snapshot.IsFailure);
            if (string.Equals(snapshot.StateKey, "cancelling", StringComparison.OrdinalIgnoreCase))
            {
                SetBadge("STOPPING SAFELY", Amber, BrushFrom("#3A2F18"));
                StartStatusPulse();
            }
            else if (snapshot.IsActive)
            {
                SetBadge("LIVE  •  " + snapshot.PhaseLabel.ToUpperInvariant(), Teal, BrushFrom("#12352F"));
                StartStatusPulse();
            }
            else if (snapshot.IsFailure)
            {
                SetBadge("NEEDS ATTENTION", Rose, BrushFrom("#3B1D2A"));
                StopStatusPulse();
            }
            else if (snapshot.NeedsAnomalyReview)
            {
                SetBadge("REVIEW CHANGES", Amber, BrushFrom("#3A2F18"));
                statusBadge.Visibility = Visibility.Visible;
                StopStatusPulse();
            }
            else if (snapshot.AnomalyReviewAcknowledged)
            {
                SetBadge("REVIEW ACKNOWLEDGED", Green, BrushFrom("#14352C"));
                statusBadge.Visibility = Visibility.Visible;
                StopStatusPulse();
            }
            else if (snapshot.IsSuccess)
            {
                SetBadge("VERIFIED", Green, BrushFrom("#14352C"));
                statusBadge.Visibility = Visibility.Visible;
                StopStatusPulse();
            }
            else if (snapshot.IsCancelled)
            {
                SetBadge("CANCELED SAFELY", Blue, BrushFrom("#172B45"));
                statusBadge.Visibility = Visibility.Visible;
                StopStatusPulse();
            }
            else
            {
                SetBadge("READY", Blue, BrushFrom("#172B45"));
                statusBadge.Visibility = Visibility.Visible;
                StopStatusPulse();
            }

            if (snapshot.IsActive || snapshot.IsFailure)
            {
                statusBadge.Visibility = Visibility.Visible;
            }

            ApplyFreshnessStatus(snapshot);
            ApplyOffsiteStatus(snapshot);
            ApplyRepositoryRecoveryPrimaryStatus(snapshot);

            IList<RunMetricView> history = snapshot.History ?? new List<RunMetricView>();
            RunMetricView selectedRun = historyGrid.SelectedItem as RunMetricView;
            string selectedRunId = selectedRun == null ? string.Empty : selectedRun.RunId;
            IList<RunMetricView> orderedHistory = history
                .OrderByDescending(item => item.StartedLocal)
                .ToList();
            historyGrid.ItemsSource = orderedHistory;
            if (!string.IsNullOrWhiteSpace(selectedRunId))
            {
                historyGrid.SelectedItem = orderedHistory.FirstOrDefault(item =>
                    string.Equals(
                        item.RunId,
                        selectedRunId,
                        StringComparison.OrdinalIgnoreCase));
            }
            runChart.Runs = history.OrderBy(item => item.StartedLocal).ToList();
            runCount.Text = history.Count.ToString(CultureInfo.InvariantCulture) + (history.Count == 1 ? " run" : " runs");
            lastUpdated.Text = "Updated " + snapshot.LastUpdatedLocal.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
            UpdateBackupButton(snapshot);
            UpdateActivityActions();
            WriteHeartbeat(snapshot, true);
        }

        private void ApplyOffsiteStatus(TelemetrySnapshot snapshot)
        {
            if (settingsOffsiteStatus == null
                || settingsOffsiteDetail == null
                || settingsOffsiteEvidence == null)
            {
                return;
            }

            OffsiteStatusView status = snapshot == null
                ? null
                : snapshot.OffsiteStatus;
            if (status == null)
            {
                status = new OffsiteStatusView();
            }

            settingsOffsiteStatus.Text = status.StatusLabel;
            settingsOffsiteDetail.Text = status.StatusDetail;
            if (status.Kind == OffsiteStatusKind.Failed)
            {
                settingsOffsiteStatus.Foreground = Rose;
            }
            else if (status.Kind == OffsiteStatusKind.StatusUnavailable)
            {
                settingsOffsiteStatus.Foreground = Amber;
            }
            else if (status.Kind == OffsiteStatusKind.InProgress)
            {
                settingsOffsiteStatus.Foreground = BlueText;
            }
            else if (status.Kind == OffsiteStatusKind.LocalVerifiedProviderPending)
            {
                settingsOffsiteStatus.Foreground = Amber;
            }
            else if (status.Kind == OffsiteStatusKind.ProviderConfirmed)
            {
                settingsOffsiteStatus.Foreground = Teal;
            }
            else if (status.Kind == OffsiteStatusKind.RestoreVerified)
            {
                settingsOffsiteStatus.Foreground = Green;
            }
            else
            {
                settingsOffsiteStatus.Foreground = MutedText;
            }

            List<string> evidence = new List<string>();
            if (!string.IsNullOrEmpty(status.SnapshotShort))
            {
                evidence.Add("Snapshot " + status.SnapshotShort);
            }
            if (status.FileCount >= 0)
            {
                evidence.Add(FormatNumber(status.FileCount) + " files");
            }
            if (status.ByteCount >= 0)
            {
                evidence.Add(FormatBytes(status.ByteCount));
            }
            if (status.LastUpdatedLocal.HasValue)
            {
                evidence.Add(
                    "Updated "
                    + status.LastUpdatedLocal.Value.ToString(
                        "yyyy-MM-dd HH:mm",
                        CultureInfo.CurrentCulture));
            }
            settingsOffsiteEvidence.Text = evidence.Count == 0
                ? "No verified direct-cloud inventory or restore evidence is available yet."
                : string.Join("  \u2022  ", evidence);

            AutomationProperties.SetName(
                settingsOffsiteStatus,
                "Google Drive verification status: " + status.StatusLabel);
            AutomationProperties.SetName(
                settingsOffsiteDetail,
                "Google Drive verification detail: " + status.StatusDetail);
            AutomationProperties.SetName(
                settingsOffsiteEvidence,
                "Google Drive verification evidence: " + settingsOffsiteEvidence.Text);
        }

        private void ApplyRepositoryRecoveryPrimaryStatus(TelemetrySnapshot snapshot)
        {
            if (!RepositoryRecoveryBlocksMutations() ||
                (snapshot != null && snapshot.IsActive) ||
                CancellationBlocksMutations(snapshot))
            {
                return;
            }

            string detail = repositoryRecoveryStatus == null
                ? "Protected recovery status is unavailable."
                : repositoryRecoveryStatus.Message;
            if (repositoryRecoveryInProgress)
            {
                heroTitle.Text = "Repairing interrupted repository move";
                heroDetail.Text = string.IsNullOrWhiteSpace(repositoryRecoveryOperationMessage)
                    ? "The protected recovery helper is validating repository state."
                    : repositoryRecoveryOperationMessage;
                etaTitle.Text = "Backup safety gate";
                etaValue.Text = "Repairing";
                etaHint.Text = "No backup can start until protected recovery completes and the journal disappears.";
                SetBadge("REPAIR IN PROGRESS", Blue, BrushFrom("#172B45"));
            }
            else
            {
                heroTitle.Text = "Repository repair required";
                heroDetail.Text = detail;
                etaTitle.Text = "Backup safety gate";
                etaValue.Text = "Paused";
                etaHint.Text = "Use Repair interrupted move. There is no dismiss or manual bypass.";
                SetBadge("BACKUPS PAUSED", Rose, BrushFrom("#3B1D2A"));
            }
            statusBadge.Visibility = Visibility.Visible;
            StopStatusPulse();
        }

        private void ApplyFreshnessStatus(TelemetrySnapshot snapshot)
        {
            BackupFreshnessResult freshness = BackupFreshnessEvaluator.Evaluate(
                currentTaskSchedule,
                snapshot.LastVerifiedFinishedUtc,
                DateTime.Now,
                DateTime.UtcNow);
            Brush tone = freshness.State == BackupFreshnessState.Healthy
                ? Green
                : freshness.State == BackupFreshnessState.Paused
                    ? Amber
                    : freshness.NeedsAttention
                        ? Rose
                        : MutedText;
            string shortStatus = "Backup freshness: " + freshness.StatusLabel;
            string accessibleStatus = shortStatus + ". " + freshness.Detail;

            if (protectionFreshnessStatus != null)
            {
                protectionFreshnessStatus.Text = shortStatus + "  \u2022  " + freshness.Detail;
                protectionFreshnessStatus.Foreground = tone;
                protectionFreshnessStatus.ToolTip = freshness.Detail;
                AutomationProperties.SetName(protectionFreshnessStatus, accessibleStatus);
                AutomationProperties.SetHelpText(protectionFreshnessStatus, freshness.Detail);
                AutomationProperties.SetLiveSetting(
                    protectionFreshnessStatus,
                    freshness.NeedsAttention
                        ? AutomationLiveSetting.Assertive
                        : AutomationLiveSetting.Polite);
            }
            if (settingsFreshnessStatus != null)
            {
                settingsFreshnessStatus.Text = freshness.StatusLabel;
                settingsFreshnessStatus.Foreground = tone;
                AutomationProperties.SetName(settingsFreshnessStatus, shortStatus);
                AutomationProperties.SetHelpText(settingsFreshnessStatus, freshness.Detail);
                AutomationProperties.SetLiveSetting(
                    settingsFreshnessStatus,
                    freshness.NeedsAttention
                        ? AutomationLiveSetting.Assertive
                        : AutomationLiveSetting.Polite);
            }
            if (settingsFreshnessDetail != null)
            {
                settingsFreshnessDetail.Text = freshness.Detail;
                AutomationProperties.SetName(settingsFreshnessDetail, freshness.Detail);
                AutomationProperties.SetHelpText(settingsFreshnessDetail, freshness.Detail);
            }

            if (!lastAnnouncedFreshnessState.HasValue ||
                lastAnnouncedFreshnessState.Value != freshness.State)
            {
                lastAnnouncedFreshnessState = freshness.State;
                FrameworkElement announcementTarget = settingsFreshnessStatus as FrameworkElement
                    ?? protectionFreshnessStatus as FrameworkElement;
                if (announcementTarget != null)
                {
                    AutomationPeer peer = UIElementAutomationPeer.FromElement(announcementTarget) ??
                        UIElementAutomationPeer.CreatePeerForElement(announcementTarget);
                    if (peer != null)
                    {
                        peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                    }
                }
            }

            bool activeRunOwnsPrimaryStatus = snapshot.IsActive || string.Equals(
                snapshot.StateKey,
                "cancelling",
                StringComparison.OrdinalIgnoreCase);
            if (activeRunOwnsPrimaryStatus)
            {
                return;
            }

            if (freshness.State == BackupFreshnessState.Overdue ||
                freshness.State == BackupFreshnessState.ClockAnomaly)
            {
                SetBadge("NEEDS ATTENTION", Rose, BrushFrom("#3B1D2A"));
                statusBadge.Visibility = Visibility.Visible;
                StopStatusPulse();
                if (!snapshot.IsFailure)
                {
                    heroTitle.Text = freshness.State == BackupFreshnessState.Overdue
                        ? "Backup overdue"
                        : "Backup clock needs attention";
                    heroDetail.Text = freshness.Detail;
                    etaTitle.Text = "Backup freshness";
                    etaValue.Text = freshness.StatusLabel;
                    etaHint.Text = currentTaskSchedule == null
                        ? freshness.Detail
                        : "Next automatic run: " + currentTaskSchedule.NextRunDisplay;
                }
            }
            else if (freshness.State == BackupFreshnessState.Paused && !snapshot.IsFailure)
            {
                heroTitle.Text = "Automatic backups paused";
                heroDetail.Text = freshness.Detail;
                etaTitle.Text = "Automatic schedule";
                etaValue.Text = "Paused";
                etaHint.Text = currentTaskSchedule == null
                    ? "Schedule unavailable"
                    : currentTaskSchedule.Summary;
                SetBadge("PAUSED", Amber, BrushFrom("#3A2F18"));
                statusBadge.Visibility = Visibility.Visible;
                StopStatusPulse();
            }
            else if (freshness.State == BackupFreshnessState.NoVerifiedBackup &&
                !snapshot.IsFailure)
            {
                heroTitle.Text = "First verified backup needed";
                heroDetail.Text = freshness.Detail;
                SetBadge("FIRST BACKUP NEEDED", Amber, BrushFrom("#3A2F18"));
                statusBadge.Visibility = Visibility.Visible;
                StopStatusPulse();
            }
        }

        private void ApplyPreview(TelemetrySnapshot snapshot)
        {
            activeProgressPanel.Visibility = Visibility.Visible;
            metricsLayout.Visibility = Visibility.Visible;
            metricsContext.Visibility = Visibility.Visible;
            metricsContext.Text = "Previewed live run";
            statusBadge.Visibility = Visibility.Visible;
            double seconds = (DateTime.Now - previewStarted).TotalSeconds;
            double cycle = seconds % 42.0;
            double fraction = Math.Min(0.985, 0.035 + cycle / 44.0);
            long totalFiles = Math.Max(1, snapshot.EstimatedFiles);
            long totalBytes = Math.Max(1, snapshot.EstimatedBytes);
            long files = (long)(totalFiles * fraction);
            long bytes = (long)(totalBytes * Math.Min(1, fraction * 1.03));
            TimeSpan elapsed = TimeSpan.FromSeconds(cycle * 52);
            TimeSpan eta = TimeSpan.FromSeconds(Math.Max(20, elapsed.TotalSeconds * (1 - fraction) / Math.Max(0.02, fraction)));

            heroTitle.Text = "Previewing live backup telemetry";
            heroDetail.Text = "Preview how backup progress moves through each stage. No backup is running.";
            progressPercent.Text = (fraction * 100).ToString("0.0", CultureInfo.CurrentCulture) + "%";
            estimateBadge.Text = "PREVIEW  •  ESTIMATED FROM VALIDATION";
            estimateBadge.Foreground = Amber;
            AnimateProgress(fraction, true);
            etaTitle.Text = "Time remaining";
            etaValue.Text = FormatDuration(eta);
            etaHint.Text = "Preview completion around " + DateTime.Now.Add(eta).ToString("HH:mm", CultureInfo.CurrentCulture);
            filesValue.Text = FormatNumber(files);
            bytesValue.Text = FormatBytes(bytes);
            speedValue.Text = FormatBytes(bytes / Math.Max(1, elapsed.TotalSeconds)) + "/s";
            elapsedValue.Text = FormatDuration(elapsed);
            errorsValue.Text = "0";
            errorsValue.Foreground = PrimaryText;
            SetPhase(fraction < 0.9 ? 0 : fraction < 0.94 ? 1 : fraction < 0.97 ? 2 : 3, false);
            SetBadge("PREVIEW ANIMATION", Amber, BrushFrom("#3A2F18"));
            StartStatusPulse();
            UpdateBackupButton(snapshot);
        }

        private void HandleStateTransition(TelemetrySnapshot snapshot)
        {
            if (lastStateKey == null)
            {
                lastStateKey = snapshot.StateKey;
                return;
            }
            if (string.Equals(lastStateKey, snapshot.StateKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (snapshot.IsActive)
            {
                ShowTrayMessage("Backup started", "Restic is now protecting your files.", Forms.ToolTipIcon.Info);
            }
            else if (snapshot.NeedsAnomalyReview)
            {
                ShowTrayMessage("Backup verified — review changes", snapshot.StatusDetail, Forms.ToolTipIcon.Warning);
            }
            else if (snapshot.AnomalyReviewAcknowledged)
            {
                ShowTrayMessage(
                    "Changes acknowledged",
                    "This exact generation was reviewed; deletion and retention remain on hold. DriveFS upload is not paused.",
                    Forms.ToolTipIcon.Info);
            }
            else if (snapshot.IsSuccess)
            {
                ShowTrayMessage("Backup verified", "Snapshot, repository check, and restore canary completed.", Forms.ToolTipIcon.Info);
            }
            else if (snapshot.IsFailure)
            {
                ShowTrayMessage("Backup needs attention", snapshot.StatusDetail, Forms.ToolTipIcon.Error);
            }
            lastStateKey = snapshot.StateKey;
        }

        private void AnimateProgress(double fraction, bool animate)
        {
            currentProgress = Math.Max(0, Math.Min(1, fraction));
            if (progressTrack == null || progressFill == null || progressTrack.ActualWidth <= 0)
            {
                return;
            }
            double target = progressTrack.ActualWidth * currentProgress;
            if (!animate || !SystemParameters.ClientAreaAnimation)
            {
                progressFill.BeginAnimation(FrameworkElement.WidthProperty, null);
                progressFill.Width = target;
                return;
            }
            DoubleAnimation animation = new DoubleAnimation();
            animation.To = target;
            animation.Duration = TimeSpan.FromMilliseconds(520);
            animation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            progressFill.BeginAnimation(FrameworkElement.WidthProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private void StartShimmer()
        {
            if (themeResolution.IsHighContrast ||
                !SystemParameters.ClientAreaAnimation ||
                shimmerTransform == null)
            {
                return;
            }
            DoubleAnimation animation = new DoubleAnimation();
            animation.From = -190;
            animation.To = 1250;
            animation.Duration = TimeSpan.FromSeconds(2.7);
            animation.RepeatBehavior = RepeatBehavior.Forever;
            shimmerTransform.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        private void StopShimmer()
        {
            if (shimmerTransform != null)
            {
                shimmerTransform.BeginAnimation(TranslateTransform.XProperty, null);
            }
        }

        private void StartStatusPulse()
        {
            if (themeResolution.IsHighContrast ||
                !SystemParameters.ClientAreaAnimation ||
                statusDot == null)
            {
                return;
            }
            DoubleAnimation pulse = new DoubleAnimation();
            pulse.From = 1;
            pulse.To = 0.32;
            pulse.AutoReverse = true;
            pulse.Duration = TimeSpan.FromMilliseconds(720);
            pulse.RepeatBehavior = RepeatBehavior.Forever;
            statusDot.BeginAnimation(UIElement.OpacityProperty, pulse, HandoffBehavior.SnapshotAndReplace);
        }

        private void StopStatusPulse()
        {
            if (statusDot != null)
            {
                statusDot.BeginAnimation(UIElement.OpacityProperty, null);
                statusDot.Opacity = 1;
            }
        }

        private void SetPhase(int activeIndex, bool failed)
        {
            for (int index = 0; index < phaseMarkers.Count; index++)
            {
                bool complete = index < activeIndex;
                bool active = index == activeIndex;
                phaseMarkers[index].Background = complete
                    ? Green
                    : active ? (failed ? Rose : Teal) : BrushFrom("#25334A");
                TextBlock number = phaseMarkers[index].Child as TextBlock;
                if (number != null)
                {
                    number.Foreground = active && failed
                        ? PrimaryText
                        : (complete || active) ? BrushFrom("#07131E") : MutedText;
                }
                phaseLabels[index].Foreground = active
                    ? failed ? Rose : PrimaryText
                    : complete ? Green : MutedText;
                phaseLabels[index].FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        private void SetBadge(string text, Brush dot, Brush background)
        {
            statusBadgeText.Text = text;
            statusDot.Fill = dot;
            statusBadge.Background = background;
        }

        private Forms.NotifyIcon BuildTrayIcon()
        {
            Forms.NotifyIcon icon = new Forms.NotifyIcon();
            try
            {
                trayApplicationIcon = Drawing.Icon.ExtractAssociatedIcon(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
            }
            catch (Exception)
            {
                trayApplicationIcon = null;
            }
            icon.Icon = trayApplicationIcon ?? Drawing.SystemIcons.Shield;
            icon.Text = "Rewindle";
            icon.Visible = true;
            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open dashboard", null, delegate { Dispatcher.BeginInvoke(new Action(ShowDashboard)); });
            menu.Items.Add("Open metrics folder", null, delegate { OpenMetricsFolder(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit monitor", null, delegate { Dispatcher.BeginInvoke(new Action(ExitApplication)); });
            icon.ContextMenuStrip = menu;
            icon.DoubleClick += delegate { Dispatcher.BeginInvoke(new Action(ShowDashboard)); };
            return icon;
        }

        private void ShowTrayMessage(string title, string message, Forms.ToolTipIcon icon)
        {
            trayIcon.BalloonTipTitle = title;
            trayIcon.BalloonTipText = message.Length > 220 ? message.Substring(0, 220) : message;
            trayIcon.BalloonTipIcon = icon;
            trayIcon.ShowBalloonTip(4500);
        }

        private void OpenMetricsFolder()
        {
            string directory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ResticBackuperDashboard");
            Directory.CreateDirectory(directory);
            Process.Start("explorer.exe", directory);
        }

        private void ShowDashboard()
        {
            ShowInTaskbar = true;
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        private void HideToTray()
        {
            ShowInTaskbar = false;
            Hide();
        }

        private void ExitApplication()
        {
            if (cancellationRequestInProgress || cancellationAwaitingTerminal)
            {
                MessageBoxResult choice = MessageBox.Show(
                    this,
                    "Cancellation is still in progress. Closing the dashboard will not stop or reverse it; " +
                        "the protected backup task will continue handling the request.\n\nExit the dashboard monitor?",
                    "Cancellation still in progress",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information,
                    MessageBoxResult.No);
                if (choice != MessageBoxResult.Yes)
                {
                    return;
                }
            }
            allowClose = true;
            Close();
            Application.Current.Shutdown();
        }

        private void OnClosing(object sender, CancelEventArgs args)
        {
            if (!allowClose)
            {
                args.Cancel = true;
                HideToTray();
            }
        }

        private void OnClosed(object sender, EventArgs args)
        {
            DisposeWebPresentation();
            WriteHeartbeat(lastSnapshot, false);
            refreshTimer.Stop();
            StopShimmer();
            StopStatusPulse();
            StopSourceOperationAnimation();
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
            trayIcon.Visible = false;
            trayIcon.Dispose();
            if (trayApplicationIcon != null)
            {
                trayApplicationIcon.Dispose();
                trayApplicationIcon = null;
            }
        }

        private Border CreateCard()
        {
            Border border = new Border();
            border.Background = CardBrush;
            border.BorderBrush = CardBorderBrush;
            border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(10);
            return border;
        }

        private void WriteHeartbeat(TelemetrySnapshot snapshot, bool running)
        {
            if (options != null && options.UseIsolatedPresentationStore)
            {
                return;
            }
            DateTime now = DateTime.UtcNow;
            if (running && lastHeartbeatUtc != DateTime.MinValue && now - lastHeartbeatUtc < TimeSpan.FromSeconds(15))
            {
                return;
            }
            try
            {
                string directory = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ResticBackuperDashboard");
                Directory.CreateDirectory(directory);
                string path = System.IO.Path.Combine(directory, "heartbeat.json");
                string temporary = System.IO.Path.Combine(
                    directory,
                    ".heartbeat." + Guid.NewGuid().ToString("N") + ".tmp");
                string backup = System.IO.Path.Combine(
                    directory,
                    ".heartbeat." + Guid.NewGuid().ToString("N") + ".bak");
                try
                {
                    Dictionary<string, object> value = new Dictionary<string, object>();
                    value["schema_version"] = 1;
                    value["updated_utc"] = now.ToString("o", CultureInfo.InvariantCulture);
                    value["running"] = running;
                    value["pid"] = Process.GetCurrentProcess().Id;
                    value["executable"] = Process.GetCurrentProcess().MainModule.FileName;
                    value["version"] = GetType().Assembly.GetName().Version.ToString();
                    value["elevated"] = false;
                    value["state"] = snapshot == null ? "closing" : snapshot.StateKey;
                    value["run_id"] = snapshot == null ? null : snapshot.RunId;
                    File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(value));
                    if (File.Exists(path))
                    {
                        File.Replace(temporary, path, backup, true);
                        File.Delete(backup);
                    }
                    else
                    {
                        File.Move(temporary, path);
                    }
                    lastHeartbeatUtc = now;
                }
                finally
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                    if (File.Exists(backup))
                    {
                        File.Delete(backup);
                    }
                }
            }
            catch
            {
                // The monitor remains useful even if its presentation cache is unavailable.
            }
        }

        private Button CreateButton(string label)
        {
            Button button = new Button();
            button.Content = label;
            button.Foreground = themeResolution.Palette.ButtonText;
            button.Background = themeResolution.Palette.ButtonBackground;
            button.BorderBrush = CardBorderBrush;
            button.BorderThickness = new Thickness(1);
            button.Padding = new Thickness(13, 7, 13, 7);
            button.FontSize = 12.5;
            button.FontWeight = FontWeights.Medium;
            button.MinHeight = 34;
            button.Cursor = Cursors.Hand;
            ToolTipService.SetShowOnDisabled(button, true);
            ApplyButtonChrome(button);
            AutomationProperties.SetName(button, label);
            Brush borderBeforeFocus = null;
            Thickness thicknessBeforeFocus = new Thickness(1);
            button.GotKeyboardFocus += delegate
            {
                borderBeforeFocus = button.BorderBrush;
                thicknessBeforeFocus = button.BorderThickness;
                button.BorderBrush = themeResolution.Palette.Focus;
                button.BorderThickness = new Thickness(2);
            };
            button.LostKeyboardFocus += delegate
            {
                if (borderBeforeFocus != null)
                {
                    button.BorderBrush = borderBeforeFocus;
                    button.BorderThickness = thicknessBeforeFocus;
                }
            };
            return button;
        }

        private static void ApplyButtonChrome(Button button)
        {
            DashboardVisualStyle.ApplyButtonChrome(button, 8);
        }

        private static string FormatNumber(long value)
        {
            return value.ToString("N0", CultureInfo.CurrentCulture);
        }

        private static string FormatBytes(double value)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double amount = Math.Max(0, value);
            int index = 0;
            while (amount >= 1024 && index < units.Length - 1)
            {
                amount /= 1024;
                index++;
            }
            string format = index == 0 ? "0" : amount >= 100 ? "0" : amount >= 10 ? "0.0" : "0.00";
            return amount.ToString(format, CultureInfo.CurrentCulture) + " " + units[index];
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
            {
                return ((int)duration.TotalHours).ToString(CultureInfo.CurrentCulture) + "h " + duration.Minutes.ToString("00", CultureInfo.CurrentCulture) + "m";
            }
            if (duration.TotalMinutes >= 1)
            {
                return ((int)duration.TotalMinutes).ToString(CultureInfo.CurrentCulture) + "m " + duration.Seconds.ToString("00", CultureInfo.CurrentCulture) + "s";
            }
            return Math.Max(0, (int)duration.TotalSeconds).ToString(CultureInfo.CurrentCulture) + "s";
        }

        private Brush BrushFrom(string value)
        {
            return themeResolution.Palette.BrushForLegacy(value);
        }
    }

    internal static class ElementStyleHolder
    {
        public static void Apply(DataGridTextColumn column)
        {
            Style style = new Style(typeof(TextBlock));
            Binding foreground = new Binding("Foreground");
            foreground.RelativeSource = new RelativeSource(
                RelativeSourceMode.FindAncestor,
                typeof(DataGridRow),
                1);
            style.Setters.Add(new Setter(TextBlock.ForegroundProperty, foreground));
            style.Setters.Add(new Setter(TextBlock.FontSizeProperty, 12.0));
            style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(5, 0, 5, 0)));
            column.ElementStyle = style;
        }

        public static void SelectionBrushCompat(
            this DataGrid grid,
            Brush brush,
            Brush foreground,
            Brush selectedForeground)
        {
            Style rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, foreground));
            Trigger selected = new Trigger();
            selected.Property = DataGridRow.IsSelectedProperty;
            selected.Value = true;
            selected.Setters.Add(new Setter(Control.BackgroundProperty, brush));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, selectedForeground));
            rowStyle.Triggers.Add(selected);
            grid.RowStyle = rowStyle;
        }
    }

}
