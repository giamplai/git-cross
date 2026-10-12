using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GitCross.App.Controls;

public static class Converters
{
    public static readonly IValueConverter BoldIf = new FuncConverter<bool, FontWeight>(b => b ? FontWeight.Bold : FontWeight.Normal);

    public static readonly IValueConverter DiffBackground = new FuncConverter<string, IBrush>(k => k switch
    {
        "add" => new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0xB0, 0x30)),
        "del" => new SolidColorBrush(Color.FromArgb(0x40, 0xE0, 0x20, 0x20)),
        "hunk" => new SolidColorBrush(Color.FromArgb(0x33, 0x20, 0x80, 0xFF)),
        _ => Brushes.Transparent,
    });

    public static readonly IValueConverter DiffOpacity = new FuncConverter<string, double>(k => k == "meta" ? 0.55 : 1.0);

    private sealed class FuncConverter<TIn, TOut>(Func<TIn, TOut> f) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is TIn v ? f(v) : default(TOut);

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
