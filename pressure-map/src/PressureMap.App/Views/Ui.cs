using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using PressureMap.Localization;

namespace PressureMap.Views;

public static class Ui
{
    private static readonly IDataTemplate CenteredButtonText = new FuncDataTemplate<string>((text, _) => new TextBlock
    {
        Name = "CenteredButtonLabel", Text = text, TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap, FontFeatures = new FontFeatureCollection { FontFeature.Parse("tnum") }
    });
    public static void CenterButtonText(Button button)
    {
        void Update()
        {
            // Template-generated strings do not match a plain `Button TextBlock`
            // selector. Own the text template, while retaining Content as string.
            var text = button.Content is string;
            button.Classes.Set("centered-content", text);
            button.Classes.Set("centered-control-content", button.Content is Control && button.HorizontalContentAlignment == HorizontalAlignment.Center);
            button.ContentTemplate = text ? CenteredButtonText : null;
        }
        button.PropertyChanged += (_, args) => { if (args.Property == ContentControl.ContentProperty || args.Property == ContentControl.HorizontalContentAlignmentProperty) Update(); };
        Update();
    }
    public static IBrush Brush(string key) => (IBrush)Application.Current!.FindResource(Application.Current!.ActualThemeVariant, key)!;
    public static Control EmptyState(string icon, string title, string hint, string? action = null, Action? callback = null)
    {
        var glyph = Icon(icon); glyph.Width = glyph.Height = 24; glyph.Foreground = Brush("AccentBrush");
        var panel = Stack(8, new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(999), Background = Brush("AccentSoftBrush"), Child = glyph, HorizontalAlignment = HorizontalAlignment.Center }, Text(title, "h2"), Text(hint, "caption"));
        panel.HorizontalAlignment = HorizontalAlignment.Center; panel.VerticalAlignment = VerticalAlignment.Center; panel.Margin = new Thickness(16, 24);
        foreach (var text in panel.Children.OfType<TextBlock>()) { text.TextAlignment = TextAlignment.Center; text.MaxWidth = 360; }
        if (action is not null && callback is not null) { var button = Button(action, callback, "tonal"); button.HorizontalAlignment = HorizontalAlignment.Center; panel.Children.Add(button); }
        return panel;
    }
    public static Button IconButton(string icon, string tooltip, Action callback)
    {
        var button = Button("", callback, "icon"); button.Content = Icon(icon);
        ToolTip.SetTip(button, Loc.T(tooltip)); Avalonia.Automation.AutomationProperties.SetName(button, Loc.T(tooltip));
        return button;
    }
    public static TextBlock Text(string text, string? classes = null)
    { var control = new TextBlock { Text = Loc.T(text), VerticalAlignment = VerticalAlignment.Center, FontFeatures = new FontFeatureCollection { FontFeature.Parse("tnum") } }; if (classes is not null) control.Classes.Add(classes); return control; }
    public static StackPanel Stack(params Control[] controls) { var panel = new StackPanel { Spacing = 12 }; foreach (var child in controls) panel.Children.Add(child); return panel; }
    public static StackPanel Stack(double spacing, params Control[] controls) { var panel = new StackPanel { Spacing = spacing }; foreach (var child in controls) panel.Children.Add(child); return panel; }
    /// <summary>标签在左、控件在右的一行式设置项；说明文字跟在标签下方。</summary>
    public static Control SettingRow(string label, Control control, string? caption = null)
    {
        var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Text(label));
        if (caption is not null) { var note = Text(caption, "caption"); note.TextWrapping = TextWrapping.Wrap; text.Children.Add(note); }
        control.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 24, MinHeight = 56 };
        grid.Children.Add(text);
        Grid.SetColumn(control, 1); grid.Children.Add(control);
        return grid;
    }
    public static WrapPanel Actions(params Control[] controls)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in controls) { control.HorizontalAlignment = HorizontalAlignment.Left; control.Margin = new Thickness(0, 0, 8, 8); panel.Children.Add(control); }
        return panel;
    }
    public static StackPanel Row(params Control[] controls) { var panel = Stack(controls); panel.Orientation = Orientation.Horizontal; return panel; }
    public static Border Card(Control child) { var card = new Border { Child = child }; card.Classes.Add("card"); return card; }
    public static Button Button(string label, Action callback, string? classes = null)
    {
        var button = new Button { Content = Loc.T(label), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        CenterButtonText(button);
        if (classes is not null) button.Classes.Add(classes);
        button.Click += (_, _) => callback(); return button;
    }
    public static Button AsyncButton(string label, Func<Task> callback, string? classes = null)
    {
        var button = Button(label, () => { }, classes);
        button.Click += async (_, _) => { button.IsEnabled = false; try { await callback(); } finally { button.IsEnabled = true; } }; return button;
    }
    public static TextBox Input(string text = "", string? placeholder = null, bool multiline = false)
        => new() { Text = text, PlaceholderText = placeholder is null ? null : Loc.T(placeholder), AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 100 : 40, VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center };
    public static Control Field(string title, Control input) => Stack(Text(title, "label"), input);
    public static ToggleSwitch Switch(string label, bool value, bool showLabel = true)
    {
        // 行式设置项把标题写在左边，开关自身就不再重复一遍（仍保留给读屏与自检使用）。
        var toggle = new ToggleSwitch { Content = showLabel ? Loc.T(label) : null, IsChecked = value, OnContent = null, OffContent = null };
        Avalonia.Automation.AutomationProperties.SetName(toggle, Loc.T(label));
        return toggle;
    }
    public static Button Link(string label, Action callback)
    {
        var button = Button(label, callback, "link");
        button.HorizontalAlignment = HorizontalAlignment.Left;
        return button;
    }
    public static Border Avatar(string text, double size = 32)
    {
        var label = new TextBlock { Text = text, FontSize = size * .4, FontWeight = FontWeight.Bold, Foreground = Brush("OnAccentBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = Brush("AccentBrush"), Child = label, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    }
    private static IImage? defaultAvatar;
    public static Image AvatarImage(double size = 34)
        => new()
        {
            Width = size, Height = size, Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            Source = defaultAvatar ??= new Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://PressureMap/Assets/default-avatar.png"), null))
        };
    public static Border Divider() { var line = new Border { Height = 1, Margin = new Thickness(0, 4, 0, 4) }; line.Bind(Border.BackgroundProperty, line.GetResourceObservable("DividerBrush")); return line; }
    public static IconView Icon(string key) => new(key);
    public static Grid Columns(string definitions, params Control[] controls)
    { var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(definitions) }; for (var i = 0; i < controls.Length; i++) { Grid.SetColumn(controls[i], i); grid.Children.Add(controls[i]); } return grid; }
    public static ScrollViewer Scroll(Control content) => new() { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
}

