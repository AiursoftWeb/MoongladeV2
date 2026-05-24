using Aiursoft.MoongladeV2.Services;
using Ganss.Xss;
using Markdig;

namespace Aiursoft.MoongladeV2.Tests.UnitTests;

[TestClass]
public class MoongladeV2ServiceTests
{
    private MoongladeV2Service _service = null!;

    [TestInitialize]
    public void Initialize()
    {
        // Setup dependencies manually or via DI container
        var services = new ServiceCollection();
        
        // Add core services needed
        services.AddSingleton(new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());
        services.AddSingleton(new HtmlSanitizer());
        services.AddTransient<MoongladeV2Service>();

        var provider = services.BuildServiceProvider();
        _service = provider.GetRequiredService<MoongladeV2Service>();
    }

    [TestMethod]
    public void ConvertMarkdownToHtml_BasicMarkdown_ReturnsHtml()
    {
        var markdown = "# Hello World";
        var html = _service.ConvertMarkdownToHtml(markdown);
        
        // Markdig usually produces "\n" at the end.
        StringAssert.Contains(html, "<h1>Hello World</h1>");
    }

    [TestMethod]
    public void ConvertMarkdownToHtml_XSS_SanitizesHtml()
    {
        var markdown = "<script>alert('xss')</script>";
        var html = _service.ConvertMarkdownToHtml(markdown);
        
        Assert.DoesNotContain("<script>", html);
    }
    
    [TestMethod]
    public void ConvertMarkdownToHtml_Table_ReturnsTable()
    {
        var markdown = @"
| Header 1 | Header 2 |
| -------- | -------- |
| Cell 1   | Cell 2   |
";
        var html = _service.ConvertMarkdownToHtml(markdown);
        
        StringAssert.Contains(html, "<table>");
        StringAssert.Contains(html, "<th>Header 1</th>");
    }
}
