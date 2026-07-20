using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace ResticBackuper.Dashboard
{
    public sealed class RunChart : FrameworkElement
    {
        private IList<RunMetricView> runs = new List<RunMetricView>();
        private readonly Typeface typeface = new Typeface("Segoe UI");
        private readonly Brush muted = BrushFrom("#8190A8");
        private readonly Brush grid = BrushFrom("#243149");
        private readonly Brush durationBrush = BrushFrom("#2DD4BF");
        private readonly Brush processedBrush = BrushFrom("#60A5FA");
        private readonly Brush successBrush = BrushFrom("#34D399");
        private readonly Brush failureBrush = BrushFrom("#FB7185");

        public IList<RunMetricView> Runs
        {
            get { return runs; }
            set
            {
                runs = value ?? new List<RunMetricView>();
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            double width = Math.Max(0, ActualWidth);
            double height = Math.Max(0, ActualHeight);
            if (width < 80 || height < 80)
            {
                return;
            }

            IList<RunMetricView> visible = runs
                .OrderBy(item => item.StartedLocal)
                .TakeLastCompat(10)
                .ToList();

            if (visible.Count == 0)
            {
                DrawText(drawingContext, "Run trends will appear here", 14, muted, 22, height / 2 - 10);
                DrawText(drawingContext, "The verified dry run seeds the first estimate.", 11, muted, 22, height / 2 + 14);
                return;
            }

            const double left = 44;
            const double right = 20;
            const double top = 30;
            const double bottom = 34;
            Rect plot = new Rect(left, top, Math.Max(1, width - left - right), Math.Max(1, height - top - bottom));

            double maxDuration = Math.Max(1, visible.Max(item => item.DurationSeconds));
            double maxProcessed = Math.Max(1, visible.Max(item => item.ProcessedBytes));
            Pen gridPen = new Pen(grid, 1);
            gridPen.DashStyle = new DashStyle(new double[] { 3, 4 }, 0);

            for (int row = 0; row <= 3; row++)
            {
                double y = plot.Top + plot.Height * row / 3.0;
                drawingContext.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                double seconds = maxDuration * (3 - row) / 3.0;
                DrawText(drawingContext, ShortDuration(seconds), 9, muted, 2, y - 7);
            }

            double slot = plot.Width / visible.Count;
            double barWidth = Math.Max(8, Math.Min(30, slot * 0.42));
            List<Point> processedPoints = new List<Point>();
            for (int index = 0; index < visible.Count; index++)
            {
                RunMetricView item = visible[index];
                double center = plot.Left + slot * index + slot / 2;
                double barHeight = Math.Max(2, plot.Height * item.DurationSeconds / maxDuration);
                Rect bar = new Rect(center - barWidth / 2, plot.Bottom - barHeight, barWidth, barHeight);
                Brush barBrush = item.Success ? durationBrush : failureBrush;
                drawingContext.DrawRoundedRectangle(barBrush, null, bar, 4, 4);

                double pointY = plot.Bottom - plot.Height * item.ProcessedBytes / maxProcessed;
                processedPoints.Add(new Point(center, pointY));

                string label = item.StartedLocal.ToString("ddd", CultureInfo.CurrentCulture);
                DrawCenteredText(drawingContext, label, 9, muted, center, plot.Bottom + 9);

                Brush dotBrush = item.Success ? successBrush : failureBrush;
                drawingContext.DrawEllipse(dotBrush, null, new Point(center, plot.Bottom + 2), 2.5, 2.5);
            }

            if (processedPoints.Count > 1)
            {
                StreamGeometry geometry = new StreamGeometry();
                using (StreamGeometryContext context = geometry.Open())
                {
                    context.BeginFigure(processedPoints[0], false, false);
                    context.PolyLineTo(processedPoints.Skip(1).ToList(), true, true);
                }
                geometry.Freeze();
                drawingContext.DrawGeometry(null, new Pen(processedBrush, 2), geometry);
            }
            foreach (Point point in processedPoints)
            {
                drawingContext.DrawEllipse(processedBrush, new Pen(BrushFrom("#0C1424"), 2), point, 4, 4);
            }

            drawingContext.DrawRoundedRectangle(durationBrush, null, new Rect(plot.Left, 7, 10, 10), 2, 2);
            DrawText(drawingContext, "Duration", 10, muted, plot.Left + 15, 4);
            drawingContext.DrawLine(new Pen(processedBrush, 2), new Point(plot.Left + 82, 12), new Point(plot.Left + 96, 12));
            DrawText(drawingContext, "Processed", 10, muted, plot.Left + 102, 4);
        }

        private void DrawText(DrawingContext context, string text, double size, Brush brush, double x, double y)
        {
            FormattedText formatted = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                size,
                brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            context.DrawText(formatted, new Point(x, y));
        }

        private void DrawCenteredText(DrawingContext context, string text, double size, Brush brush, double x, double y)
        {
            FormattedText formatted = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                size,
                brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            context.DrawText(formatted, new Point(x - formatted.Width / 2, y));
        }

        private static string ShortDuration(double seconds)
        {
            if (seconds >= 3600)
            {
                return (seconds / 3600.0).ToString("0.#", CultureInfo.InvariantCulture) + "h";
            }
            if (seconds >= 60)
            {
                return (seconds / 60.0).ToString("0", CultureInfo.InvariantCulture) + "m";
            }
            return seconds.ToString("0", CultureInfo.InvariantCulture) + "s";
        }

        private static Brush BrushFrom(string value)
        {
            SolidColorBrush brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            return brush;
        }
    }

    internal static class EnumerableCompatibility
    {
        public static IEnumerable<T> TakeLastCompat<T>(this IEnumerable<T> source, int count)
        {
            Queue<T> queue = new Queue<T>();
            foreach (T item in source)
            {
                if (queue.Count == count)
                {
                    queue.Dequeue();
                }
                queue.Enqueue(item);
            }
            return queue;
        }
    }
}
