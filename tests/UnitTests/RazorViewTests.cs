using System.Text.RegularExpressions;

namespace Aiursoft.MoongladeV2.Tests.UnitTests;

[TestClass]
public class RazorViewTests
{
    [TestMethod]
    public void Views_DoNotDefineTheSameSectionMoreThanOnce()
    {
        var repositoryRoot = FindRepositoryRoot();
        var viewsRoot = Path.Combine(repositoryRoot, "src", "Aiursoft.MoongladeV2", "Views");
        var duplicateSections = Directory.EnumerateFiles(viewsRoot, "*.cshtml", SearchOption.AllDirectories)
            .Select(file => new
            {
                File = Path.GetRelativePath(repositoryRoot, file),
                Sections = Regex.Matches(File.ReadAllText(file), @"(?m)^\s*@section\s+([A-Za-z_][A-Za-z0-9_]*)\s*\{")
                    .Select(match => match.Groups[1].Value)
            })
            .SelectMany(view => view.Sections
                .GroupBy(section => section, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => $"{view.File}: {group.Key} ({group.Count()} definitions)"))
            .ToArray();

        Assert.AreEqual(0, duplicateSections.Length,
            $"Razor views must define each section at most once.{Environment.NewLine}{string.Join(Environment.NewLine, duplicateSections)}");
    }

    [TestMethod]
    public void BlogViews_RenderViewCounts()
    {
        var repositoryRoot = FindRepositoryRoot();
        foreach (var view in new[] { "Index.cshtml", "Archive.cshtml", "Post.cshtml" })
        {
            var contents = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Aiursoft.MoongladeV2", "Views", "Blog", view));
            StringAssert.Contains(contents, "ViewCount", $"{view} should render the view count.");
            StringAssert.Contains(contents, "Localizer[\"Views\"]", $"{view} should localize the view-count label.");
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aiursoft.MoongladeV2.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
