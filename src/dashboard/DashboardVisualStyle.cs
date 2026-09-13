using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace ResticBackuper.Dashboard
{
    /// <summary>
    /// Shared presentation primitives for the native dashboard and its protected workflow windows.
    /// This class intentionally owns visuals only; command wiring, state, and safety gates stay with
    /// the controls that use it.
    /// </summary>
    internal static class DashboardVisualStyle
    {
        public static FontFamily UiFont
        {
            get { return new FontFamily("Segoe UI Variable Text"); }
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
            presenter.SetValue(
                FrameworkElement.HorizontalAlignmentProperty,
                HorizontalAlignment.Center);
            presenter.SetValue(
                FrameworkElement.VerticalAlignmentProperty,
                VerticalAlignment.Center);
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

            Trigger hover = new Trigger();
            hover.Property = UIElement.IsMouseOverProperty;
            hover.Value = true;
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.90));
            style.Triggers.Add(hover);

            Trigger pressed = new Trigger();
            pressed.Property = ButtonBase.IsPressedProperty;
            pressed.Value = true;
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.72));
            style.Triggers.Add(pressed);

            Trigger disabled = new Trigger();
            disabled.Property = UIElement.IsEnabledProperty;
            disabled.Value = false;
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.46));
            style.Triggers.Add(disabled);
            button.Style = style;
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
            headerStyle.Setters.Add(new Setter(Control.FontSizeProperty, 11.0));
            headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
            headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, palette.Border));
            headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 7, 8, 7)));
            headerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            grid.ColumnHeaderStyle = headerStyle;
        }
    }
}
