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
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace ResticBackuper.Dashboard
{
    public sealed class DashboardWindow : Window
    {
        private static readonly Brush BackgroundTop = BrushFrom("#08111F");
        private static readonly Brush BackgroundBottom = BrushFrom("#101B30");
        private static readonly Brush CardBrush = BrushFrom("#111D31");
        private static readonly Brush CardSoftBrush = BrushFrom("#0D1728");
        private static readonly Brush CardBorderBrush = BrushFrom("#25334B");
        private static readonly Brush PrimaryText = BrushFrom("#F3F7FC");
        private static readonly Brush MutedText = BrushFrom("#8FA0B8");
        private static readonly Brush Teal = BrushFrom("#2DD4BF");
        private static readonly Brush Blue = BrushFrom("#60A5FA");
        private static readonly Brush Green = BrushFrom("#34D399");
        private static readonly Brush Rose = BrushFrom("#FB7185");
        private static readonly Brush Amber = BrushFrom("#FBBF24");

        private readonly AppOptions options;
        private readonly TelemetryReader reader;
        private readonly EventWaitHandle showEvent;
        private readonly DispatcherTimer refreshTimer;
        private readonly Forms.NotifyIcon trayIcon;
        private Drawing.Icon trayApplicationIcon;
        private readonly List<Border> phaseMarkers = new List<Border>();
        private readonly List<TextBlock> phaseLabels = new List<TextBlock>();

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
        private DataGrid sourceGrid;
        private RunChart runChart;
        private Button previewButton;
        private Button addSourceButton;
        private Button removeSourceButton;
        private TextBlock sourceSummary;
        private TextBlock sourceStatus;

        private SourceConfiguration currentSourceConfiguration;
        private string sourceSignature;
        private bool sourceOperationInProgress;
        private DateTime sourceNoticeExpiresUtc = DateTime.MinValue;

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
            this.reader = new TelemetryReader(options.StateDirectory, true);

            Title = "ResticBackuper Dashboard";
            Width = 1280;
            Height = 950;
            MinWidth = 1040;
            MinHeight = 820;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new LinearGradientBrush(
                ((SolidColorBrush)BackgroundTop).Color,
                ((SolidColorBrush)BackgroundBottom).Color,
                new Point(0, 0),
                new Point(1, 1));
            FontFamily = new FontFamily("Segoe UI");
            Foreground = PrimaryText;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            Content = BuildInterface();
            Closing += OnClosing;
            Closed += OnClosed;
            Loaded += OnLoaded;
            Application.Current.SessionEnding += delegate { allowClose = true; };

            trayIcon = BuildTrayIcon();
            refreshTimer = new DispatcherTimer(DispatcherPriority.Background);
            refreshTimer.Interval = TimeSpan.FromSeconds(1);
            refreshTimer.Tick += OnRefreshTick;
            refreshTimer.Start();
        }

        private UIElement BuildInterface()
        {
            Grid root = new Grid();
            root.Margin = new Thickness(28, 22, 28, 18);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(220) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(112) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(170) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });

            UIElement header = BuildHeader();
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            UIElement hero = BuildHero();
            Grid.SetRow(hero, 2);
            root.Children.Add(hero);

            UIElement metrics = BuildMetrics();
            Grid.SetRow(metrics, 4);
            root.Children.Add(metrics);

            UIElement sources = BuildSources();
            Grid.SetRow(sources, 6);
            root.Children.Add(sources);

            UIElement history = BuildHistory();
            Grid.SetRow(history, 8);
            root.Children.Add(history);

            TextBlock footer = new TextBlock();
            footer.Text = "READ-ONLY TELEMETRY  •  UAC-PROTECTED FOLDER CHANGES  •  LOCAL METRICS  •  REFRESHES EVERY SECOND";
            footer.Foreground = BrushFrom("#617089");
            footer.FontSize = 10;
            footer.FontWeight = FontWeights.SemiBold;
            footer.VerticalAlignment = VerticalAlignment.Bottom;
            footer.HorizontalAlignment = HorizontalAlignment.Left;
            lastUpdated = new TextBlock();
            lastUpdated.Foreground = BrushFrom("#617089");
            lastUpdated.FontSize = 10;
            lastUpdated.HorizontalAlignment = HorizontalAlignment.Right;
            lastUpdated.VerticalAlignment = VerticalAlignment.Bottom;

            Grid footerGrid = new Grid();
            footerGrid.Children.Add(footer);
            footerGrid.Children.Add(lastUpdated);
            Grid.SetRow(footerGrid, 9);
            root.Children.Add(footerGrid);
            return root;
        }

        private UIElement BuildHeader()
        {
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel titlePanel = new StackPanel();
            TextBlock title = new TextBlock();
            title.Text = "ResticBackuper";
            title.FontSize = 28;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = PrimaryText;
            titlePanel.Children.Add(title);
            TextBlock subtitle = new TextBlock();
            subtitle.Text = "Encrypted incremental protection  •  Windows Task Scheduler  •  Powered by Restic";
            subtitle.FontSize = 12;
            subtitle.Foreground = MutedText;
            subtitle.Margin = new Thickness(1, 4, 0, 0);
            titlePanel.Children.Add(subtitle);
            grid.Children.Add(titlePanel);

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.VerticalAlignment = VerticalAlignment.Center;
            previewButton = CreateButton(previewEnabled ? "Stop preview" : "Preview animation");
            previewButton.Click += delegate
            {
                previewEnabled = !previewEnabled;
                previewStarted = DateTime.Now;
                previewButton.Content = previewEnabled ? "Stop preview" : "Preview animation";
                RefreshDashboard();
            };
            actions.Children.Add(previewButton);

            Button refresh = CreateButton("Refresh");
            refresh.Margin = new Thickness(10, 0, 14, 0);
            refresh.Click += delegate { RefreshDashboard(); };
            actions.Children.Add(refresh);

            statusDot = new Ellipse();
            statusDot.Width = 8;
            statusDot.Height = 8;
            statusDot.Fill = Blue;
            statusDot.Margin = new Thickness(0, 0, 8, 0);
            statusBadgeText = new TextBlock();
            statusBadgeText.Text = "LOADING";
            statusBadgeText.FontSize = 11;
            statusBadgeText.FontWeight = FontWeights.Bold;
            statusBadgeText.Foreground = PrimaryText;
            StackPanel badgeContent = new StackPanel();
            badgeContent.Orientation = Orientation.Horizontal;
            badgeContent.Children.Add(statusDot);
            badgeContent.Children.Add(statusBadgeText);
            statusBadge = new Border();
            statusBadge.Background = BrushFrom("#17243A");
            statusBadge.BorderBrush = CardBorderBrush;
            statusBadge.BorderThickness = new Thickness(1);
            statusBadge.CornerRadius = new CornerRadius(16);
            statusBadge.Padding = new Thickness(13, 8, 13, 8);
            statusBadge.Child = badgeContent;
            actions.Children.Add(statusBadge);

            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);
            return grid;
        }

        private UIElement BuildHero()
        {
            Border card = CreateCard();
            Grid grid = new Grid();
            grid.Margin = new Thickness(25, 21, 22, 20);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            StackPanel left = new StackPanel();
            heroTitle = new TextBlock();
            heroTitle.Text = "Reading protected backup state…";
            heroTitle.FontSize = 19;
            heroTitle.FontWeight = FontWeights.SemiBold;
            heroTitle.Foreground = PrimaryText;
            left.Children.Add(heroTitle);

            heroDetail = new TextBlock();
            heroDetail.Text = "Live progress will appear automatically when Restic starts.";
            heroDetail.FontSize = 12;
            heroDetail.Foreground = MutedText;
            heroDetail.Margin = new Thickness(0, 5, 0, 16);
            left.Children.Add(heroDetail);

            progressTrack = new Border();
            progressTrack.Height = 18;
            progressTrack.CornerRadius = new CornerRadius(9);
            progressTrack.Background = BrushFrom("#25334A");
            progressTrack.ClipToBounds = true;
            progressTrack.SizeChanged += delegate { AnimateProgress(currentProgress, false); };

            progressFill = new Border();
            progressFill.HorizontalAlignment = HorizontalAlignment.Left;
            progressFill.Width = 0;
            progressFill.CornerRadius = new CornerRadius(9);
            progressFill.Background = new LinearGradientBrush(
                ((SolidColorBrush)Blue).Color,
                ((SolidColorBrush)Teal).Color,
                new Point(0, 0.5),
                new Point(1, 0.5));
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
            left.Children.Add(progressTrack);

            Grid progressMeta = new Grid();
            progressMeta.Margin = new Thickness(0, 10, 0, 15);
            progressMeta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            progressMeta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            progressPercent = new TextBlock();
            progressPercent.Text = "0%";
            progressPercent.FontSize = 23;
            progressPercent.FontWeight = FontWeights.Bold;
            progressPercent.Foreground = PrimaryText;
            progressMeta.Children.Add(progressPercent);
            estimateBadge = new TextBlock();
            estimateBadge.Text = "ESTIMATE FROM VALIDATION BASELINE";
            estimateBadge.FontSize = 9;
            estimateBadge.FontWeight = FontWeights.Bold;
            estimateBadge.Foreground = Teal;
            estimateBadge.HorizontalAlignment = HorizontalAlignment.Right;
            estimateBadge.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(estimateBadge, 1);
            progressMeta.Children.Add(estimateBadge);
            left.Children.Add(progressMeta);

            left.Children.Add(BuildPhaseRail());
            grid.Children.Add(left);

            Border etaCard = new Border();
            etaCard.Background = CardSoftBrush;
            etaCard.BorderBrush = CardBorderBrush;
            etaCard.BorderThickness = new Thickness(1);
            etaCard.CornerRadius = new CornerRadius(13);
            etaCard.Padding = new Thickness(22, 19, 22, 18);
            Grid.SetColumn(etaCard, 2);
            StackPanel etaPanel = new StackPanel();
            etaTitle = new TextBlock();
            etaTitle.Text = "ESTIMATED TIME REMAINING";
            etaTitle.FontSize = 10;
            etaTitle.FontWeight = FontWeights.Bold;
            etaTitle.Foreground = MutedText;
            etaPanel.Children.Add(etaTitle);
            etaValue = new TextBlock();
            etaValue.Text = "—";
            etaValue.FontSize = 35;
            etaValue.FontWeight = FontWeights.SemiBold;
            etaValue.Foreground = PrimaryText;
            etaValue.Margin = new Thickness(0, 7, 0, 3);
            etaPanel.Children.Add(etaValue);
            etaHint = new TextBlock();
            etaHint.Text = "Waiting for the first real snapshot";
            etaHint.FontSize = 11;
            etaHint.Foreground = MutedText;
            etaHint.TextWrapping = TextWrapping.Wrap;
            etaPanel.Children.Add(etaHint);
            Border schedule = new Border();
            schedule.Background = BrushFrom("#14243A");
            schedule.CornerRadius = new CornerRadius(8);
            schedule.Padding = new Thickness(11, 8, 11, 8);
            schedule.Margin = new Thickness(0, 18, 0, 0);
            TextBlock scheduleText = new TextBlock();
            scheduleText.Text = "AUTOMATIC  •  02:00 DAILY";
            scheduleText.FontSize = 10;
            scheduleText.FontWeight = FontWeights.Bold;
            scheduleText.Foreground = Blue;
            schedule.Child = scheduleText;
            etaPanel.Children.Add(schedule);
            etaCard.Child = etaPanel;
            grid.Children.Add(etaCard);

            card.Child = grid;
            return card;
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
                marker.Width = 22;
                marker.Height = 22;
                marker.CornerRadius = new CornerRadius(11);
                marker.Background = BrushFrom("#25334A");
                marker.BorderBrush = CardBorderBrush;
                marker.BorderThickness = new Thickness(1);
                TextBlock number = new TextBlock();
                number.Text = (index + 1).ToString(CultureInfo.InvariantCulture);
                number.FontSize = 9;
                number.FontWeight = FontWeights.Bold;
                number.Foreground = MutedText;
                number.HorizontalAlignment = HorizontalAlignment.Center;
                number.VerticalAlignment = VerticalAlignment.Center;
                marker.Child = number;
                phaseMarkers.Add(marker);
                step.Children.Add(marker);
                TextBlock label = new TextBlock();
                label.Text = labels[index];
                label.FontSize = 9;
                label.Foreground = MutedText;
                label.Margin = new Thickness(0, 4, 0, 0);
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
            Grid grid = new Grid();
            for (int index = 0; index < 5; index++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                if (index < 4)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                }
            }

            filesValue = new TextBlock();
            bytesValue = new TextBlock();
            speedValue = new TextBlock();
            elapsedValue = new TextBlock();
            errorsValue = new TextBlock();
            AddMetric(grid, 0, "FILES", filesValue, "items visited", Blue);
            AddMetric(grid, 2, "PROCESSED", bytesValue, "logical source data", Teal);
            AddMetric(grid, 4, "THROUGHPUT", speedValue, "current average", BrushFrom("#A78BFA"));
            AddMetric(grid, 6, "ELAPSED", elapsedValue, "current or latest run", Amber);
            AddMetric(grid, 8, "ERRORS", errorsValue, "from protected Restic output", Rose);
            return grid;
        }

        private void AddMetric(Grid grid, int column, string title, TextBlock value, string hint, Brush accent)
        {
            Border card = CreateCard();
            card.Padding = new Thickness(18, 14, 18, 13);
            StackPanel panel = new StackPanel();
            TextBlock heading = new TextBlock();
            heading.Text = title;
            heading.FontSize = 9;
            heading.FontWeight = FontWeights.Bold;
            heading.Foreground = accent;
            panel.Children.Add(heading);
            value.Text = "—";
            value.FontSize = 21;
            value.FontWeight = FontWeights.SemiBold;
            value.Foreground = PrimaryText;
            value.Margin = new Thickness(0, 5, 0, 1);
            panel.Children.Add(value);
            TextBlock subtitle = new TextBlock();
            subtitle.Text = hint;
            subtitle.FontSize = 9;
            subtitle.Foreground = MutedText;
            panel.Children.Add(subtitle);
            card.Child = panel;
            Grid.SetColumn(card, column);
            grid.Children.Add(card);
        }

        private UIElement BuildSources()
        {
            Border card = CreateCard();
            card.Padding = new Thickness(18, 13, 18, 11);

            Grid cardGrid = new Grid();
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel heading = new StackPanel();
            heading.Orientation = Orientation.Horizontal;
            TextBlock title = new TextBlock();
            title.Text = "Backed-up folders";
            title.FontSize = 15;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = PrimaryText;
            title.VerticalAlignment = VerticalAlignment.Center;
            heading.Children.Add(title);

            sourceSummary = new TextBlock();
            sourceSummary.Text = "Loading protected configuration...";
            sourceSummary.FontSize = 10;
            sourceSummary.Foreground = MutedText;
            sourceSummary.Margin = new Thickness(12, 3, 0, 0);
            sourceSummary.VerticalAlignment = VerticalAlignment.Center;
            heading.Children.Add(sourceSummary);
            header.Children.Add(heading);

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            addSourceButton = CreateButton("Add folder");
            addSourceButton.Background = BrushFrom("#153A3A");
            addSourceButton.BorderBrush = BrushFrom("#25645F");
            addSourceButton.Click += OnAddSourceClick;
            actions.Children.Add(addSourceButton);

            removeSourceButton = CreateButton("Remove selected");
            removeSourceButton.Margin = new Thickness(8, 0, 0, 0);
            removeSourceButton.IsEnabled = false;
            removeSourceButton.Click += OnRemoveSourceClick;
            actions.Children.Add(removeSourceButton);
            Grid.SetColumn(actions, 1);
            header.Children.Add(actions);
            cardGrid.Children.Add(header);

            sourceGrid = CreateSourceGrid();
            sourceGrid.Margin = new Thickness(0, 8, 0, 5);
            sourceGrid.SelectionChanged += delegate { UpdateSourceButtons(); };
            Grid.SetRow(sourceGrid, 1);
            cardGrid.Children.Add(sourceGrid);

            sourceStatus = new TextBlock();
            sourceStatus.Text = "Select a folder to remove it. Changes require Windows approval.";
            sourceStatus.FontSize = 9;
            sourceStatus.Foreground = MutedText;
            sourceStatus.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetRow(sourceStatus, 2);
            cardGrid.Children.Add(sourceStatus);

            card.Child = cardGrid;
            return card;
        }

        private DataGrid CreateSourceGrid()
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
            grid.RowHeight = 28;
            grid.SelectionMode = DataGridSelectionMode.Single;
            grid.SelectionUnit = DataGridSelectionUnit.FullRow;
            grid.SelectionBrushCompat(BrushFrom("#213D59"));

            Style headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, MutedText));
            headerStyle.Setters.Add(new Setter(Control.FontSizeProperty, 9.0));
            headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold));
            headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
            headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, CardBorderBrush));
            headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 0, 5, 5)));
            grid.ColumnHeaderStyle = headerStyle;

            grid.Columns.Add(TextColumn("FOLDER PATH", "SourcePath", 4.2));
            grid.Columns.Add(TextColumn("ROLE", "RoleLabel", 1.1));
            return grid;
        }

        private UIElement BuildHistory()
        {
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });

            Border chartCard = CreateCard();
            chartCard.Padding = new Thickness(20, 16, 20, 14);
            Grid chartGrid = new Grid();
            chartGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            chartGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid chartHeader = new Grid();
            chartHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            chartHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock chartTitle = new TextBlock();
            chartTitle.Text = "Run trends";
            chartTitle.FontSize = 15;
            chartTitle.FontWeight = FontWeights.SemiBold;
            chartHeader.Children.Add(chartTitle);
            runCount = new TextBlock();
            runCount.Text = "0 RUNS";
            runCount.FontSize = 9;
            runCount.FontWeight = FontWeights.Bold;
            runCount.Foreground = MutedText;
            runCount.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(runCount, 1);
            chartHeader.Children.Add(runCount);
            chartGrid.Children.Add(chartHeader);
            runChart = new RunChart();
            runChart.Margin = new Thickness(0, 10, 0, 0);
            Grid.SetRow(runChart, 1);
            chartGrid.Children.Add(runChart);
            chartCard.Child = chartGrid;
            grid.Children.Add(chartCard);

            Border tableCard = CreateCard();
            tableCard.Padding = new Thickness(18, 15, 18, 12);
            Grid tableGrid = new Grid();
            tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            tableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            TextBlock tableTitle = new TextBlock();
            tableTitle.Text = "Run history";
            tableTitle.FontSize = 15;
            tableTitle.FontWeight = FontWeights.SemiBold;
            tableGrid.Children.Add(tableTitle);
            historyGrid = CreateHistoryGrid();
            historyGrid.Margin = new Thickness(0, 10, 0, 0);
            Grid.SetRow(historyGrid, 1);
            tableGrid.Children.Add(historyGrid);
            tableCard.Child = tableGrid;
            Grid.SetColumn(tableCard, 2);
            grid.Children.Add(tableCard);
            return grid;
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
            grid.RowHeight = 32;
            grid.SelectionMode = DataGridSelectionMode.Single;
            grid.SelectionUnit = DataGridSelectionUnit.FullRow;
            grid.SelectionBrushCompat(BrushFrom("#213D59"));

            Style headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, MutedText));
            headerStyle.Setters.Add(new Setter(Control.FontSizeProperty, 9.0));
            headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold));
            headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
            headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, CardBorderBrush));
            headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 0, 5, 7)));
            grid.ColumnHeaderStyle = headerStyle;

            grid.Columns.Add(TextColumn("WHEN", "StartedDisplay", 1.2));
            grid.Columns.Add(TextColumn("TYPE", "TypeLabel", 0.8));
            grid.Columns.Add(TextColumn("RESULT", "StateLabel", 0.95));
            grid.Columns.Add(TextColumn("DURATION", "DurationDisplay", 0.9));
            grid.Columns.Add(TextColumn("FILES", "FilesDisplay", 0.9));
            grid.Columns.Add(TextColumn("PROCESSED", "ProcessedDisplay", 1.05));
            return grid;
        }

        private static DataGridTextColumn TextColumn(string header, string path, double width)
        {
            DataGridTextColumn column = new DataGridTextColumn();
            column.Header = header;
            column.Binding = new Binding(path);
            column.Width = new DataGridLength(width, DataGridLengthUnitType.Star);
            ElementStyleHolder.Apply(column);
            return column;
        }

        private void OnLoaded(object sender, RoutedEventArgs args)
        {
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

        private void RefreshDashboard()
        {
            try
            {
                TelemetrySnapshot snapshot = reader.Load();
                lastSnapshot = snapshot;
                ApplySnapshot(snapshot);
                if (previewEnabled)
                {
                    ApplyPreview(snapshot);
                }
                HandleStateTransition(snapshot);
            }
            catch (Exception error)
            {
                heroTitle.Text = "Dashboard data is temporarily unavailable";
                heroDetail.Text = error.Message;
                SetBadge("DATA RETRY", Amber, BrushFrom("#3A2F18"));
                lastUpdated.Text = "Retrying automatically";
            }
            RefreshSources(false);
        }

        private void RefreshSources(bool force)
        {
            try
            {
                SourceConfiguration configuration = SourceConfiguration.Load();
                string signature = string.Join(
                    "\u001f",
                    configuration.Sources.Select(source =>
                        (source.IsProtectedCanary ? "canary:" : "user:") + source.SourcePath).ToArray());
                string selectedPath = null;
                BackupSourceView selected = sourceGrid.SelectedItem as BackupSourceView;
                if (selected != null)
                {
                    selectedPath = selected.SourcePath;
                }

                currentSourceConfiguration = configuration;
                if (force || !string.Equals(signature, sourceSignature, StringComparison.Ordinal))
                {
                    sourceSignature = signature;
                    sourceGrid.ItemsSource = configuration.Sources.ToList();
                    if (selectedPath != null)
                    {
                        foreach (BackupSourceView item in configuration.Sources)
                        {
                            if (string.Equals(item.SourcePath, selectedPath, StringComparison.OrdinalIgnoreCase))
                            {
                                sourceGrid.SelectedItem = item;
                                break;
                            }
                        }
                    }
                }

                int userSources = configuration.Sources.Count(source => !source.IsProtectedCanary);
                bool hasCanarySource = configuration.Sources.Any(source => source.IsProtectedCanary);
                sourceSummary.Text = userSources.ToString(CultureInfo.CurrentCulture) +
                    (userSources == 1 ? " folder" : " folders") +
                    (hasCanarySource ? "  |  protected restore canary" : "  |  restore canary covered");

                if (DateTime.UtcNow >= sourceNoticeExpiresUtc)
                {
                    if (!File.Exists(configuration.ManagerPath))
                    {
                        sourceStatus.Text = "Folder management is unavailable because the protected manager is missing.";
                        sourceStatus.Foreground = Rose;
                    }
                    else if (hasCanarySource)
                    {
                        sourceStatus.Text = "The protected canary verifies restores and cannot be removed.";
                        sourceStatus.Foreground = MutedText;
                    }
                    else
                    {
                        sourceStatus.Text = "Select a folder to remove it. Changes require Windows approval.";
                        sourceStatus.Foreground = MutedText;
                    }
                }
            }
            catch (Exception error)
            {
                currentSourceConfiguration = null;
                sourceSignature = null;
                sourceGrid.ItemsSource = null;
                sourceSummary.Text = "Protected configuration unavailable";
                if (DateTime.UtcNow >= sourceNoticeExpiresUtc)
                {
                    sourceStatus.Text = error.Message;
                    sourceStatus.Foreground = Rose;
                }
            }
            UpdateSourceButtons();
        }

        private void UpdateSourceButtons()
        {
            bool managerAvailable = currentSourceConfiguration != null &&
                File.Exists(currentSourceConfiguration.ManagerPath);
            BackupSourceView selected = sourceGrid == null
                ? null
                : sourceGrid.SelectedItem as BackupSourceView;
            if (addSourceButton != null)
            {
                addSourceButton.IsEnabled = !sourceOperationInProgress && managerAvailable;
            }
            if (removeSourceButton != null)
            {
                removeSourceButton.IsEnabled = !sourceOperationInProgress &&
                    managerAvailable &&
                    selected != null &&
                    !selected.IsProtectedCanary;
            }
        }

        private async void OnAddSourceClick(object sender, RoutedEventArgs args)
        {
            if (sourceOperationInProgress || currentSourceConfiguration == null)
            {
                return;
            }

            string sourcePath;
            using (Forms.FolderBrowserDialog dialog = new Forms.FolderBrowserDialog())
            {
                dialog.Description = "Choose a folder to include in future ResticBackuper runs.";
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
            await ApplySourceChange("Add", sourcePath);
        }

        private async void OnRemoveSourceClick(object sender, RoutedEventArgs args)
        {
            if (sourceOperationInProgress || currentSourceConfiguration == null)
            {
                return;
            }
            BackupSourceView selected = sourceGrid.SelectedItem as BackupSourceView;
            if (selected == null || selected.IsProtectedCanary)
            {
                SetSourceNotice("The protected restore canary cannot be removed.", Amber, 8);
                return;
            }

            MessageBoxResult confirmation = MessageBox.Show(
                this,
                "Stop backing up this folder in future runs?\n\n" + selected.SourcePath +
                    "\n\nExisting Restic snapshots are not deleted by this action.",
                "Remove backed-up folder",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }
            await ApplySourceChange("Remove", selected.SourcePath);
        }

        private async Task ApplySourceChange(string action, string sourcePath)
        {
            SourceConfiguration configuration = currentSourceConfiguration;
            sourceOperationInProgress = true;
            SetSourceNotice("Waiting for Windows approval...", Blue, 600);
            UpdateSourceButtons();

            SourceManagerResult result;
            try
            {
                result = await Task.Run(delegate
                {
                    return SourceManagerLauncher.Run(configuration, action, sourcePath);
                });
            }
            catch (Exception error)
            {
                result = SourceManagerResult.Failure(error.Message, -1);
            }
            finally
            {
                sourceOperationInProgress = false;
            }

            if (result.UserCancelled)
            {
                SetSourceNotice("Windows approval was cancelled. No folders were changed.", Amber, 12);
                MessageBox.Show(
                    this,
                    "Windows approval was cancelled. The backup folder list was not changed.",
                    "Folder change cancelled",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                RefreshSources(false);
                return;
            }
            if (!result.Succeeded)
            {
                string detail = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "The protected source manager could not complete the request."
                    : result.ErrorMessage;
                SetSourceNotice("Folder change failed. The protected configuration was not updated.", Rose, 15);
                MessageBox.Show(
                    this,
                    "The folder list was not changed.\n\n" + detail,
                    "Folder change failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                RefreshSources(false);
                return;
            }

            RefreshSources(true);
            bool isPresent = currentSourceConfiguration != null &&
                currentSourceConfiguration.ContainsUserSource(sourcePath);
            bool confirmed = currentSourceConfiguration != null &&
                (string.Equals(action, "Add", StringComparison.Ordinal)
                    ? isPresent
                    : !isPresent);
            if (!confirmed)
            {
                SetSourceNotice("The manager finished, but the protected configuration did not confirm the change.", Rose, 15);
                MessageBox.Show(
                    this,
                    "The protected configuration did not confirm the requested folder change. " +
                        "Refresh the dashboard and check the installation logs.",
                    "Folder change not confirmed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            string success = string.Equals(action, "Add", StringComparison.Ordinal)
                ? "Folder added. It will be included in the next backup."
                : "Folder removed from future backups. Existing snapshots were not deleted.";
            SetSourceNotice(success, Green, 12);
            ShowTrayMessage(
                string.Equals(action, "Add", StringComparison.Ordinal) ? "Backup folder added" : "Backup folder removed",
                sourcePath,
                Forms.ToolTipIcon.Info);
            UpdateSourceButtons();
        }

        private void SetSourceNotice(string message, Brush color, int seconds)
        {
            sourceStatus.Text = message;
            sourceStatus.Foreground = color;
            sourceNoticeExpiresUtc = DateTime.UtcNow.AddSeconds(seconds);
        }

        private void ApplySnapshot(TelemetrySnapshot snapshot)
        {
            heroTitle.Text = snapshot.StatusLabel;
            heroDetail.Text = snapshot.StatusDetail;
            bool firstBackupPending = string.Equals(
                snapshot.StateKey,
                "ready",
                StringComparison.OrdinalIgnoreCase);
            progressPercent.Text = firstBackupPending
                ? "First backup pending"
                : (snapshot.Percent * 100).ToString("0.0", CultureInfo.CurrentCulture) + "%";
            estimateBadge.Text = firstBackupPending
                ? "DRY-RUN BASELINE  •  NOT YET BACKED UP"
                : snapshot.IsSuccess
                    ? "FINAL VERIFIED RESULT"
                : snapshot.ProgressIsEstimated
                    ? "ESTIMATED  •  " + snapshot.ConfidenceLabel.ToUpperInvariant()
                    : "RESTIC COUNTERS  •  COMPOSED OVERALL";
            estimateBadge.Foreground = firstBackupPending
                ? Amber
                : snapshot.ProgressIsEstimated ? Teal : Blue;
            AnimateProgress(snapshot.Percent, true);

            etaTitle.Text = "ESTIMATED TIME REMAINING";
            if (snapshot.Eta.HasValue)
            {
                etaValue.Text = FormatDuration(snapshot.Eta.Value);
                etaHint.Text = snapshot.EstimatedCompletion.HasValue
                    ? "Likely completion around " + snapshot.EstimatedCompletion.Value.ToString("HH:mm", CultureInfo.CurrentCulture)
                    : "Estimate updates as Restic advances";
            }
            else if (snapshot.IsSuccess)
            {
                etaValue.Text = "Complete";
                etaHint.Text = "Snapshot and verification finished";
            }
            else if (snapshot.IsFailure)
            {
                etaValue.Text = "Stopped";
                etaHint.Text = "Open the protected logs for the recorded error";
            }
            else if (snapshot.IsActive)
            {
                etaValue.Text = "Calculating";
                etaHint.Text = "Building an estimate from live progress and run history";
            }
            else
            {
                etaTitle.Text = "NEXT AUTOMATIC RUN";
                etaValue.Text = "02:00";
                etaHint.Text = "Next automatic run; StartWhenAvailable is enabled";
            }

            long shownFiles = snapshot.FilesDone > 0 ? snapshot.FilesDone : snapshot.EstimatedFiles;
            long shownBytes = snapshot.BytesDone > 0 ? snapshot.BytesDone : snapshot.EstimatedBytes;
            filesValue.Text = FormatNumber(shownFiles);
            bytesValue.Text = FormatBytes(shownBytes);
            speedValue.Text = snapshot.TransferRateBytesPerSecond > 0
                ? FormatBytes(snapshot.TransferRateBytesPerSecond) + "/s"
                : "—";
            elapsedValue.Text = snapshot.Elapsed > TimeSpan.Zero ? FormatDuration(snapshot.Elapsed) : "—";
            errorsValue.Text = snapshot.ErrorCount.ToString("N0", CultureInfo.CurrentCulture);
            errorsValue.Foreground = snapshot.ErrorCount > 0 ? Rose : PrimaryText;

            SetPhase(snapshot.PhaseIndex, snapshot.IsFailure);
            if (snapshot.IsActive)
            {
                SetBadge("LIVE  •  " + snapshot.PhaseLabel.ToUpperInvariant(), Teal, BrushFrom("#12352F"));
                StartStatusPulse();
            }
            else if (snapshot.IsFailure)
            {
                SetBadge("NEEDS ATTENTION", Rose, BrushFrom("#3B1D2A"));
                StopStatusPulse();
            }
            else if (snapshot.IsSuccess)
            {
                SetBadge("VERIFIED", Green, BrushFrom("#14352C"));
                StopStatusPulse();
            }
            else
            {
                SetBadge("READY", Blue, BrushFrom("#172B45"));
                StopStatusPulse();
            }

            IList<RunMetricView> history = snapshot.History ?? new List<RunMetricView>();
            historyGrid.ItemsSource = history.OrderByDescending(item => item.StartedLocal).ToList();
            runChart.Runs = history.OrderBy(item => item.StartedLocal).ToList();
            runCount.Text = history.Count.ToString(CultureInfo.InvariantCulture) + (history.Count == 1 ? " RUN" : " RUNS");
            lastUpdated.Text = "Updated " + snapshot.LastUpdatedLocal.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
            WriteHeartbeat(snapshot, true);
        }

        private void ApplyPreview(TelemetrySnapshot snapshot)
        {
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
            heroDetail.Text = "Clearly labeled simulation using your verified validation baseline.";
            progressPercent.Text = (fraction * 100).ToString("0.0", CultureInfo.CurrentCulture) + "%";
            estimateBadge.Text = "PREVIEW  •  ESTIMATED FROM VALIDATION";
            estimateBadge.Foreground = Amber;
            AnimateProgress(fraction, true);
            etaTitle.Text = "ESTIMATED TIME REMAINING";
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
            if (!SystemParameters.ClientAreaAnimation || shimmerTransform == null)
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

        private void StartStatusPulse()
        {
            if (!SystemParameters.ClientAreaAnimation || statusDot == null)
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
            icon.Text = "ResticBackuper Dashboard";
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
            WriteHeartbeat(lastSnapshot, false);
            refreshTimer.Stop();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            if (trayApplicationIcon != null)
            {
                trayApplicationIcon.Dispose();
                trayApplicationIcon = null;
            }
        }

        private static Border CreateCard()
        {
            Border border = new Border();
            border.Background = CardBrush;
            border.BorderBrush = CardBorderBrush;
            border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(14);
            border.Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 3,
                Opacity = 0.18,
                Color = Colors.Black
            };
            return border;
        }

        private void WriteHeartbeat(TelemetrySnapshot snapshot, bool running)
        {
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
                string temporary = path + "." + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + ".tmp";
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
                    File.Replace(temporary, path, null, true);
                }
                else
                {
                    File.Move(temporary, path);
                }
                lastHeartbeatUtc = now;
            }
            catch
            {
                // The monitor remains useful even if its presentation cache is unavailable.
            }
        }

        private static Button CreateButton(string label)
        {
            Button button = new Button();
            button.Content = label;
            button.Foreground = PrimaryText;
            button.Background = BrushFrom("#17243A");
            button.BorderBrush = CardBorderBrush;
            button.BorderThickness = new Thickness(1);
            button.Padding = new Thickness(13, 7, 13, 7);
            button.FontSize = 11;
            button.FontWeight = FontWeights.SemiBold;
            button.Cursor = Cursors.Hand;
            return button;
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

        private static Brush BrushFrom(string value)
        {
            SolidColorBrush brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            return brush;
        }
    }

    internal static class ElementStyleHolder
    {
        public static void Apply(DataGridTextColumn column)
        {
            Style style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D7E1EF"))));
            style.Setters.Add(new Setter(TextBlock.FontSizeProperty, 10.0));
            style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(5, 0, 5, 0)));
            column.ElementStyle = style;
        }

        public static void SelectionBrushCompat(this DataGrid grid, Brush brush)
        {
            Style rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, DashboardWindowBrushes.Primary));
            Trigger selected = new Trigger();
            selected.Property = DataGridRow.IsSelectedProperty;
            selected.Value = true;
            selected.Setters.Add(new Setter(Control.BackgroundProperty, brush));
            rowStyle.Triggers.Add(selected);
            grid.RowStyle = rowStyle;
        }
    }

    internal static class DashboardWindowBrushes
    {
        public static readonly Brush Primary = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3F7FC"));
    }
}
