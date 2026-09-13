using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ResticBackuper.Dashboard
{
    /// <summary>
    /// Shared Beautiful UI presentation and motion primitives. This class deliberately owns
    /// appearance only: protected commands, state transitions, focus, and dialog results remain
    /// synchronous in the controls that use it.
    /// </summary>
    internal static class DashboardVisualStyle
    {
        private static readonly KeySpline DefaultSpline = new KeySpline(0.4, 0.0, 0.2, 1.0);
        private static readonly KeySpline StrongSpline = new KeySpline(0.23, 1.0, 0.32, 1.0);

        public static readonly TimeSpan FastDuration = TimeSpan.FromMilliseconds(100);
        public static readonly TimeSpan DefaultDuration = TimeSpan.FromMilliseconds(150);
        public static readonly TimeSpan StrongDuration = TimeSpan.FromMilliseconds(220);
        public static readonly TimeSpan PopDuration = TimeSpan.FromMilliseconds(240);
        public static readonly TimeSpan ExpandDuration = TimeSpan.FromMilliseconds(340);
        public static readonly TimeSpan SectionDuration = TimeSpan.FromMilliseconds(600);

        public static FontFamily UiFont
        {
            get { return new FontFamily("Segoe UI Variable Text"); }
        }

        public static bool MotionAllowed()
        {
            return SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
        }

        public static void ApplyWindow(Window window, DashboardThemePalette palette)
        {
            if (window == null || palette == null)
            {
                return;
            }

            window.FontFamily = UiFont;
            window.Background = palette.BackgroundTop;
            window.Foreground = palette.TextPrimary;
            window.UseLayoutRounding = true;
            window.SnapsToDevicePixels = true;
            window.Resources[SystemColors.HighlightBrushKey] = palette.Selection;
            window.Resources[SystemColors.HighlightTextBrushKey] = palette.SelectionText;
            window.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = palette.Selection;
            window.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = palette.SelectionText;
            TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(window, TextRenderingMode.Auto);
            RenderOptions.SetClearTypeHint(window, ClearTypeHint.Enabled);
        }

        public static void ApplyButtonChrome(Button button, double cornerRadius)
        {
            if (button == null)
            {
                return;
            }

            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Chrome";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(cornerRadius));
            border.SetBinding(
                Border.BackgroundProperty,
                new Binding("Background")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                });
            border.SetBinding(
                Border.BorderBrushProperty,
                new Binding("BorderBrush")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                });
            border.SetBinding(
                Border.BorderThicknessProperty,
                new Binding("BorderThickness")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                });
            border.SetBinding(
                Border.PaddingProperty,
                new Binding("Padding")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                });

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetBinding(
                FrameworkElement.HorizontalAlignmentProperty,
                new Binding("HorizontalContentAlignment")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                });
            presenter.SetBinding(
                FrameworkElement.VerticalAlignmentProperty,
                new Binding("VerticalContentAlignment")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                });
            presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            presenter.SetBinding(
                ContentPresenter.ContentProperty,
                new Binding("Content")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                });
            border.AppendChild(presenter);
            template.VisualTree = border;

            Style style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            button.Style = style;

            ScaleTransform scale = new ScaleTransform(1.0, 1.0);
            button.RenderTransformOrigin = new Point(0.5, 0.5);
            button.RenderTransform = scale;

            Action<bool> setPressed = delegate(bool pressed)
            {
                double target = pressed && button.IsEnabled && MotionAllowed() ? 0.96 : 1.0;
                TimeSpan duration = pressed ? FastDuration : DefaultDuration;
                AnimateDouble(scale, ScaleTransform.ScaleXProperty, target, duration, false);
                AnimateDouble(scale, ScaleTransform.ScaleYProperty, target, duration, false);
            };
            Action updateOpacity = delegate
            {
                double target = !button.IsEnabled
                    ? (SystemParameters.HighContrast ? 1.0 : 0.46)
                    : SystemParameters.HighContrast
                        ? 1.0
                        : button.IsMouseOver ? 0.94 : 1.0;
                AnimateDouble(button, UIElement.OpacityProperty, target, DefaultDuration, false);
            };

            button.MouseEnter += delegate { updateOpacity(); };
            button.MouseLeave += delegate
            {
                setPressed(false);
                updateOpacity();
            };
            button.PreviewMouseDown += delegate(object sender, MouseButtonEventArgs args)
            {
                if (args.ChangedButton == MouseButton.Left)
                {
                    setPressed(true);
                }
            };
            button.PreviewMouseUp += delegate(object sender, MouseButtonEventArgs args)
            {
                if (args.ChangedButton == MouseButton.Left)
                {
                    setPressed(false);
                }
            };
            button.PreviewKeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.Key == Key.Space || args.Key == Key.Enter || args.Key == Key.Return)
                {
                    setPressed(true);
                }
            };
            button.PreviewKeyUp += delegate(object sender, KeyEventArgs args)
            {
                if (args.Key == Key.Space || args.Key == Key.Enter || args.Key == Key.Return)
                {
                    setPressed(false);
                }
            };
            button.IsEnabledChanged += delegate
            {
                setPressed(false);
                updateOpacity();
            };
            updateOpacity();
        }

        public static void ApplyFocusOutline(Button button, Brush focusBrush)
        {
            if (button == null || focusBrush == null)
            {
                return;
            }

            Brush borderBeforeFocus = null;
            Thickness thicknessBeforeFocus = new Thickness(1);
            button.GotKeyboardFocus += delegate
            {
                borderBeforeFocus = button.BorderBrush;
                thicknessBeforeFocus = button.BorderThickness;
                button.BorderBrush = focusBrush;
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
        }

        public static void ApplyDataGridChrome(DataGrid grid, DashboardThemePalette palette)
        {
            if (grid == null || palette == null)
            {
                return;
            }

            Style headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, palette.SurfaceSoft));
            headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, palette.TextSecondary));
            headerStyle.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
            headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, palette.Border));
            headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 9, 10, 9)));
            headerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            grid.ColumnHeaderStyle = headerStyle;
            grid.FontSize = 12.0;
            grid.MinRowHeight = 34;
            grid.CanUserResizeRows = false;
            grid.HorizontalGridLinesBrush = palette.Border;
            grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        }

        public static void Reveal(FrameworkElement element)
        {
            Reveal(element, SectionDuration, TimeSpan.Zero, 8.0);
        }

        public static void Reveal(
            FrameworkElement element,
            TimeSpan duration,
            TimeSpan delay,
            double offset)
        {
            if (element == null)
            {
                return;
            }
            if (!MotionAllowed())
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = 1.0;
                TranslateTransform immediate = EnsureTranslate(element);
                immediate.BeginAnimation(TranslateTransform.YProperty, null);
                immediate.Y = 0.0;
                return;
            }

            TranslateTransform translate = EnsureTranslate(element);
            element.BeginAnimation(UIElement.OpacityProperty, null);
            translate.BeginAnimation(TranslateTransform.YProperty, null);
            element.Opacity = 0.0;
            translate.Y = offset;
            element.Dispatcher.BeginInvoke(
                new Action(delegate
                {
                    AnimateDouble(element, UIElement.OpacityProperty, 1.0, duration, true, delay);
                    AnimateDouble(translate, TranslateTransform.YProperty, 0.0, duration, true, delay);
                }),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        public static void PopIn(FrameworkElement element)
        {
            if (element == null)
            {
                return;
            }
            if (!MotionAllowed())
            {
                element.Opacity = 1.0;
                return;
            }

            ScaleTransform scale = new ScaleTransform(0.95, 0.95);
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = scale;
            element.Opacity = 0.0;
            element.Dispatcher.BeginInvoke(
                new Action(delegate
                {
                    AnimateDouble(element, UIElement.OpacityProperty, 1.0, PopDuration, true);
                    AnimateDouble(scale, ScaleTransform.ScaleXProperty, 1.0, PopDuration, true);
                    AnimateDouble(scale, ScaleTransform.ScaleYProperty, 1.0, PopDuration, true);
                }),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        public static void Emphasize(FrameworkElement element)
        {
            if (element == null || !MotionAllowed())
            {
                return;
            }

            ScaleTransform scale = element.RenderTransform as ScaleTransform;
            if (scale == null)
            {
                scale = new ScaleTransform(0.985, 0.985);
                element.RenderTransformOrigin = new Point(0.5, 0.5);
                element.RenderTransform = scale;
            }
            else
            {
                scale.ScaleX = 0.985;
                scale.ScaleY = 0.985;
            }
            AnimateDouble(scale, ScaleTransform.ScaleXProperty, 1.0, StrongDuration, true);
            AnimateDouble(scale, ScaleTransform.ScaleYProperty, 1.0, StrongDuration, true);
        }

        public static void ApplyDialogEntrance(Window window)
        {
            if (window == null)
            {
                return;
            }
            FrameworkElement content = window.Content as FrameworkElement;
            if (content == null || !MotionAllowed())
            {
                return;
            }
            PopIn(content);
        }

        public static void AnimateProgressValue(ProgressBar progress, double value)
        {
            if (progress == null)
            {
                return;
            }
            double target = Math.Max(progress.Minimum, Math.Min(progress.Maximum, value));
            AnimateDouble(progress, ProgressBar.ValueProperty, target, TimeSpan.FromMilliseconds(250), false);
        }

        public static void SetBusy(ProgressBar progress, bool busy)
        {
            if (progress == null)
            {
                return;
            }
            if (!busy)
            {
                progress.IsIndeterminate = false;
                AutomationProperties.SetItemStatus(progress, string.Empty);
                return;
            }
            progress.IsIndeterminate = MotionAllowed();
            if (!progress.IsIndeterminate)
            {
                progress.Value = progress.Minimum;
            }
            AutomationProperties.SetItemStatus(progress, "Busy; progress is indeterminate");
        }

        public static void AnimateDouble(
            DependencyObject target,
            DependencyProperty property,
            double value,
            TimeSpan duration,
            bool strong)
        {
            AnimateDouble(target, property, value, duration, strong, TimeSpan.Zero);
        }

        public static void AnimateDouble(
            DependencyObject target,
            DependencyProperty property,
            double value,
            TimeSpan duration,
            bool strong,
            TimeSpan delay)
        {
            Animatable animatable = target as Animatable;
            if (animatable == null)
            {
                target.SetValue(property, value);
                return;
            }

            double current = Convert.ToDouble(target.GetValue(property));
            animatable.BeginAnimation(property, null);
            target.SetValue(property, value);
            if (!MotionAllowed() || duration <= TimeSpan.Zero)
            {
                return;
            }

            DoubleAnimationUsingKeyFrames animation = new DoubleAnimationUsingKeyFrames();
            animation.BeginTime = delay;
            animation.Duration = new Duration(duration);
            animation.FillBehavior = FillBehavior.Stop;
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(current, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(
                new SplineDoubleKeyFrame(
                    value,
                    KeyTime.FromTimeSpan(duration),
                    strong ? StrongSpline : DefaultSpline));
            animatable.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private static TranslateTransform EnsureTranslate(FrameworkElement element)
        {
            TranslateTransform translate = element.RenderTransform as TranslateTransform;
            if (translate == null)
            {
                translate = new TranslateTransform();
                element.RenderTransform = translate;
            }
            return translate;
        }
    }
}
