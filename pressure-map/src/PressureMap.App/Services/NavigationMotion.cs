using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PressureMap.Services;

/// <summary>ListBoxItem has selection but no Button-style pressed state.</summary>
public sealed class NavigationMotion : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<NavigationMotion, ListBoxItem, bool>("Enabled");

    static NavigationMotion()
    {
        EnabledProperty.Changed.AddClassHandler<ListBoxItem>((item, change) =>
        {
            if (item.GetValue(EnabledProperty))
            {
                item.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel, true);
                item.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel, true);
                item.PointerExited += Exited;
                item.PointerCaptureLost += CaptureLost;
                item.DetachedFromVisualTree += Detached;
            }
            else
            {
                item.RemoveHandler(InputElement.PointerPressedEvent, Pressed);
                item.RemoveHandler(InputElement.PointerReleasedEvent, Released);
                item.PointerExited -= Exited;
                item.PointerCaptureLost -= CaptureLost;
                item.DetachedFromVisualTree -= Detached;
                item.Classes.Remove("nav-pressed");
            }
        });
    }

    public static bool GetEnabled(ListBoxItem item) => item.GetValue(EnabledProperty);
    public static void SetEnabled(ListBoxItem item, bool value) => item.SetValue(EnabledProperty, value);

    private static void Pressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is ListBoxItem item && args.GetCurrentPoint(item).Properties.IsLeftButtonPressed)
            item.Classes.Add("nav-pressed");
    }

    private static void Released(object? sender, PointerReleasedEventArgs args) => Reset(sender);
    private static void Exited(object? sender, PointerEventArgs args) => Reset(sender);
    private static void CaptureLost(object? sender, PointerCaptureLostEventArgs args) => Reset(sender);
    private static void Detached(object? sender, VisualTreeAttachmentEventArgs args) => Reset(sender);
    private static void Reset(object? sender)
    {
        if (sender is ListBoxItem item) item.Classes.Remove("nav-pressed");
    }
}

