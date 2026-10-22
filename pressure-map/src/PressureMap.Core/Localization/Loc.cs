using System.Globalization;

namespace PressureMap.Localization;

/// <summary>界面语言。中文是源语言，英文按中文原文查表；查不到时退回中文，避免出现空白。</summary>
public static class Loc
{
    public const string Chinese = "zh";
    public const string English = "en";
    public static string Language { get; private set; } = Chinese;
    public static bool IsEnglish => Language == English;
    public static void Use(string? language) => Language = string.Equals(language, English, StringComparison.OrdinalIgnoreCase) ? English : Chinese;
    private static CultureInfo Culture => CultureInfo.GetCultureInfo(IsEnglish ? "en-US" : "zh-CN");
    public static string T(string chinese) => !IsEnglish ? chinese : StringsEn.Map.TryGetValue(chinese, out var english) ? english : chinese;
    /// <summary>带占位符的句子：中文模板用 {0}、{1}…，查表后按当前语言格式化。</summary>
    public static string F(string chineseTemplate, params object?[] arguments) => string.Format(Culture, T(chineseTemplate), arguments);
    /// <summary>日期格式随语言切换：中文用「M月d日」，英文用「MMM d」。</summary>
    public static string Date(DateTime value, string chineseFormat, string englishFormat) => value.ToString(IsEnglish ? englishFormat : chineseFormat, Culture);
    public static string DayOfWeek(DateTime value) => value.ToString("dddd", Culture);
    /// <summary>任务类型是数据库里的中文值，只在显示时翻译。</summary>
    public static string Kind(string kind) => T(kind);
}
