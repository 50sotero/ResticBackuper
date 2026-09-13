using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ResticBackuper.Dashboard
{
    internal sealed class ScheduleEditorWindow : Window
    {
        private readonly TaskSchedule installed;
        private readonly DashboardThemePalette palette;
        private readonly RadioButton dailyRadio;
        private readonly RadioButton selectedDaysRadio;
        private readonly TextBox timeText;
        private readonly StackPanel dayPanel;
        private readonly Dictionary<DayOfWeek, CheckBox> dayChecks;
        private readonly CheckBox enabledCheck;
        private readonly CheckBox startWhenAvailableCheck;
        private readonly CheckBox wakeCheck;
        private readonly CheckBox allowBatteryCheck;
        private readonly CheckBox finishOnBatteryCheck;
        private readonly TextBlock validationText;
        private readonly Button reviewButton;

        public ScheduleEditorWindow(TaskSchedule installed, DashboardThemePalette palette)
        {
            if (installed == null)
            {
                throw new ArgumentNullException("installed");
            }
            if (palette == null)
            {
                throw new ArgumentNullException("palette");
            }

            this.installed = installed;
            this.palette = palette;
            this.dayChecks = new Dictionary<DayOfWeek, CheckBox>();

            Title = "Backup schedule";
            Width = 780;
            Height = 690;
            MinWidth = 680;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            ShowInTaskbar = false;
            DashboardVisualStyle.ApplyWindow(this, palette);
            AutomationProperties.SetName(this, "Edit automatic backup schedule");

            Grid root = new Grid { Margin = new Thickness(28, 24, 28, 20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel heading = new StackPanel();
            TextBlock title = new TextBlock
            {
                Text = "Backup schedule",
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                Foreground = palette.TextPrimary
            };
            heading.Children.Add(title);
            TextBlock intro = new TextBlock
            {
                Text = "Choose when Windows should run the protected backup. Saving a schedule never starts a backup immediately.",
                FontSize = 12,
                Foreground = palette.TextSecondary,
                Margin = new Thickness(0, 5, 0, 18),
                TextWrapping = TextWrapping.Wrap
            };
            heading.Children.Add(intro);
            root.Children.Add(heading);

            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            StackPanel content = new StackPanel();
            scroll.Content = content;

            Border installedCard = CreateSection();
            StackPanel installedPanel = new StackPanel();
            installedPanel.Children.Add(CreateSectionTitle("Installed schedule"));
            installedPanel.Children.Add(new TextBlock
            {
                Text = installed.Summary,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = palette.TextPrimary,
                Margin = new Thickness(0, 4, 0, 3)
            });
            installedPanel.Children.Add(new TextBlock
            {
                Text = "Next run: " + installed.NextRunDisplay + "\n" + installed.SettingsSummary,
                FontSize = 12,
                Foreground = palette.TextSecondary,
                TextWrapping = TextWrapping.Wrap
            });
            installedCard.Child = installedPanel;
            content.Children.Add(installedCard);

            Border frequencyCard = CreateSection();
            frequencyCard.Margin = new Thickness(0, 12, 0, 0);
            StackPanel frequencyPanel = new StackPanel();
            frequencyPanel.Children.Add(CreateSectionTitle("Frequency and time"));

            dailyRadio = CreateRadio("Every day", "ScheduleFrequency");
            dailyRadio.Margin = new Thickness(0, 10, 0, 0);
            selectedDaysRadio = CreateRadio("Selected days", "ScheduleFrequency");
            selectedDaysRadio.Margin = new Thickness(0, 7, 0, 0);
            dailyRadio.Checked += delegate { UpdateDayAvailability(); };
            selectedDaysRadio.Checked += delegate { UpdateDayAvailability(); };
            frequencyPanel.Children.Add(dailyRadio);
            frequencyPanel.Children.Add(selectedDaysRadio);

            dayPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(24, 9, 0, 2)
            };
            AddDay(dayPanel, DayOfWeek.Monday, "Mon");
            AddDay(dayPanel, DayOfWeek.Tuesday, "Tue");
            AddDay(dayPanel, DayOfWeek.Wednesday, "Wed");
            AddDay(dayPanel, DayOfWeek.Thursday, "Thu");
            AddDay(dayPanel, DayOfWeek.Friday, "Fri");
            AddDay(dayPanel, DayOfWeek.Saturday, "Sat");
            AddDay(dayPanel, DayOfWeek.Sunday, "Sun");
            frequencyPanel.Children.Add(dayPanel);

            Grid timeRow = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            timeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            timeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock timeLabel = new TextBlock
            {
                Text = "Start time",
                FontSize = 12,
                Foreground = palette.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            };
            timeRow.Children.Add(timeLabel);
            timeText = new TextBox
            {
                Text = TaskScheduleCanonicalizer.FormatTime(installed.TimeOfDay),
                Width = 92,
                MinHeight = 34,
                Padding = new Thickness(9, 5, 9, 5),
                FontSize = 13,
                Background = palette.Surface,
                Foreground = palette.TextPrimary,
                BorderBrush = palette.Border,
                ToolTip = "24-hour time, for example 02:00 or 18:30"
            };
            AutomationProperties.SetName(timeText, "Backup start time in 24-hour HH:mm format");
            timeText.LostKeyboardFocus += delegate { NormalizeTimeIfValid(); };
            timeText.PreviewKeyDown += OnTimePreviewKeyDown;
            Grid.SetColumn(timeText, 1);
            timeRow.Children.Add(timeText);
            frequencyPanel.Children.Add(timeRow);
            TextBlock timeHelp = new TextBlock
            {
                Text = "Use 24-hour time. Windows may delay a run slightly after resume or sign-in.",
                FontSize = 11,
                Foreground = palette.TextTertiary,
                Margin = new Thickness(150, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            frequencyPanel.Children.Add(timeHelp);
            frequencyCard.Child = frequencyPanel;
            content.Children.Add(frequencyCard);

            Border behaviorCard = CreateSection();
            behaviorCard.Margin = new Thickness(0, 12, 0, 0);
            StackPanel behaviorPanel = new StackPanel();
            behaviorPanel.Children.Add(CreateSectionTitle("Automatic run behavior"));
            enabledCheck = CreateCheck("Automatic backups enabled", "Pause or resume this automatic schedule.");
            enabledCheck.Margin = new Thickness(0, 10, 0, 0);
            startWhenAvailableCheck = CreateCheck(
                "Run when the PC is next available",
                "Runs a missed backup after the PC becomes available again.");
            wakeCheck = CreateCheck(
                "Wake this PC from sleep",
                "May wake the PC from sleep. It cannot power on a shut-down PC.");
            allowBatteryCheck = CreateCheck(
                "Allow a backup to start on battery",
                "When off, Windows waits for AC power before starting.");
            finishOnBatteryCheck = CreateCheck(
                "Let a running backup finish if the PC is unplugged",
                "When off, Windows may stop the task after switching to battery.");
            behaviorPanel.Children.Add(enabledCheck);
            behaviorPanel.Children.Add(startWhenAvailableCheck);
            behaviorPanel.Children.Add(wakeCheck);
            behaviorPanel.Children.Add(allowBatteryCheck);
            behaviorPanel.Children.Add(finishOnBatteryCheck);
            behaviorCard.Child = behaviorPanel;
            content.Children.Add(behaviorCard);

            validationText = new TextBlock
            {
                FontSize = 12,
                Foreground = palette.Danger,
                Margin = new Thickness(2, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };
            AutomationProperties.SetLiveSetting(validationText, AutomationLiveSetting.Assertive);
            content.Children.Add(validationText);

            Grid footer = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            TextBlock safety = new TextBlock
            {
                Text = "Administrator approval is required. The installed task is re-read and verified after saving.",
                FontSize = 11,
                Foreground = palette.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 18, 0)
            };
            footer.Children.Add(safety);

            StackPanel footerButtons = new StackPanel { Orientation = Orientation.Horizontal };
            Button cancel = CreateButton("Cancel", false);
            cancel.IsCancel = true;
            cancel.Click += delegate { DialogResult = false; };
            footerButtons.Children.Add(cancel);
            reviewButton = CreateButton("Review changes", true);
            reviewButton.Margin = new Thickness(10, 0, 0, 0);
            reviewButton.IsDefault = true;
            reviewButton.Click += OnReviewClick;
            AutomationProperties.SetHelpText(
                reviewButton,
                "Review the requested schedule before asking Windows for administrator approval.");
            footerButtons.Children.Add(reviewButton);
            Grid.SetColumn(footerButtons, 1);
            footer.Children.Add(footerButtons);

            Content = root;
            LoadInstalledValues();
        }

        public ScheduleChangeRequest RequestedChange { get; private set; }

        private Border CreateSection()
        {
            return new Border
            {
                Background = palette.SurfaceSoft,
                BorderBrush = palette.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(18, 15, 18, 16)
            };
        }

        private TextBlock CreateSectionTitle(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = palette.TextPrimary
            };
        }

        private RadioButton CreateRadio(string text, string group)
        {
            return new RadioButton
            {
                Content = text,
                GroupName = group,
                FontSize = 12,
                Foreground = palette.TextPrimary,
                MinHeight = 26
            };
        }

        private CheckBox CreateCheck(string text, string help)
        {
            CheckBox check = new CheckBox
            {
                Content = text,
                FontSize = 12,
                Foreground = palette.TextPrimary,
                MinHeight = 27,
                Margin = new Thickness(0, 7, 0, 0),
                ToolTip = help
            };
            AutomationProperties.SetName(check, text);
            AutomationProperties.SetHelpText(check, help);
            return check;
        }

        private Button CreateButton(string text, bool primary)
        {
            Button button = new Button
            {
                Content = text,
                MinHeight = 36,
                MinWidth = primary ? 132 : 88,
                Padding = new Thickness(14, 5, 14, 5),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Background = primary ? palette.AccentPrimary : palette.ButtonBackground,
                Foreground = primary ? palette.TextOnAccent : palette.ButtonText,
                BorderBrush = primary ? palette.AddButtonBorder : palette.Border,
                BorderThickness = new Thickness(1)
            };
            ToolTipService.SetShowOnDisabled(button, true);
            DashboardVisualStyle.ApplyButtonChrome(button, 8);
            DashboardVisualStyle.ApplyFocusOutline(button, palette.Focus);
            AutomationProperties.SetName(button, text);
            return button;
        }

        private void AddDay(Panel parent, DayOfWeek day, string label)
        {
            CheckBox check = new CheckBox
            {
                Content = label,
                Tag = day,
                FontSize = 11,
                Foreground = palette.TextPrimary,
                Margin = new Thickness(0, 0, 12, 0),
                MinHeight = 28
            };
            AutomationProperties.SetName(check, label);
            dayChecks[day] = check;
            parent.Children.Add(check);
        }

        private void LoadInstalledValues()
        {
            dailyRadio.IsChecked = installed.Cadence == BackupScheduleCadence.Daily;
            selectedDaysRadio.IsChecked = installed.Cadence == BackupScheduleCadence.SelectedDays;
            foreach (KeyValuePair<DayOfWeek, CheckBox> item in dayChecks)
            {
                item.Value.IsChecked = installed.Days.Contains(item.Key);
            }
            enabledCheck.IsChecked = installed.Enabled;
            startWhenAvailableCheck.IsChecked = installed.StartWhenAvailable;
            wakeCheck.IsChecked = installed.WakeToRun;
            allowBatteryCheck.IsChecked = installed.AllowStartOnBatteries;
            finishOnBatteryCheck.IsChecked = !installed.StopIfGoingOnBatteries;
            UpdateDayAvailability();
        }

        private void UpdateDayAvailability()
        {
            bool enabled = selectedDaysRadio != null && selectedDaysRadio.IsChecked == true;
            if (dayPanel != null)
            {
                dayPanel.IsEnabled = enabled;
                dayPanel.Opacity = enabled ? 1 : 0.55;
            }
        }

        private void OnTimePreviewKeyDown(object sender, KeyEventArgs args)
        {
            if (args.Key != Key.Up && args.Key != Key.Down)
            {
                return;
            }
            TimeSpan current;
            if (!TryParseTime(timeText.Text, out current))
            {
                current = installed.TimeOfDay;
            }
            int delta = args.Key == Key.Up ? 15 : -15;
            int minutes = ((int)current.TotalMinutes + delta + (24 * 60)) % (24 * 60);
            timeText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}:{1:00}",
                minutes / 60,
                minutes % 60);
            timeText.SelectAll();
            args.Handled = true;
        }

        private void NormalizeTimeIfValid()
        {
            TimeSpan value;
            if (TryParseTime(timeText.Text, out value))
            {
                timeText.Text = TaskScheduleCanonicalizer.FormatTime(value);
            }
        }

        private static bool TryParseTime(string text, out TimeSpan value)
        {
            return TimeSpan.TryParseExact(
                (text ?? string.Empty).Trim(),
                @"hh\:mm",
                CultureInfo.InvariantCulture,
                out value) && value.TotalHours >= 0 && value.TotalHours < 24;
        }

        private void OnReviewClick(object sender, RoutedEventArgs args)
        {
            SetValidation(null);
            TimeSpan time;
            if (!TryParseTime(timeText.Text, out time))
            {
                SetValidation("Enter a valid 24-hour time in HH:mm format, such as 02:00 or 18:30.");
                timeText.Focus();
                timeText.SelectAll();
                return;
            }

            BackupScheduleCadence cadence = selectedDaysRadio.IsChecked == true
                ? BackupScheduleCadence.SelectedDays
                : BackupScheduleCadence.Daily;
            IEnumerable<DayOfWeek> days = cadence == BackupScheduleCadence.SelectedDays
                ? dayChecks.Where(item => item.Value.IsChecked == true).Select(item => item.Key)
                : Enumerable.Empty<DayOfWeek>();
            ScheduleChangeRequest request;
            string error;
            if (!ScheduleChangeRequest.TryCreate(
                cadence,
                time,
                days,
                enabledCheck.IsChecked == true,
                startWhenAvailableCheck.IsChecked == true,
                wakeCheck.IsChecked == true,
                allowBatteryCheck.IsChecked == true,
                finishOnBatteryCheck.IsChecked != true,
                out request,
                out error))
            {
                SetValidation(error);
                return;
            }

            if (installed.Matches(request))
            {
                SetValidation("No schedule settings have changed.");
                return;
            }

            string review = BuildReviewText(request);
            MessageBoxResult confirmed = MessageBox.Show(
                this,
                review,
                "Review schedule changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (confirmed != MessageBoxResult.Yes)
            {
                return;
            }

            RequestedChange = request;
            DialogResult = true;
        }

        private string BuildReviewText(ScheduleChangeRequest request)
        {
            List<string> details = new List<string>();
            details.Add("Automatic backup: " + request.Summary);
            details.Add(request.StartWhenAvailable
                ? "Missed run: Run when the PC is next available"
                : "Missed run: Wait for the next scheduled time");
            details.Add(request.WakeToRun
                ? "Sleep: Wake this PC to run"
                : "Sleep: Do not wake this PC");
            details.Add(request.AllowStartOnBatteries
                ? "Power: May start on battery"
                : "Power: Wait for AC before starting");
            details.Add(request.StopIfGoingOnBatteries
                ? "If unplugged: Windows may stop the task"
                : "If unplugged: Let a running backup finish");
            return "Review schedule changes\n\n" +
                string.Join("\n", details.ToArray()) +
                "\n\nThis updates the protected Windows scheduled task. " +
                "No backup will start immediately.\n\nApply these changes?";
        }

        private void SetValidation(string message)
        {
            validationText.Text = message ?? string.Empty;
            validationText.Visibility = string.IsNullOrWhiteSpace(message)
                ? Visibility.Collapsed
                : Visibility.Visible;
            if (!string.IsNullOrWhiteSpace(message))
            {
                AutomationProperties.SetName(validationText, message);
            }
        }
    }
}
