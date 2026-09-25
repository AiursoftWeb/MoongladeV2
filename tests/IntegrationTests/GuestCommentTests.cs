using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.MoongladeV2.Entities;
using Aiursoft.MoongladeV2.Configuration;
using Aiursoft.MoongladeV2.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;

namespace Aiursoft.MoongladeV2.Tests.IntegrationTests;

[TestClass]
public class GuestCommentTests : TestBase
{
    private readonly List<Guid> createdDocumentIds = [];

    [TestCleanup]
    public override async Task CleanServer()
    {
        if (Server != null)
        {
            using var scope = Server.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var documents = await db.MarkdownDocuments
                .Where(d => createdDocumentIds.Contains(d.Id))
                .ToListAsync();
            db.MarkdownDocuments.RemoveRange(documents);
            await db.SaveChangesAsync();
        }

        await base.CleanServer();
    }

    [TestMethod]
    public async Task AnonymousVisitor_CanPostComment_WithGuestNameAndLongPureContent()
    {
        var documentId = await CreatePublicDocumentAsync();
        var content = new string('x', 1500);
        var guestPage = await Http.GetStringAsync($"/post/{documentId}");
        StringAssert.Contains(guestPage, "name=\"guestName\"");
        StringAssert.Contains(guestPage, "name=\"guestEmail\"");
        StringAssert.Contains(guestPage, "name=\"captchaAnswer\"");
        StringAssert.Contains(guestPage, "id=\"comment-captcha-image\"");
        Assert.IsFalse(guestPage.Contains(" = ?"));
        StringAssert.Contains(guestPage, "/Account/Login");
        var imageMatch = Regex.Match(guestPage, "data:image/png;base64,([^\"]+)");
        Assert.IsTrue(imageMatch.Success, "The comment form should render a PNG captcha image.");
        var image = Convert.FromBase64String(WebUtility.HtmlDecode(imageMatch.Groups[1].Value));
        CollectionAssert.AreEqual(new byte[] { 137, 80, 78, 71 }, image[..4]);
        var tokenMatch = Regex.Match(guestPage, "id=\"comment-captcha-token\" value=\"([^\"]+)\"");
        Assert.IsTrue(tokenMatch.Success, "The comment form should include the captcha token.");
        var captchaToken = WebUtility.HtmlDecode(tokenMatch.Groups[1].Value);
        var captchaAnswer = DecodeCaptchaAnswer(captchaToken);

        var response = await PostForm("/Comments/Post", new Dictionary<string, string>
        {
            ["documentId"] = documentId.ToString(),
            ["guestName"] = "  Pure Guest  ",
            ["guestEmail"] = "guest@example.com",
            ["captchaToken"] = captchaToken,
            ["captchaAnswer"] = captchaAnswer,
            ["content"] = content
        }, tokenUrl: $"/post/{documentId}");

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var comment = await db.Comments.AsNoTracking().SingleAsync(c => c.DocumentId == documentId);
        Assert.IsNull(comment.UserId);
        Assert.AreEqual("Pure Guest", comment.GuestName);
        Assert.AreEqual("guest@example.com", comment.GuestEmail);
        Assert.AreEqual(content, comment.Content);

        var postResponse = await Http.GetAsync($"/post/{documentId}");
        postResponse.EnsureSuccessStatusCode();
        StringAssert.Contains(await postResponse.Content.ReadAsStringAsync(), "Pure Guest");
    }

