using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using BeProductive.App.ViewModels;

namespace BeProductive.App.Views;

/// <summary>
/// Focus per day (or month) as columns, with each column's goal as a short tick across it.
/// One series, so no legend box: the title says what's plotted and a key below explains the tick.
/// Hovering a column shows its details.
/// </summary>
public sealed class ColumnChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<ChartColumn>?> ColumnsProperty =
        AvaloniaProperty.Register<ColumnChart, IReadOnlyList<ChartColumn>?>(nameof(Columns));

    private const double MaxBarWidth = 24;
    private const double Radius = 4;
    private const double LeftAxis = 44;
    private const double BottomAxis = 22;
    private const double Top = 10;

    private static readonly int[] TickSteps = [15, 30, 60, 120, 240, 480, 960, 1920, 3840, 7680, 15360];

    private int hovered = -1;

    static ColumnChart()
    {
        AffectsRender<ColumnChart>(ColumnsProperty);
    }

    public ColumnChart()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public IReadOnlyList<ChartColumn>? Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColumnsProperty)
        {
            if (change.OldValue is System.Collections.Specialized.INotifyCollectionChanged oldItems)
                oldItems.CollectionChanged -= OnColumnsChanged;
            if (change.NewValue is System.Collections.Specialized.INotifyCollectionChanged newItems)
                newItems.CollectionChanged += OnColumnsChanged;
        }
    }

    public override void Render(DrawingContext context)
    {
        var columns = Columns;
        var plot = new Rect(LeftAxis, Top, Math.Max(0, Bounds.Width - LeftAxis), Math.Max(0, Bounds.Height - Top - BottomAxis));
        if (columns is null || columns.Count == 0 || plot.Width <= 0 || plot.Height <= 0)
            return;

        var series = Brush("ChartSeries", Brushes.SteelBlue);
        var grid = new Pen(Brush("ChartGrid", Brushes.LightGray), 1);
        var text = Brush("ChartText", Brushes.Gray);
        var goalPen = new Pen(Brush("ChartGoal", Brushes.Black), 2, lineCap: PenLineCap.Round);
        var hover = Brush("ChartHover", Brushes.Transparent);

        var maximum = Math.Max(1, columns.Max(column => Math.Max(column.FocusedMinutes, column.GoalMinutes)));
        var step = TickSteps.FirstOrDefault(candidate => maximum / candidate <= 4, TickSteps[^1]);
        var top = Math.Ceiling(maximum / (double)step) * step;
        double Y(double minutes) => plot.Bottom - minutes / top * plot.Height;

        // Gridlines and y labels.
        for (var tick = 0.0; tick <= top; tick += step)
        {
            var y = Math.Round(Y(tick)) + 0.5;
            context.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(context, AxisLabel((int)tick), text, new Point(plot.Left - 8, y), alignRight: true);
        }

        var slot = plot.Width / columns.Count;
        var barWidth = Math.Min(MaxBarWidth, Math.Max(2, slot * 0.6));
        var labelEvery = Math.Max(1, (int)Math.Ceiling(columns.Count / 10.0));

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var centre = plot.Left + slot * (i + 0.5);

            if (i == hovered)
                context.FillRectangle(hover, new Rect(plot.Left + slot * i, plot.Top, slot, plot.Height));

            if (column.FocusedMinutes > 0)
            {
                var height = plot.Bottom - Y(column.FocusedMinutes);
                context.DrawGeometry(series, null, ColumnShape(new Rect(centre - barWidth / 2, plot.Bottom - height, barWidth, height)));
            }

            if (column.GoalMinutes > 0)
            {
                var y = Y(column.GoalMinutes);
                var half = Math.Min(slot * 0.42, barWidth / 2 + 5);
                context.DrawLine(goalPen, new Point(centre - half, y), new Point(centre + half, y));
            }

            if (i % labelEvery == 0 || i == columns.Count - 1)
                DrawText(context, column.Label, text, new Point(centre, plot.Bottom + 12), alignRight: false, centred: true);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var index = IndexAt(e.GetPosition(this));
        if (index == hovered)
            return;

        hovered = index;
        InvalidateVisual();
        if (index >= 0 && Columns is { } columns)
        {
            ToolTip.SetTip(this, columns[index].Tooltip);
            ToolTip.SetIsOpen(this, true);
        }
        else
        {
            ToolTip.SetIsOpen(this, false);
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        hovered = -1;
        ToolTip.SetIsOpen(this, false);
        InvalidateVisual();
    }

    /// <summary>The whole slot is the hit target, not just the bar, so short columns are easy to hover.</summary>
    private int IndexAt(Point point)
    {
        if (Columns is not { Count: > 0 } columns || point.X < LeftAxis || point.Y < Top || point.Y > Bounds.Height)
            return -1;
        var slot = (Bounds.Width - LeftAxis) / columns.Count;
        var index = (int)((point.X - LeftAxis) / slot);
        return index >= 0 && index < columns.Count ? index : -1;
    }

    private void OnColumnsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        hovered = -1;
        InvalidateVisual();
    }

    /// <summary>A column with rounded corners at the data end and square corners on the baseline.</summary>
    private static Geometry ColumnShape(Rect rect)
    {
        var radius = Math.Min(Radius, Math.Min(rect.Width / 2, rect.Height));
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(rect.BottomLeft, isFilled: true);
        context.LineTo(new Point(rect.Left, rect.Top + radius));
        context.ArcTo(new Point(rect.Left + radius, rect.Top), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
        context.LineTo(new Point(rect.Right - radius, rect.Top));
        context.ArcTo(new Point(rect.Right, rect.Top + radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
        context.LineTo(rect.BottomRight);
        context.EndFigure(isClosed: true);
        return geometry;
    }

    private static string AxisLabel(int minutes) =>
        minutes == 0 ? "0" : minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes} min";

    private void DrawText(DrawingContext context, string value, IBrush brush, Point anchor, bool alignRight, bool centred = false)
    {
        var formatted = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, brush);
        var x = alignRight ? anchor.X - formatted.Width : centred ? anchor.X - formatted.Width / 2 : anchor.X;
        context.DrawText(formatted, new Point(x, anchor.Y - formatted.Height / 2));
    }

    private IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback;
}
