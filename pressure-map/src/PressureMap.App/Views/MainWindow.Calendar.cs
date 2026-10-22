using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using PressureMap.Core;
using PressureMap.Localization;

namespace PressureMap.Views;

public partial class MainWindow
{
    private TextBlock? scheduleStatus;
    private ToggleSwitch? scheduleToggle;
    private bool updatingScheduleToggle;
    private Control SchedulePage()
    {
        schedulePreview = new ScheduleControl(Store);
        schedulePreview.TaskRequested += async title => await NewTodoForCourseAsync(title);
        var toggle = Ui.Switch("在桌面显示今日课程", Preferences.ScheduleEnabled, showLabel: false); scheduleToggle = toggle;
        toggle.IsCheckedChanged += (_, _) => { if (!updatingScheduleToggle) { if (toggle.IsChecked == true) ShowScheduleDesktop(); else HideScheduleDesktop(); } };
        scheduleStatus = Ui.Text(desktopSchedule?.IsVisible == true ? desktopSchedule.MaterialStatus : "桌面课程尚未开启", "caption");
        var list = Ui.Stack();
        foreach (var source in Store.ListCalendars())
        {
            var own = source;
            var info = Ui.Stack(Ui.Text(source.Name), Ui.Text($"{source.Format.ToUpperInvariant()} · {source.ImportedAt.Replace('T', ' ')}", "caption")); info.Spacing = 2;
            var remove = Ui.Link("移除", () => _ = RemoveCalendarAsync(own));
            list.Children.Add(Ui.Columns("*,Auto", info, remove));
        }
        if (list.Children.Count == 0) list.Children.Add(Ui.Text("尚未导入课表", "caption"));
        var tools = Ui.Stack(Ui.Text("我的课表", "h2"), Ui.AsyncButton("导入课表…", ImportCalendarAsync, "accent"),
            list, new Separator(), Ui.Text("桌面课程", "h2"), Ui.SettingRow("在桌面显示今日课程", toggle),
            Ui.Row(Ui.Link("锁定 / 解锁位置", () => { ShowScheduleDesktop(); desktopSchedule?.ToggleLock(); }), Ui.Link("收起 / 展开", () => { ShowScheduleDesktop(); desktopSchedule?.ToggleCollapse(); })));
        var card = Ui.Card(schedulePreview); card.Padding = new Thickness(18);
        var columns = Ui.Columns("*,16,250", card, new Border(), Ui.Card(Ui.Scroll(tools)));
        return new PageFrame(Ui.Text("课程日历", "h1"), columns, false);
    }
    private async Task RemoveCalendarAsync(CalendarSource own)
    {
        if (!await Dialogs.Confirm(this, "移除课表", Loc.F("移除“{0}”及它的课程？", own.Name))) return;
        try { Store.DeleteCalendar(own.Name); RefreshCalendars(); SelectPage(8); } catch (Exception e) { Error(e); }
    }
    private sealed record CalendarImportResult(CalendarPreview Preview, HduPdfResult? Pdf, HduSemester? Semester);
    private Task ImportCalendarAsync() => ImportCalendarDialogAsync();
    /// <param name="seededName">界面自检用：跳过系统文件选择框，直接填入这个文件名。</param>
    /// <param name="seededBytes">界面自检用：与 <paramref name="seededName"/> 一起提供文件内容。</param>
    /// <param name="seededPdf">界面自检用：直接展示已经解析好的杭电 PDF 结果。</param>
    private async Task ImportCalendarDialogAsync(string? seededName = null, byte[]? seededBytes = null, HduPdfResult? seededPdf = null)
    {
        var fileName = Ui.Text("尚未选择文件", "h2");
        var fileMeta = Ui.Text("PDF · ICS · CSV", "caption");
        var fileCard = Ui.Card(Ui.Stack(4, fileName, fileMeta)); fileCard.Padding = new Thickness(14);
        var choose = Ui.Button("选择文件…", () => { }, "tonal");
        choose.HorizontalAlignment = HorizontalAlignment.Left; choose.VerticalAlignment = VerticalAlignment.Center; choose.MinWidth = 116;
        var semesters = new ComboBox { ItemsSource = HduPdfCalendar.Semesters, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var semesterHint = Ui.Text("", "caption"); semesterHint.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        var semesterSection = Ui.Stack(8, Ui.Text("学期预设", "label"), semesters, semesterHint); semesterSection.IsVisible = false;
        var message = Ui.Text("", "danger"); message.IsVisible = false; message.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        var summary = Ui.Stack(10);
        var summaryScroll = Ui.Scroll(summary); summaryScroll.MaxHeight = 340; summaryScroll.IsVisible = false;
        var confirm = Ui.Button("确认", () => { }, "accent"); confirm.IsEnabled = false;
        var body = Ui.Stack(Ui.Field("选择文件", Ui.Columns("*,12,Auto", fileCard, new Border(), choose)), semesterSection, message, summaryScroll);
        var dialog = Dialogs.CreateWindow("导入课表", body, 540);
        body.Children.Add(Ui.Row(confirm, Ui.Button("取消", () => dialog.Close(null))));

        string? name = null; byte[]? bytes = null; CalendarImportResult? result = null; var busy = false; var syncingSemester = false;

        void Problem(string text) { message.Text = text; message.IsVisible = text.Length > 0; summaryScroll.IsVisible = false; confirm.IsEnabled = false; }
        void Show(CalendarPreview preview, HduPdfResult? pdf, HduSemester? semester)
        {
            summary.Children.Clear();
            summary.Children.Add(Ui.Text(Loc.F("{0} 个事件 · {1} 个全天事件不显示为课程", preview.EventCount, preview.AllDayCount), "caption"));
            if (pdf is not null)
            {
                var rules = Ui.Text(HduPdfCalendar.CalendarSummary, "caption"); rules.TextWrapping = Avalonia.Media.TextWrapping.Wrap; summary.Children.Add(rules);
                summary.Children.Add(Ui.Text(Loc.F("已识别 {0} 条排课记录，按校历生成 {1} 次课程。", pdf.LessonCount, preview.EventCount), "caption"));
                foreach (var warning in pdf.Warnings) summary.Children.Add(Ui.Text(warning, "danger"));
                summary.Children.Add(Ui.Link("另存为 ICS…", () => _ = SaveConvertedIcsAsync(pdf)));
            }
            if (Store.ListCalendars().Any(s => string.Equals(s.Name, preview.Source.Name, StringComparison.OrdinalIgnoreCase)))
                summary.Children.Add(Ui.Text("已有同名课表，确认后将更新这份课表。", "danger"));
            summary.Children.Add(Ui.Text("未来一年课程预览", "label"));
            if (preview.Upcoming.Count == 0) summary.Children.Add(Ui.Text("未来一年没有课程，请检查日历日期。仍可导入并切换到其他日期查看。", "caption"));
            foreach (var item in preview.Upcoming.Take(10))
                summary.Children.Add(Ui.Stack(2, Ui.Text(item.Title), Ui.Text(Loc.F("{0} {1} · {2}", Loc.Date(item.Start, "M月d日", "MMM d"), item.TimeLabel, item.Location), "caption")));
            message.IsVisible = false; summaryScroll.IsVisible = true; confirm.IsEnabled = true;
        }
        async Task<CalendarImportResult?> ParseAsync(string pickedName, byte[] data, HduSemester? forced)
        {
            try
            {
                HduSemester? semester = null; HduPdfResult? pdf = null; string text;
                if (string.Equals(Path.GetExtension(pickedName), ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    var probe = forced is null ? await Task.Run(() => HduPdfCalendar.Probe(data)) : new HduPdfProbe(true, forced);
                    if (!probe.IsSchedule) { semesterSection.IsVisible = false; semesterHint.Text = ""; Problem(Loc.T("无法识别这份 PDF：请选择杭电教务处导出的文字版课表。")); return null; }
                    semesterSection.IsVisible = true;
                    if (probe.Semester is null) { semesterHint.Text = Loc.T("这份 PDF 的学期不在预设中，请先更新学期预设。"); Problem(""); return null; }
                    semesterHint.Text = Loc.T("已检测到杭电教务课表 PDF，请确认学期预设。");
                    var index = HduPdfCalendar.Semesters.ToList().IndexOf(probe.Semester);
                    if (index >= 0 && semesters.SelectedIndex != index) { syncingSemester = true; semesters.SelectedIndex = index; syncingSemester = false; }
                    semester = probe.Semester;
                    var chosen = semester;
                    pdf = await Task.Run(() => HduPdfCalendar.Convert(pickedName, data, chosen.Id));
                    text = pdf.Ics;
                }
                else
                {
                    semesterSection.IsVisible = false; semesterHint.Text = "";
                    text = new UTF8Encoding(false, true).GetString(data);
                }
                var previewName = pdf?.Name ?? pickedName;
                var preview = await Task.Run(() => CourseCalendar.Preview(previewName, text, DateTime.Today));
                Show(preview, pdf, semester);
                return new(preview, pdf, semester);
            }
            catch (DecoderFallbackException) { Problem(Loc.T("文件不是 UTF-8 编码，请在 Excel 中另存为 CSV UTF-8 后导入。")); }
            catch (Exception exception) { Problem(Loc.F("无法读取这个课表：{0}", exception.Message)); }
            return null;
        }
        async Task LoadAsync(string pickedName, byte[] data)
        {
            var isPdf = string.Equals(Path.GetExtension(pickedName), ".pdf", StringComparison.OrdinalIgnoreCase);
            name = pickedName; bytes = data; result = null;
            fileName.Text = Path.GetFileName(pickedName);
            fileMeta.Text = (isPdf ? "PDF" : Path.GetExtension(pickedName).TrimStart('.').ToUpperInvariant()) + " · " + Size(data.Length);
            semesterSection.IsVisible = false; semesterHint.Text = ""; summary.Children.Clear(); summaryScroll.IsVisible = false; message.IsVisible = false; confirm.IsEnabled = false;
            if (data.Length > (isPdf ? HduPdfCalendar.MaxPdfBytes : CourseCalendar.MaxBytes)) { Problem(Loc.T(isPdf ? "PDF 不能超过 10 MB。" : "课表文件不能超过 2 MB。")); return; }
            busy = true; choose.IsEnabled = false;
            try { result = await ParseAsync(pickedName, data, null); }
            finally { busy = false; choose.IsEnabled = true; }
        }
        choose.Click += async (_, _) =>
        {
            if (busy) return;
            IReadOnlyList<IStorageFile> files;
            try
            {
                files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Loc.T("选择课表文件"), AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("课表（PDF / ICS / CSV）") { Patterns = ["*.pdf", "*.ics", "*.csv"] }] });
            }
            catch (Exception exception) { Problem(exception.Message); return; }
            if (files.Count == 0) return;
            var isPdf = string.Equals(Path.GetExtension(files[0].Name), ".pdf", StringComparison.OrdinalIgnoreCase);
            var limit = isPdf ? HduPdfCalendar.MaxPdfBytes : CourseCalendar.MaxBytes;
            byte[]? data = null;
            busy = true; choose.IsEnabled = false;
            try
            {
                await using var stream = await files[0].OpenReadAsync();
                using var memory = new MemoryStream(); var buffer = new byte[8192]; int read;
                while ((read = await stream.ReadAsync(buffer)) > 0)
                {
                    if (memory.Length + read > limit) throw new InvalidDataException(Loc.T(isPdf ? "PDF 不能超过 10 MB。" : "课表文件不能超过 2 MB。"));
                    await memory.WriteAsync(buffer.AsMemory(0, read));
                }
                data = memory.ToArray();
            }
            catch (Exception exception) { Problem(exception.Message); }
            finally { busy = false; choose.IsEnabled = true; }
            if (data is not null) await LoadAsync(files[0].Name, data);
        };
        semesters.SelectionChanged += async (_, _) =>
        {
            if (syncingSemester || busy || !semesterSection.IsVisible || name is null || bytes is null || semesters.SelectedItem is not HduSemester picked) return;
            busy = true; try { result = await ParseAsync(name, bytes, picked); } finally { busy = false; }
        };
        confirm.Click += (_, _) => { if (result is not null) dialog.Close(result); };

