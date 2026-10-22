using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace PressureMap.Views;

public static class CoursePalette
{
    public static int Index(string name)
    {
        uint hash = 2166136261;
        foreach (var c in name) hash = unchecked((hash ^ c) * 16777619);
        return (int)(hash % 8);
    }
    public static IBrush Brush(string name, bool tint = false) =>
        (IBrush)Application.Current!.FindResource(Application.Current!.ActualThemeVariant, $"Course{Index(name)}{(tint ? "Tint" : "")}Brush")!;
    public static Color Color(string name) => ((ISolidColorBrush)Brush(name)).Color;
}


