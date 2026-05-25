using Aiursoft.MoongladeV2.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Aiursoft.MoongladeV2.Tests;

[TestClass]
public class SearchRateLimiterTests
{
    private IMemoryCache _cache = null!;
    private SearchRateLimiter _rateLimiter = null!;

    [TestInitialize]
    public void Initialize()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
        _rateLimiter = new SearchRateLimiter(_cache);
    }

    [TestMethod]
    public void AllowsUpToMaxRequests_PerIpPerWindow()
    {
        for (var i = 0; i < 8; i++)
        {
            Assert.IsTrue(_rateLimiter.TryConsume("192.168.1.1"),
                $"Request {i + 1} should be allowed within the rate limit.");
        }
    }

    [TestMethod]
    public void BlocksAfterExceedingLimit()
    {
        for (var i = 0; i < 8; i++)
        {
            Assert.IsTrue(_rateLimiter.TryConsume("192.168.1.1"));
        }

        Assert.IsFalse(_rateLimiter.TryConsume("192.168.1.1"),
            "9th request within the same window should be rate limited.");
    }

    [TestMethod]
    public void DifferentIps_HaveIndependentLimits()
    {
        for (var i = 0; i < 8; i++)
        {
            _rateLimiter.TryConsume("192.168.1.1");
        }

        Assert.IsFalse(_rateLimiter.TryConsume("192.168.1.1"));

        for (var i = 0; i < 8; i++)
        {
            Assert.IsTrue(_rateLimiter.TryConsume("192.168.1.2"),
                $"IP 2 request {i + 1} should be allowed (independent limit).");
        }

        Assert.IsFalse(_rateLimiter.TryConsume("192.168.1.2"),
            "IP 2 should be rate limited after exceeding its own limit.");
    }

    [TestMethod]
    public void NullIp_UsesStringLiteral()
    {
        for (var i = 0; i < 8; i++)
        {
            Assert.IsTrue(_rateLimiter.TryConsume("unknown"));
        }

        Assert.IsFalse(_rateLimiter.TryConsume("unknown"));
    }
}
