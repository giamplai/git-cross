using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using GitCross.Core.Diff;

namespace GitCross.App.Controls;

/// <summary>Text of one diff line; the edited characters (found by intra-line comparison) get a stronger background.</summary>
public sealed class DiffText : TextBlock
{
    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<DiffText, string?>(nameof(Source));
    public static readonly StyledProperty<int> StartProperty = AvaloniaProperty.Register<DiffText, int>(nameof(Start));
    public static readonly StyledProperty<int> LengthProperty = AvaloniaProperty.Register<DiffText, int>(nameof(Length));
    public static readonly StyledProperty<DiffLineKind> KindProperty = AvaloniaProperty.Register<DiffText, DiffLineKind>(nameof(Kind));

    private static readonly IBrush AddedStrong = new SolidColorBrush(Color.FromArgb(0x80, 0x2E, 0xB8, 0x5C));
    private static readonly IBrush RemovedStrong = new SolidColorBrush(Color.FromArgb(0x80, 0xE5, 0x48, 0x4D));

    public string? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    public int Start { get => GetValue(StartProperty); set => SetValue(StartProperty, value); }

    public int Length { get => GetValue(LengthProperty); set => SetValue(LengthProperty, value); }

    public DiffLineKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty || change.Property == StartProperty
            || change.Property == LengthProperty || change.Property == KindProperty)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        var text = Source ?? "";
        if (Length <= 0 || Start < 0 || Start + Length > text.Length)
        {
            Inlines?.Clear();
            Text = text;
            return;
        }

        Text = null;
        Inlines ??= [];
        Inlines.Clear();
        Inlines.Add(new Run(text[..Start]));
        Inlines.Add(new Run(text.Substring(Start, Length)) { Background = Kind == DiffLineKind.Added ? AddedStrong : RemovedStrong });
        Inlines.Add(new Run(text[(Start + Length)..]));
    }
}
