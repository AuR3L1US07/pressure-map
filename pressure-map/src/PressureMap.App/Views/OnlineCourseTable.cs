using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using PressureMap.Localization;

namespace PressureMap.Views;

public sealed record OnlineCourseRow(int Order, string Name, double? Required, double? Done, double? Remaining, double? Percent, string Status, string Updated, string Reason)
{
    public string RequiredText => Format(Required);
    public string DoneText => Format(Done);
    public string RemainingText => Format(Remaining);
    public string PercentText => Percent is { } value ? $"{value:0}%" : "—";
    private static string Format(double? value) => value?.ToString("0.#", CultureInfo.InvariantCulture) ?? "待确认";
    public static OnlineCourseRow Read(JsonNode course, string category, int index)
    {
        double? Number(JsonNode? value) => double.TryParse(value?.ToJsonString(), CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) && number >= 0 ? number : null;
        double? required = null, done = null;
        var reason = "";
        if (category == "primary")
        {
            if (course["counts"] is JsonArray counts && counts.Count == 2) { done = Number(counts[0]); required = Number(counts[1]); }
            reason = course["error"]?.GetValue<string>() ?? "";
        }
        else
        {
            var metric = category == "visits" ? course["visits"] : course["metrics"]?[category];
            required = Number(metric?["required"]); done = Number(metric?["done"]);
            reason = metric?["error"]?.GetValue<string>() ?? "";
        }
        var known = required is not null && done is not null && reason.Length == 0;
        double? percent = known && required > 0 ? Math.Clamp(done!.Value / required!.Value * 100, 0, 100) : null;
        double? remaining = known ? Math.Max(0, required!.Value - done!.Value) : null;
        var status = !known ? "待确认" : required == 0 ? "无此要求" : remaining == 0 ? "已完成" : "待完成";
        var updated = course["updated"]?.GetValue<string>() ?? "尚未读取";
        if (!known && reason.Length == 0) reason = "该项要求或完成数量尚未读取，请重新读取或到课程平台核对。";
        if (required == 0 && reason.Length == 0) reason = "平台未设置该项数量要求，不计入完成度。";
        return new(index, course["name"]?.GetValue<string>() ?? "未命名课程", required, done, remaining, percent, status, updated, reason);
    }
}

public sealed class OnlineCourseTable : Grid
{
    // One shared left inset for text columns and one right inset for numbers keeps every
    // header label sitting exactly above its values.
    private const double TextInset = 12;
    private const double CourseNameInset = TextInset + 8 + 12;
    private const double NumberInset = 16;

