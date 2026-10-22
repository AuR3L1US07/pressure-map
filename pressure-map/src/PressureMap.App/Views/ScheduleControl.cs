using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PressureMap.Core;
using PressureMap.Localization;

namespace PressureMap.Views;

public sealed class ScheduleControl : UserControl
{
    private Store store;
    private readonly bool compact;
    private readonly StackPanel heading = new() { Spacing = 8 };
    private readonly Grid body = new();
    private readonly TextBlock status = Ui.Text("", "caption"), error = Ui.Text("", "danger"), upcomingInfo = Ui.Text("", "caption");
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(30) };
    private List<CalendarSource> sources = [];
    private List<CourseOccurrence> events = [];
    private DateTime selected = DateTime.Today;
    private DateTime lastRenderedDay;
    private bool followToday = true, endedExpanded;
    private int revision;
    public string Summary => status.Text ?? "";
    public string? LastError { get; private set; }
    public DateTime SelectedDate => selected;
    public int VisibleCourseCount => events.Count;
    public event Action? SettingsRequested;
    public event Action<string>? TaskRequested;
    public ScheduleControl(Store store, bool compact = false)
    {
        this.store = store; this.compact = compact; endedExpanded = compact;
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"), RowSpacing = compact ? 6 : 10 };
        root.Children.Add(heading); Grid.SetRow(status, 1); root.Children.Add(status);
        Grid.SetRow(upcomingInfo, 2); root.Children.Add(upcomingInfo);
        Grid.SetRow(body, 3); root.Children.Add(body); Grid.SetRow(error, 4); root.Children.Add(error);
        Content = root;
        AttachedToVisualTree += (_, _) => clock.Start(); DetachedFromVisualTree += (_, _) => { clock.Stop(); revision++; };
        clock.Tick += async (_, _) => { if (followToday && selected.Date != DateTime.Today) { selected = DateTime.Today; await LoadDayAsync(); } else Render(); };
        _ = ReloadAsync();
    }
    public void SetStore(Store value) { store = value; _ = ReloadAsync(); }
    public async Task ReloadAsync()
    {
        try { sources = store.ListCalendars(); await LoadDayAsync(); }
        catch (Exception e) { LastError = e.Message; error.Text = e.Message; }
    }
    public async Task SelectDateAsync(DateTime date) { selected = date.Date; followToday = selected == DateTime.Today; await LoadDayAsync(); }
    private async Task LoadDayAsync()
    {
        var own = ++revision; var day = selected; var input = sources.ToList(); status.Text = Loc.T("正在读取课程…");
        try
        {
            var result = await Task.Run(() => CourseCalendar.Day(input, day));
            var ahead = await Task.Run(() => CourseCalendar.Between(input, day.AddDays(1), day.AddDays(8)));
            if (own != revision) return;
            events = result; LastError = null; error.Text = "";
            upcomingInfo.IsVisible = ahead.Count > 0 && !compact;
            upcomingInfo.Text = ahead.Count > 0 ? Loc.F("接下来 · {0} · {1}", Loc.Date(ahead[0].Start, "M月d日 HH:mm", "MMM d, HH:mm"), ahead[0].Title) : "";
            Render();
        }
        catch (Exception e) { if (own == revision) { events = []; LastError = e.Message; error.Text = e.Message; Render(); } }
    }
    private void Render()
    {
        var offset = selected == lastRenderedDay ? body.Children.OfType<ScrollViewer>().FirstOrDefault()?.Offset : null;
        lastRenderedDay = selected;
        heading.Children.Clear(); body.Children.Clear();
        var dayText = Loc.Date(selected, "M月d日 dddd", "dddd, MMM d");
        var dateLabel = Ui.Text(dayText, "h2");
        var controls = Ui.Row(Ui.AsyncButton("‹", () => SelectDateAsync(selected.AddDays(-1)), "icon"), Ui.AsyncButton("今天", () => SelectDateAsync(DateTime.Today), "pill"), Ui.AsyncButton("›", () => SelectDateAsync(selected.AddDays(1)), "icon"));
        Control dateControl = dateLabel;
        if (!compact)
        {
            var picker = new CalendarDatePicker { SelectedDate = selected, DisplayDate = selected, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
            Avalonia.Automation.AutomationProperties.SetName(picker, "选择课程日期");
            picker.SelectedDateChanged += async (_, _) => { if (picker.SelectedDate is { } date && date.Date != selected) await SelectDateAsync(date); };
            dateControl = picker;
        }
        var title = compact ? Ui.Columns("*,Auto", dateControl, controls) : Ui.Columns("Auto,16,*", controls, new Border(), dateControl); heading.Children.Add(title);
        if (compact) { heading.Spacing = 4; dateLabel.FontSize = 16; }
        var week = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*") };
        var monday = selected.AddDays(-((int)selected.DayOfWeek + 6) % 7);
        for (var index = 0; index < 7; index++)
        {
            var date = monday.AddDays(index); var button = Ui.AsyncButton(Loc.Date(date, "ddd", "ddd") + (compact ? " " : "\n") + date.Day, () => SelectDateAsync(date), date == selected ? "accent" : "ghost");
            button.MinHeight = compact ? 36 : 64; button.CornerRadius = new CornerRadius(12); button.Padding = new Thickness(4, 6); button.HorizontalContentAlignment = HorizontalAlignment.Center;
            Avalonia.Automation.AutomationProperties.SetName(button, Loc.Date(date, "yyyy年M月d日", "MMM d, yyyy")); Grid.SetColumn(button, index); week.Children.Add(button);
        }
        heading.Children.Add(week);
        var now = DateTime.Now; var active = events.FirstOrDefault(e => e.Start <= now && now < e.End);
        status.Text = selected == DateTime.Today && active is not null ? Loc.F("正在上课 · {0} · 还有 {1} 分钟", active.Title, Math.Ceiling((active.End - now).TotalMinutes)) : Loc.F("{0} 门课程", events.Count) + (selected == DateTime.Today ? Loc.F(" · {0} 门未结束", events.Count(e => e.End > now)) : "");
        if (!compact && selected == DateTime.Today && active is null && events.FirstOrDefault(e => e.Start > now) is { } next)
            status.Text = Loc.F("下一节 · {0} · {1}", next.Title, next.Start.ToString("HH:mm")) + (next.Location.Length > 0 ? " · " + next.Location : "");
        if (events.Count == 0)
        {
            var empty = (StackPanel)Ui.EmptyState("Calendar", sources.Count == 0 ? "还没有导入课表" : "这一天没有课程", sources.Count == 0 ? "导入日历或 CSV 课表后，在这里查看。" : "留一点时间，安排自己的节奏。");
            empty.HorizontalAlignment = HorizontalAlignment.Center; empty.VerticalAlignment = VerticalAlignment.Center;
            if (compact) empty.Children.Add(Ui.Button("管理课表", () => SettingsRequested?.Invoke(), "pill")); body.Children.Add(empty); return;
        }
        if (compact)
        {
            var list = new StackPanel { Spacing = 10 };
            var ended = selected == DateTime.Today ? events.Where(e => e.End <= now).ToList() : [];
            var upcoming = events.Except(ended).ToList();
            Control Row(CourseOccurrence item)
            {
                var time = Ui.Stack(Ui.Text(item.Start.ToString("HH:mm"), "h2"), Ui.Text(item.End.ToString("HH:mm"), "caption")); time.Spacing = 3;
                return Ui.Columns("56,10,*", time, new Border(), CourseCard(item, now));
            }
            foreach (var item in upcoming) list.Children.Add(Row(item));
            if (ended.Count > 0)
            {
                var previous = new StackPanel { Spacing = 10 }; foreach (var item in ended) previous.Children.Add(Row(item));
                previous.IsVisible = endedExpanded;
                var label = Ui.Text(Loc.F("已结束 {0} 门课程", ended.Count));
                var chevron = Ui.Text(endedExpanded ? "⌃" : "⌄");
                var toggle = Ui.Button("", () => { }, "ghost"); toggle.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                toggle.Content = Ui.Columns("*,Auto", label, chevron); toggle.Padding = new Thickness(12, 8); toggle.CornerRadius = new CornerRadius(10);
                toggle.Click += (_, _) => { endedExpanded = !endedExpanded; previous.IsVisible = endedExpanded; chevron.Text = endedExpanded ? "⌃" : "⌄"; };
                list.Children.Add(toggle); list.Children.Add(previous);
            }
            body.Children.Add(Ui.Scroll(list));
        }
        else body.Children.Add(Timeline(now));
        if (offset is { } saved && body.Children.OfType<ScrollViewer>().FirstOrDefault() is { } viewer)
            Dispatcher.UIThread.Post(() => { if (body.Children.Contains(viewer)) viewer.Offset = saved; }, DispatcherPriority.Loaded);
    }
    private Border CourseCard(CourseOccurrence item, DateTime now)
    {
        var accent = CoursePalette.Color(item.Title); var active = item.Start <= now && now < item.End;
        var addTask = Ui.IconButton("Plus", "为这门课新建任务", () => TaskRequested?.Invoke(item.Title));
        Avalonia.Automation.AutomationProperties.SetName(addTask, "为课程新建任务：" + item.Title);
        var title = Ui.Text(item.Title, "h2"); title.FontSize = 16; title.MaxLines = 2; title.TextTrimming = TextTrimming.CharacterEllipsis; ToolTip.SetTip(title, item.Title);
        var meta = Ui.Text(item.TimeLabel + (item.Location.Length > 0 ? " · " + item.Location : ""), "caption"); meta.TextWrapping = TextWrapping.NoWrap; meta.TextTrimming = TextTrimming.CharacterEllipsis; ToolTip.SetTip(meta, meta.Text);
        var words = Ui.Stack(Ui.Columns("*,Auto", title, addTask), meta); words.Spacing = 4;
        if (selected == DateTime.Today) words.Children.Add(Ui.Text(item.State(now), active ? "h2" : "caption"));
        var content = Ui.Columns("4,12,*", new Border { Background = new SolidColorBrush(accent), CornerRadius = new CornerRadius(2) }, new Border(), words);
        var card = new Border { Child = content, Padding = new Thickness(compact ? 10 : 16), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(active ? 1.5 : 1), BorderBrush = new SolidColorBrush(accent, active ? .8 : .24), Background = CoursePalette.Brush(item.Title, true), ClipToBounds = true };
        ToolTip.SetTip(card, $"{item.Title}\n{item.Start:yyyy-MM-dd HH:mm} – {item.End:yyyy-MM-dd HH:mm}\n{item.Location}\n{item.Description}");
        Avalonia.Automation.AutomationProperties.SetName(card, $"{item.Title} {item.TimeLabel} {item.Location}"); return card;
    }
    private Control Timeline(DateTime now)
    {
        var first = Math.Max(0, Math.Min(8, Math.Min(selected == DateTime.Today ? now.Hour - 1 : 8, events.Min(e => e.Start.Date < selected ? 0 : e.Start.Hour - 1))));
        var last = Math.Max(22, events.Max(e => e.End.Date > selected ? 24 : e.End.Hour + 1 + (e.End.Minute > 0 ? 1 : 0))); last = Math.Min(last, 24);
        const double pixels = 64;
        var canvas = new Canvas { Height = (last - first) * pixels + 12, ClipToBounds = true };
        for (var hour = first; hour <= last; hour++)
        {
            var label = Ui.Text($"{hour:00}:00", "caption"); label.FontSize = 12; label.Width = 40; label.TextAlignment = TextAlignment.Right; Canvas.SetTop(label, (hour - first) * pixels); canvas.Children.Add(label);
            var line = new Border { Height = 1, Background = Ui.Brush("DividerBrush") }; Canvas.SetLeft(line, 48); Canvas.SetTop(line, (hour - first) * pixels + 8); canvas.Children.Add(line);
        }
        var positioned = new List<(Border Card, int Lane, int Count)>();
        var group = new List<CourseOccurrence>(); var groupEnd = DateTime.MinValue;
        void PlaceGroup()
        {
            if (group.Count == 0) return;
            var ends = new List<DateTime>(); var lanes = new List<int>();
            foreach (var item in group) { var lane = ends.FindIndex(end => end <= item.Start); if (lane < 0) { lane = ends.Count; ends.Add(item.End); } else ends[lane] = item.End; lanes.Add(lane); }
            for (var i = 0; i < group.Count; i++)
            {
                var item = group[i]; var startMinutes = Math.Max(first * 60, item.Start.Date < selected ? 0 : item.Start.TimeOfDay.TotalMinutes);
                var endMinutes = Math.Min(last * 60, item.End.Date > selected ? 1440 : item.End.TimeOfDay.TotalMinutes);
                var card = CourseCard(item, now); card.Height = Math.Max(28, (endMinutes - startMinutes) / 60 * pixels - 4); Canvas.SetTop(card, (startMinutes / 60 - first) * pixels + 8); canvas.Children.Add(card); positioned.Add((card, lanes[i], ends.Count));
            }
            group.Clear();
        }
        foreach (var item in events) { if (group.Count > 0 && item.Start >= groupEnd) PlaceGroup(); if (group.Count == 0) groupEnd = item.End; else if (item.End > groupEnd) groupEnd = item.End; group.Add(item); } PlaceGroup();
        var nowLine = new Border { Height = 2, Background = Ui.Brush("NowLineBrush"), IsVisible = selected == DateTime.Today && now.Hour >= first && now.Hour < last }; Canvas.SetLeft(nowLine, 46); Canvas.SetTop(nowLine, (now.TimeOfDay.TotalHours - first) * pixels + 8); canvas.Children.Add(nowLine);
        if (nowLine.IsVisible)
        {
            var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Ui.Brush("NowLineBrush") }; Canvas.SetLeft(dot, 44); Canvas.SetTop(dot, (now.TimeOfDay.TotalHours - first) * pixels + 5); canvas.Children.Add(dot);
            var stamp = Ui.Text(now.ToString("HH:mm"), "caption"); stamp.Foreground = Ui.Brush("NowLineBrush"); stamp.Background = Ui.Brush("SurfaceBrush"); Canvas.SetTop(stamp, (now.TimeOfDay.TotalHours - first) * pixels); canvas.Children.Add(stamp);
        }
        void Resize()
        {
            var available = Math.Max(80, canvas.Bounds.Width - 54);
            foreach (var (card, lane, count) in positioned) { card.Width = Math.Max(35, available / count - 5); Canvas.SetLeft(card, 50 + available / count * lane); }
            foreach (var line in canvas.Children.OfType<Border>().Where(b => b.Height <= 2)) line.Width = available;
        }
        canvas.SizeChanged += (_, _) => Resize(); Resize();
        var scroll = Ui.Scroll(canvas);
        var focus = selected == DateTime.Today ? events.FirstOrDefault(e => e.End > now) ?? events[0] : events[0];
        var initial = Math.Max(0, ((selected == DateTime.Today ? now.TimeOfDay.TotalHours : focus.Start.TimeOfDay.TotalHours) - first - 1) * pixels);
        Dispatcher.UIThread.Post(() => scroll.Offset = new Vector(0, initial), DispatcherPriority.Loaded);
        return scroll;
    }
}
