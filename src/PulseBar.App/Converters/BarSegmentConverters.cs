using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PulseBar.Core.Services;

namespace PulseBar.App.Converters;

/// <summary>Foreground per segment kind, tuned for the dark bar background.</summary>
public sealed class SegmentKindToBrushConverter : IValueConverter
{
    private static SolidColorBrush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private static readonly SolidColorBrush LabelBrush = Frozen(0x8C, 0xFF, 0xFF, 0xFF);
    private static readonly SolidColorBrush ValueBrush = Frozen(0xFF, 0xFF, 0xFF, 0xFF);
    private static readonly SolidColorBrush WarningBrush = Frozen(0xFF, 0xFF, 0xD5, 0x4F);
    private static readonly SolidColorBrush HighBrush = Frozen(0xFF, 0xFF, 0xA0, 0x50);
    private static readonly SolidColorBrush CriticalBrush = Frozen(0xFF, 0xFF, 0x5C, 0x5C);
    private static readonly SolidColorBrush SeparatorBrush = Frozen(0x4D, 0xFF, 0xFF, 0xFF);
    private static readonly SolidColorBrush ClaudeBrush = Frozen(0xFF, 0xE8, 0x9C, 0x7D);
    private static readonly SolidColorBrush CodexBrush = Frozen(0xFF, 0x6F, 0xD3, 0xB8);
    private static readonly SolidColorBrush DownBrush = Frozen(0xFF, 0x7B, 0xD8, 0x8F);
    private static readonly SolidColorBrush UpBrush = Frozen(0xFF, 0x6F, 0xB6, 0xFF);
    private static readonly SolidColorBrush StaleBrush = Frozen(0x80, 0xFF, 0xFF, 0xFF);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            BarSegmentKind.Label => LabelBrush,
            BarSegmentKind.Value => ValueBrush,
            BarSegmentKind.ValueWarning => WarningBrush,
            BarSegmentKind.ValueHigh => HighBrush,
            BarSegmentKind.ValueCritical => CriticalBrush,
            BarSegmentKind.Separator => SeparatorBrush,
            BarSegmentKind.ProviderClaude => ClaudeBrush,
            BarSegmentKind.ProviderCodex => CodexBrush,
            BarSegmentKind.Down => DownBrush,
            BarSegmentKind.Up => UpBrush,
            BarSegmentKind.Stale => StaleBrush,
            _ => ValueBrush,
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Values pop; labels and separators stay light.</summary>
public sealed class SegmentKindToFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            BarSegmentKind.ValueCritical => FontWeights.Bold,
            BarSegmentKind.Value or BarSegmentKind.ValueWarning or BarSegmentKind.ValueHigh
                or BarSegmentKind.ProviderClaude or BarSegmentKind.ProviderCodex
                or BarSegmentKind.Down or BarSegmentKind.Up => FontWeights.SemiBold,
            _ => FontWeights.Normal,
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
