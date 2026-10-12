using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GitCross.App.ViewModels;
using GitCross.Core.Diff;
using GitCross.Core.Models;

namespace GitCross.App.Controls;

public static class Converters
{
    private static readonly IBrush[] AvatarPalette =
    [
        new SolidColorBrush(Color.Parse("#4F7DF3")), new SolidColorBrush(Color.Parse("#E5604D")),
        new SolidColorBrush(Color.Parse("#2FA36B")), new SolidColorBrush(Color.Parse("#A064D8")),
        new SolidColorBrush(Color.Parse("#D89A1E")), new SolidColorBrush(Color.Parse("#1F9CB0")),
        new SolidColorBrush(Color.Parse("#D4568F")), new SolidColorBrush(Color.Parse("#6B7A8F")),
    ];

    public static readonly IValueConverter BoldIf = new Func<bool, FontWeight>(b => b ? FontWeight.SemiBold : FontWeight.Normal).AsConverter();

    public static readonly IValueConverter DiffBackground = new Func<DiffLineKind, IBrush>(k => k switch
    {
        DiffLineKind.Added => new SolidColorBrush(Color.FromArgb(0x38, 0x2E, 0xB8, 0x5C)),
        DiffLineKind.Removed => new SolidColorBrush(Color.FromArgb(0x38, 0xE5, 0x48, 0x4D)),
        DiffLineKind.Hunk => new SolidColorBrush(Color.FromArgb(0x30, 0x4F, 0x7D, 0xF3)),
        _ => Brushes.Transparent,
    }).AsConverter();

    public static readonly IValueConverter DiffOpacity = new Func<DiffLineKind, double>(k => k == DiffLineKind.Meta ? 0.5 : 1.0).AsConverter();

    public static readonly IValueConverter DiffSign = new Func<DiffLineKind, string>(k => k switch
    {
        DiffLineKind.Added => "+",
        DiffLineKind.Removed => "−",
        _ => " ",
    }).AsConverter();

    public static readonly IValueConverter StripeBackground = new Func<bool, IBrush>(b => b ? new SolidColorBrush(Color.FromArgb(0x14, 0x80, 0x90, 0xB0)) : Brushes.Transparent).AsConverter();

    public static readonly IValueConverter LineNumber = new Func<int?, string>(n => n?.ToString(CultureInfo.InvariantCulture) ?? "").AsConverter();

    public static readonly IValueConverter ChangeLetter = new Func<ChangeKind, string>(k => k switch
    {
        ChangeKind.Added => "A",
        ChangeKind.Deleted => "D",
        ChangeKind.Renamed => "R",
        ChangeKind.Copied => "C",
        ChangeKind.Untracked => "U",
        ChangeKind.Conflicted => "!",
        ChangeKind.TypeChanged => "T",
        _ => "M",
    }).AsConverter();

    public static readonly IValueConverter ChangeBrush = new Func<ChangeKind, IBrush>(k => new SolidColorBrush(Color.Parse(k switch
    {
        ChangeKind.Added or ChangeKind.Untracked => "#2FA36B",
        ChangeKind.Deleted => "#E5484D",
        ChangeKind.Renamed or ChangeKind.Copied => "#4F7DF3",
        ChangeKind.Conflicted => "#D4568F",
        _ => "#D89A1E",
    }))).AsConverter();

    public static readonly IValueConverter ChipBackground = new Func<RefLabel, IBrush>(l => new SolidColorBrush(Color.Parse(
        l.IsCurrent ? "#4F7DF3" : l.Kind switch
        {
            RefKind.Tag => "#D89A1E",
            RefKind.RemoteBranch => "#6B7A8F",
            _ => "#2FA36B",
        }))).AsConverter();

    public static readonly IValueConverter ChipGlyph = new Func<RefLabel, string>(l => l.IsCurrent ? "● " : l.Kind switch
    {
        RefKind.Tag => "🏷 ",
        RefKind.RemoteBranch => "☁ ",
        _ => "⎇ ",
    }).AsConverter();

    public static readonly IValueConverter AvatarBrush = new Func<string, IBrush>(
        name => AvatarPalette[(name.Aggregate(17, (h, c) => unchecked(h * 31 + c)) & 0x7fffffff) % AvatarPalette.Length]).AsConverter();

    public static readonly IValueConverter SidebarGlyph = new Func<SidebarEntry, string>(e => e.Stash is not null ? "📦" : e.Ref switch
    {
        { IsCurrent: true } => "●",
        { Kind: RefKind.Tag } => "🏷",
        { Kind: RefKind.RemoteBranch } => "☁",
        _ => "⎇",
    }).AsConverter();

    private static IValueConverter AsConverter<TIn, TOut>(this Func<TIn, TOut> f) => new FuncConverter<TIn, TOut>(f);

    private sealed class FuncConverter<TIn, TOut>(Func<TIn, TOut> f) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is TIn v ? f(v) : (value is null && default(TIn) is null ? f(default!) : default(TOut));

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
