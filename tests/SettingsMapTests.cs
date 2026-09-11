using Aiursoft.MoongladeV2.Configuration;

namespace Aiursoft.MoongladeV2.Tests;

[TestClass]
public class SettingsMapTests
{
    [TestMethod]
    public void DefaultLocalizationLanguages_IncludeAllSupportedChineseCultures()
    {
        var definition = SettingsMap.Definitions.Single(d => d.Key == SettingsMap.LocalizationLanguages);
        var languages = definition.DefaultValue.Split(',', StringSplitOptions.RemoveEmptyEntries);

        Assert.AreEqual(SettingsMap.DefaultLocalizationLanguages, definition.DefaultValue);
        Assert.HasCount(28, languages);
        CollectionAssert.Contains(languages, "zh-CN");
        CollectionAssert.Contains(languages, "zh-TW");
        CollectionAssert.Contains(languages, "zh-HK");
        Assert.AreEqual(languages.Length, languages.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