    private readonly StackPanel categories = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly WrapPanel summary = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel emptyState = new() { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock emptyTitle = new() { FontSize = 14, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock emptyHint = new() { FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, MaxWidth = 380, TextWrapping = TextWrapping.Wrap };
    private readonly Border card;
    private readonly TextBlock detail = Ui.Text("", "muted");
    public DataGrid Table { get; } = new()
    {
        AutoGenerateColumns = false, IsReadOnly = true, CanUserSortColumns = true,
        CanUserResizeColumns = true, CanUserReorderColumns = true,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, HeadersVisibility = DataGridHeadersVisibility.Column,
        RowHeight = 56, ColumnHeaderHeight = 40, BorderThickness = new Thickness(0), FrozenColumnCount = 1, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
    };
    public string Category { get; private set; } = "primary";
    public IReadOnlyList<OnlineCourseRow> Rows { get; private set; } = [];
    private JsonObject courses = new();
    private string? statusFilter;
    private bool showNoRequirement;
    public OnlineCourseTable(string platform)
    {
        Table.Classes.Add("course-table");
        RowDefinitions = new RowDefinitions("Auto,10,Auto,10,*,Auto");
        var options = platform == "mooc"
            ? new[] { ("primary", "课件"), ("quiz", "测验"), ("homework", "作业"), ("discussion", "讨论") }
            : new[] { ("primary", "任务点"), ("homework", "作业"), ("discussion", "讨论"), ("task", "活动"), ("visits", "学习次数") };
        foreach (var (key, label) in options)
        {
            var button = Ui.Button(label, () => SelectCategory(key), "course-tab");
            button.Tag = key; button.MinWidth = 68; button.Padding = new Thickness(15, 5);
            button.Classes.Set("accent", key == Category);
            categories.Children.Add(button);
        }
        Add(categories, 0); Add(summary, 2);
        Table.Bind(DataGrid.HorizontalGridLinesBrushProperty, Table.GetResourceObservable("SubtleBorderBrush"));
        static TextBlock Header(string text, bool number) => new()
        {
            Text = Loc.T(text), FontSize = 12, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap, Opacity = .9,
            HorizontalAlignment = number ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = new Thickness(number ? 0 : TextInset, 0, number ? NumberInset : 0, 0)
        };
        void NumberColumn(string title, string property, Func<OnlineCourseRow, string?> value, double width, bool emphasis = false, double fontSize = 13)
        {
            Table.Columns.Add(new DataGridTemplateColumn { Header = Header(title, true), SortMemberPath = property, Width = new DataGridLength(width),
                CellTemplate = new FuncDataTemplate<OnlineCourseRow>((row, _) =>
                {
                    var text = row is null ? null : value(row);
                    var block = new TextBlock {
                        Text = text ?? "—", FontSize = fontSize, TextWrapping = TextWrapping.NoWrap,
                        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, NumberInset, 0),
                        FontWeight = text is null || !emphasis || text == "0" ? FontWeight.Normal : FontWeight.Medium
                    };
                    block.FontFeatures = new FontFeatureCollection { FontFeature.Parse("tnum") };
                    // Unknown or zero values stay quiet so the eye lands on the numbers that still need work.
                    if (text is null || !emphasis || text == "0")
                        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable("TextSecondaryBrush"));
                    return block;
                }) });
        }
        
        var courseNameHeader = Header("课程名称", false);
        courseNameHeader.Margin = new Thickness(CourseNameInset, 0, 0, 0);
        Table.Columns.Add(new DataGridTemplateColumn { Header = courseNameHeader, SortMemberPath = nameof(OnlineCourseRow.Name), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 160,
            CellTemplate = new FuncDataTemplate<OnlineCourseRow>((row, _) =>
            {
                var name = Ui.Text(row?.Name ?? ""); name.FontWeight = FontWeight.SemiBold;
                name.FontSize = 15; name.Margin = new Thickness(TextInset, 0); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
                ToolTip.SetTip(name, row?.Name);
                var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = CoursePalette.Brush(row?.Name ?? ""), VerticalAlignment = VerticalAlignment.Center };
                var content = Ui.Columns("8,*", dot, name);
                content.Margin = new Thickness(TextInset, 0, 0, 0);
                return content;
            }) });
        static string? Quantity(double? value) => value?.ToString("0.#", CultureInfo.InvariantCulture);
        NumberColumn("要求", nameof(OnlineCourseRow.Required), row => Quantity(row.Required), 68);
        NumberColumn("已完成", nameof(OnlineCourseRow.Done), row => Quantity(row.Done), 72);
        NumberColumn("剩余", nameof(OnlineCourseRow.Remaining), row => Quantity(row.Remaining), 68, emphasis: true);
        Table.Columns.Add(new DataGridTemplateColumn { Header = Header("完成进度", false), SortMemberPath = nameof(OnlineCourseRow.Percent), Width = new DataGridLength(150),
            CellTemplate = new FuncDataTemplate<OnlineCourseRow>((row, _) =>
            {
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("80,8,34"), Width = 122, HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(TextInset, 0, 0, 0) };
                if (row?.Percent is not { } percent)
                {
                    var dash = new TextBlock { Text = "—", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                    dash.Bind(TextBlock.ForegroundProperty, dash.GetResourceObservable("TextSecondaryBrush"));
                    Grid.SetColumn(dash, 0); grid.Children.Add(dash);
                    ToolTip.SetTip(grid, "该项无要求或进度尚未确认");
                    return grid;
                }
                // Two plain borders instead of a ProgressBar: the bar keeps an exact 80 px width inside the column.
                var track = new Border { Width = 80, Height = 6, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
                track.Bind(Border.BackgroundProperty, track.GetResourceObservable("SubtleBorderBrush"));
                var fill = new Border { Width = Math.Max(6, Math.Round(80 * percent / 100)), Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
                fill.Bind(Border.BackgroundProperty, fill.GetResourceObservable(row.Status == "已完成" ? "SuccessBrush" : "AccentBrush"));
                var bar = new Panel { Width = 80, Height = 6, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
                bar.Children.Add(track); bar.Children.Add(fill);
                var label = new TextBlock { Text = row.PercentText, FontSize = 12, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                label.FontFeatures = new FontFeatureCollection { FontFeature.Parse("tnum") };
                label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("TextSecondaryBrush"));
                Grid.SetColumn(label, 2); grid.Children.Add(bar); grid.Children.Add(label);
                ToolTip.SetTip(grid, $"已完成 {row.DoneText} / {row.RequiredText}");
                return grid;
            }) });
        Table.Columns.Add(new DataGridTemplateColumn { Header = Header("状态", false), SortMemberPath = nameof(OnlineCourseRow.Status), Width = new DataGridLength(100),
            CellTemplate = new FuncDataTemplate<OnlineCourseRow>((row, _) =>
            {
                var status = row?.Status ?? "待确认";
                var label = Ui.Text(status); label.FontSize = 12; label.FontWeight = FontWeight.Medium;
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable(status switch {
                    "已完成" => "SuccessBrush", "待完成" => "CoursePendingForeground", "待确认" => "WarningBrush", _ => "TextSecondaryBrush" }));
                var badge = new Border { Child = label, CornerRadius = new CornerRadius(999), Height = 24, MinWidth = 60, Padding = new Thickness(10, 0),
                    Margin = new Thickness(TextInset, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
                badge.Classes.Add("status-chip");
                badge.Bind(Border.BackgroundProperty, badge.GetResourceObservable(status switch {
                    "已完成" => "CourseSuccessBackground", "待完成" => "AccentFaintBrush", "待确认" => "CourseWarningBackground", _ => "CourseNeutralBackground" }));
                ToolTip.SetTip(badge, row?.Reason); return badge;
            }) });
        foreach (var column in Table.Columns.Where(column => !column.Width.IsStar))
            column.MinWidth = column.Width.Value;
        emptyHint.Bind(TextBlock.ForegroundProperty, emptyHint.GetResourceObservable("TextSecondaryBrush"));
        emptyState.Children.Add(emptyTitle); emptyState.Children.Add(emptyHint);
        card = Ui.Card(Table); card.Padding = new Thickness(0); card.ClipToBounds = true; Add(card, 4); Add(emptyState, 4);
        detail.TextWrapping = TextWrapping.Wrap; detail.Margin = new Thickness(2, 10, 0, 0); detail.IsVisible = false; Add(detail, 5);
        Table.SelectionChanged += (_, _) =>
        {
            detail.Text = (Table.SelectedItem as OnlineCourseRow)?.Reason ?? ""; detail.IsVisible = detail.Text.Length > 0;
        };
        RenderSummary();
    }
    private void Add(Control control, int row) { SetRow(control, row); Children.Add(control); }
    public void Load(JsonObject data) { courses = data; Rebuild(); }
    public void SelectCategory(string category)
    {
        Category = category;
        foreach (var button in categories.Children.OfType<Button>()) button.Classes.Set("accent", button.Tag as string == category);
        Rebuild();
    }
    private void Rebuild()
    {
        var selected = (Table.SelectedItem as OnlineCourseRow)?.Name;
        Rows = courses.Select((pair, index) => OnlineCourseRow.Read(pair.Value!, Category, index + 1)).ToArray();
        ApplyFilter();
        if (selected is not null) Table.SelectedItem = Rows.FirstOrDefault(row => row.Name == selected);
        var hasRows = Rows.Count > 0;
        Table.IsVisible = hasRows; card.IsVisible = hasRows; emptyState.IsVisible = !hasRows;
        if (!hasRows)
        {
            var untouched = courses.Count == 0;
            emptyTitle.Text = Loc.T(untouched ? "暂无课程记录" : "该分类下没有记录");
            emptyHint.Text = Loc.T(untouched ? "登录后点击「读取课程任务」，课程进度会保存在本地。" : "可以切换上方分类，或重新读取课程任务。");
        }
        RenderSummary();
    }
    private void ApplyFilter()
    {
        Table.ItemsSource = Rows.Where(row => statusFilter is not null ? row.Status == statusFilter : showNoRequirement || row.Status != "无此要求")
            .OrderBy(row => row.Status == "待完成" ? 0 : row.Status == "已完成" ? 1 : row.Status == "待确认" ? 2 : 3).ToArray();
    }
    private void RenderSummary()
    {
        summary.Children.Clear();
        if (Rows.Count == 0) return;
        void Chip(string label, string? filter, int count)
        {
            var button = Ui.Button(Loc.T(label) + " " + count, () => { statusFilter = filter; ApplyFilter(); RenderSummary(); }, statusFilter == filter ? "tonal" : "ghost");
            button.FontSize = 13; button.Padding = new Thickness(10, 4); summary.Children.Add(button);
        }
        Chip("全部", null, Rows.Count); Chip("待完成", "待完成", Rows.Count(r => r.Status == "待完成"));
        Chip("已完成", "已完成", Rows.Count(r => r.Status == "已完成"));
        var count = Rows.Count(r => r.Status == "无此要求");
        if (count > 0)
        {
            var none = Ui.Button(Loc.T("无此要求") + " " + count + (showNoRequirement ? " ▾" : " ▸"), () => { showNoRequirement = !showNoRequirement; statusFilter = null; ApplyFilter(); RenderSummary(); }, "ghost");
            none.FontSize = 13; summary.Children.Add(none);
        }
        var unknown = Rows.Count(r => r.Status == "待确认"); if (unknown > 0) Chip("待确认", "待确认", unknown);
        var updated = Rows.Select(r => DateTime.TryParse(r.Updated, out var date) ? date : DateTime.MinValue).Max();
        if (updated != DateTime.MinValue) { var time = Ui.Text(Loc.F("上次检查：{0}", updated.ToString("MM-dd HH:mm")), "caption"); time.Margin = new Thickness(8, 0, 0, 0); summary.Children.Add(time); }
    }
}