    [TestMethod]
    public async Task AnonymousVisitor_WithoutGuestName_IsRejected()
    {
        var documentId = await CreatePublicDocumentAsync();
        var (captchaToken, captchaAnswer) = CreateCaptcha(documentId);
        var response = await PostForm("/Comments/Post", new Dictionary<string, string>
        {
            ["documentId"] = documentId.ToString(),
            ["guestName"] = " ",
            ["captchaToken"] = captchaToken,
            ["captchaAnswer"] = captchaAnswer,
            ["content"] = "Anonymous comment"
        }, tokenUrl: $"/post/{documentId}");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task AnonymousVisitor_CannotPostWhenSettingIsDisabled()
    {
        var documentId = await CreatePublicDocumentAsync();
        using (var scope = Server!.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<GlobalSettingsService>()
                .UpdateSettingAsync(SettingsMap.AllowAnonymousComments, "False");
        }

        var page = await Http.GetStringAsync($"/post/{documentId}");
        Assert.IsFalse(page.Contains("name=\"guestName\""));
        var (captchaToken, captchaAnswer) = CreateCaptcha(documentId);
        var response = await PostForm("/Comments/Post", new Dictionary<string, string>
        {
            ["documentId"] = documentId.ToString(),
            ["guestName"] = "Guest",
            ["captchaToken"] = captchaToken,
            ["captchaAnswer"] = captchaAnswer,
            ["content"] = "Blocked comment"
        }, tokenUrl: $"/post/{documentId}");

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        StringAssert.Contains(response.Headers.Location?.OriginalString ?? string.Empty, "/Error/Code403");
        using var checkScope = Server!.Services.CreateScope();
        var db = checkScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.IsFalse(await db.Comments.AnyAsync(c => c.DocumentId == documentId));
    }

    [TestMethod]
    public async Task AnonymousVisitor_InvalidCaptcha_IsRejected()
    {
        var documentId = await CreatePublicDocumentAsync();
        var (captchaToken, _) = CreateCaptcha(documentId);
        var response = await PostForm("/Comments/Post", new Dictionary<string, string>
        {
            ["documentId"] = documentId.ToString(),
            ["guestName"] = "Guest",
            ["captchaToken"] = captchaToken,
            ["captchaAnswer"] = "999",
            ["content"] = "Wrong answer"
        }, tokenUrl: $"/post/{documentId}");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task AnonymousVisitor_CanRefreshCaptcha_AndCannotUseItForAnotherPost()
    {
        var documentId = await CreatePublicDocumentAsync();
        var otherDocumentId = await CreatePublicDocumentAsync();
        var response = await Http.GetAsync($"/Comments/Captcha?documentId={documentId}");
        response.EnsureSuccessStatusCode();
        Assert.AreEqual("no-store", response.Headers.CacheControl?.ToString());

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var image = Convert.FromBase64String(json.RootElement.GetProperty("imageBase64").GetString()!);
        CollectionAssert.AreEqual(new byte[] { 137, 80, 78, 71 }, image[..4]);
        var token = json.RootElement.GetProperty("token").GetString()!;
        var answer = DecodeCaptchaAnswer(token);
        using var scope = Server!.Services.CreateScope();
        var captcha = scope.ServiceProvider.GetRequiredService<CommentCaptchaService>();
        Assert.IsTrue(captcha.Verify(documentId, token, answer));
        Assert.IsFalse(captcha.Verify(otherDocumentId, token, answer));
        Assert.IsFalse(captcha.Verify(documentId, token, "wrong"));
    }

    [TestMethod]
    public async Task SignedInUser_SeesCompactFormAndCanComment()
    {
        var documentId = await CreatePublicDocumentAsync();
        await LoginAsAdmin();

        var page = await Http.GetStringAsync($"/post/{documentId}");
        StringAssert.Contains(page, "name=\"captchaAnswer\"");
        Assert.IsFalse(page.Contains("name=\"guestName\""));
        Assert.IsFalse(page.Contains("name=\"guestEmail\""));

        var (captchaToken, captchaAnswer) = CreateCaptcha(documentId);
        var response = await PostForm("/Comments/Post", new Dictionary<string, string>
        {
            ["documentId"] = documentId.ToString(),
            ["captchaToken"] = captchaToken,
            ["captchaAnswer"] = captchaAnswer,
            ["content"] = "**Signed in** comment"
        }, tokenUrl: $"/post/{documentId}");

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var comment = await db.Comments.SingleAsync(c => c.DocumentId == documentId);
        Assert.IsNotNull(comment.UserId);
        Assert.IsNull(comment.GuestName);
        Assert.IsNull(comment.GuestEmail);

        var renderedPage = await Http.GetStringAsync($"/post/{documentId}");
        StringAssert.Contains(renderedPage, "<strong>Signed in</strong> comment");
    }

    private (string Token, string Answer) CreateCaptcha(Guid documentId)
    {
        using var scope = Server!.Services.CreateScope();
        var (_, token) = scope.ServiceProvider.GetRequiredService<CommentCaptchaService>().Create(documentId);
        return (token, DecodeCaptchaAnswer(token));
    }

    private string DecodeCaptchaAnswer(string token)
    {
        using var scope = Server!.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        var payload = provider.CreateProtector("Moonglade.CommentCaptcha.v1")
            .ToTimeLimitedDataProtector().Unprotect(token);
        var ediToken = payload.Split(':', 2)[1];
        var ediPayload = provider.CreateProtector("Edi.Captcha.Stateless").Unprotect(ediToken);
        using var json = JsonDocument.Parse(ediPayload);
        return json.RootElement.GetProperty("Code").GetString()!;
    }

    private async Task<Guid> CreatePublicDocumentAsync()
    {
        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var admin = await userManager.FindByEmailAsync("admin@default.com")
                    ?? throw new InvalidOperationException("Seeded admin user was not found.");
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var document = new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            UserId = admin.Id,
            Title = "Guest comment target",
            Content = "Post body",
            IsPublic = true
        };
        db.MarkdownDocuments.Add(document);
        await db.SaveChangesAsync();
        createdDocumentIds.Add(document.Id);
        return document.Id;
    }
}