        if (seededPdf is not null)
        {
            fileName.Text = seededName ?? seededPdf.Name; fileMeta.Text = "PDF";
            semesterSection.IsVisible = true; semesterHint.Text = Loc.T("已检测到杭电教务课表 PDF，请确认学期预设。");
            var preview = await Task.Run(() => CourseCalendar.Preview(seededPdf.Name, seededPdf.Ics, DateTime.Today));
            result = new(preview, seededPdf, HduPdfCalendar.Semesters[0]); Show(preview, seededPdf, result.Semester);
        }
        else if (seededName is not null && seededBytes is not null) await LoadAsync(seededName, seededBytes);

        var imported = await dialog.ShowDialog<CalendarImportResult?>(this);
        if (imported is null) return;
        try
        {
            await Task.Run(() => Store.SaveCalendar(imported.Preview.Source));
            RefreshCalendars(); SelectPage(8); Notice("课表已导入，可以开启桌面今日课程。");
        }
        catch (Exception e) { Error(e); }
    }
    private static string Size(int bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024:0.0} MB" : $"{Math.Max(1, (int)Math.Round(bytes / 1024d))} KB";
    private async Task SaveConvertedIcsAsync(HduPdfResult pdf)
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "保存 ICS 课表", SuggestedFileName = pdf.Name, DefaultExtension = "ics", FileTypeChoices = [new FilePickerFileType("ICS 日历") { Patterns = ["*.ics"] }] });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync(); stream.SetLength(0);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false)); await writer.WriteAsync(pdf.Ics);
            Notice("ICS 课表已保存。");
        }
        catch (Exception e) { Error(e); }
    }
    private void RefreshCalendars() { _ = RefreshTodayOverviewAsync(); if (schedulePreview is not null) _ = schedulePreview.ReloadAsync(); desktopSchedule?.Refresh(); desktopPressure?.Refresh(); if (settingsPressure is not null) _ = settingsPressure.ReloadAsync(); if (todayCourses is not null) _ = todayCourses.ReloadAsync(); }
    public void ShowScheduleDesktop()
    {
        try
        {
            if (desktopSchedule?.NativeAvailable == false) { desktopSchedule.Dispose(); desktopSchedule = null; }
            if (desktopSchedule is null)
            {
                desktopSchedule = new TodoWindow(Store, Preferences, LocalStore.DirectoryPath, schedule: true);
                desktopSchedule.TaskRequested += async title => { Restore(); await NewTodoForCourseAsync(title); };
                desktopSchedule.SettingsRequested += () => { Restore(); SelectPage(8); };
                desktopSchedule.ManageRequested += () => { Restore(); SelectPage(8); };
                desktopSchedule.StatusChanged += status => { if (scheduleStatus is not null) scheduleStatus.Text = status; };
                desktopSchedule.HiddenByUser += () => { UpdateScheduleToggle(false); Preferences.ScheduleEnabled = false; SavePreferences(); };
            }
            desktopSchedule.Present(); EnsureTray(); Preferences.ScheduleEnabled = true; SavePreferences(); UpdateScheduleToggle(true);
            if (scheduleStatus is not null) scheduleStatus.Text = desktopSchedule.MaterialStatus;
        }
        catch (Exception e) { Preferences.ScheduleEnabled = false; desktopSchedule?.HideFromDesktop(); UpdateScheduleToggle(false); Error(e); }
    }
    private void HideScheduleDesktop() { desktopSchedule?.HideFromDesktop(); Preferences.ScheduleEnabled = false; SavePreferences(); UpdateScheduleToggle(false); }
    private void UpdateScheduleToggle(bool enabled) { updatingScheduleToggle = true; if (scheduleToggle is not null) scheduleToggle.IsChecked = enabled; updatingScheduleToggle = false; }
}
