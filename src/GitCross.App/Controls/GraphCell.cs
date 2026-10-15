using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GitCross.Core.Graph;

namespace GitCross.App.Controls;

/// <summary>Draws the commit graph segment for one history row.</summary>
public sealed class GraphCell : Control
{
    public const double LaneWidth = 14;
    public const double RowHeight = 24;

    private static readonly IBrush[] Palette =
    [
        Brushes.DodgerBlue, Brushes.OrangeRed, Brushes.SeaGreen, Brushes.MediumOrchid,
        Brushes.Goldenrod, Brushes.Teal, Brushes.HotPink, Brushes.SlateGray,
    ];

    public static readonly StyledProperty<GraphRow?> RowProperty =
        AvaloniaProperty.Register<GraphCell, GraphRow?>(nameof(Row));

    static GraphCell()
    {
        AffectsRender<GraphCell>(RowProperty);
        ClipToBoundsProperty.OverrideDefaultValue<GraphCell>(true);
    }

    public GraphRow? Row
    {
        get => GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
        => new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, RowHeight);

    public override void Render(DrawingContext ctx)
    {
        if (Row is not { } row)
        {
            return;
        }

        double X(int lane) => (lane + 0.5) * LaneWidth + 2;
        double mid = Bounds.Height / 2, bottom = Bounds.Height;

        foreach (var e in row.Edges)
        {
            var pen = new Pen(Palette[e.ColorIndex % Palette.Length], 2);
            if (e.From == e.To)
            {
                // Lane continuing through the row; own-lane edge only draws the lower half.
                var top = e.From == row.Lane ? mid : 0;
                ctx.DrawLine(pen, new Point(X(e.From), top), new Point(X(e.To), bottom));
            }
            else if (e.To == row.Lane)
            {
                ctx.DrawLine(pen, new Point(X(e.From), 0), new Point(X(e.To), mid)); // merge-in from above
            }
            else
            {
                ctx.DrawLine(pen, new Point(X(e.From), mid), new Point(X(e.To), bottom)); // fork-out below
            }
        }

        if (row.HasIncoming)
        {
            // Upper half of the commit's own lane (line coming from a child above).
            ctx.DrawLine(new Pen(Palette[row.ColorIndex % Palette.Length], 2),
                new Point(X(row.Lane), 0), new Point(X(row.Lane), mid));
        }

        ctx.DrawEllipse(Palette[row.ColorIndex % Palette.Length], new Pen(new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), 1.5),
            new Point(X(row.Lane), mid), 5, 5);
    }
}
