namespace PressureMap.Localization;

internal static partial class StringsEn
{
    internal static readonly Dictionary<string, string> Map = Build();
    private static Dictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        AddStatic1(map); AddStatic2(map); AddStatic3(map); AddTemplates(map); AddUi(map);
        return map;
    }
}
