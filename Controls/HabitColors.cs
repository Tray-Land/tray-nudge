using Microsoft.UI.Xaml.Media;
using TrayNudge.Core;
using Windows.UI;

namespace TrayNudge.Controls;

/// <summary>Turns <see cref="HabitPalette"/> entries into XAML colors and brushes.</summary>
internal static class HabitColors
{
    public static Color ColorFor(int index, byte alpha = 255)
    {
        uint argb = HabitPalette.ColorFor(index);
        return Color.FromArgb(alpha, (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
    }

    public static SolidColorBrush Brush(int index, double opacity = 1) =>
        new(ColorFor(index, (byte)Math.Round(255 * Math.Clamp(opacity, 0, 1))));

    /// <summary>The habit's own color first, then the rest of the palette for variety.</summary>
    public static IReadOnlyList<Color> ConfettiFor(int index)
    {
        List<Color> colors = [ColorFor(index), ColorFor(index)];
        for (int i = 1; i < HabitPalette.Colors.Count; i += 2)
        {
            colors.Add(ColorFor(index + i));
        }

        return colors;
    }

    public static IReadOnlyList<Color> All() =>
        Enumerable.Range(0, HabitPalette.Colors.Count).Select(i => ColorFor(i)).ToList();
}
