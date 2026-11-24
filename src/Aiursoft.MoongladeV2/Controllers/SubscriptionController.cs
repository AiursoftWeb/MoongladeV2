using Aiursoft.MoongladeV2.Services;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.MoongladeV2.Controllers;

[LimitPerMin]
public class SubscriptionController(FeedService feedService) : Controller
{
    [HttpGet("/rss")]
    [HttpGet("/rss.xml")]
    [HttpGet("/feed")]
    public async Task<IActionResult> Rss()
    {
        var xml = await feedService.BuildRssAsync(Request);
        return Content(xml, "application/rss+xml; charset=utf-8");
    }

    [HttpGet("/rss/{tag}")]
    public async Task<IActionResult> TagRss([FromRoute] string tag)
    {
        var xml = await feedService.BuildRssAsync(Request, tag);
        return Content(xml, "application/rss+xml; charset=utf-8");
    }

    [HttpGet("/atom")]
    [HttpGet("/atom.xml")]
    public async Task<IActionResult> Atom()
    {
        var xml = await feedService.BuildAtomAsync(Request);
        return Content(xml, "application/atom+xml; charset=utf-8");
    }

    [HttpGet("/atom/{tag}")]
    public async Task<IActionResult> TagAtom([FromRoute] string tag)
    {
        var xml = await feedService.BuildAtomAsync(Request, tag);
        return Content(xml, "application/atom+xml; charset=utf-8");
    }
}
