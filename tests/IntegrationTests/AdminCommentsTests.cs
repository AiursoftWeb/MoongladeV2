using Aiursoft.MoongladeV2.Entities;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.MoongladeV2.Tests.IntegrationTests;

[TestClass]
public class AdminCommentsTests : TestBase
{
    [TestMethod]
    public async Task Comments_WithReply_RendersReplyAuthor()
    {
        await LoginAsAdmin();

        const string replyAuthor = "Reply author";
        using (var scope = Server!.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var admin = await userManager.FindByEmailAsync("admin@default.com")
                        ?? throw new InvalidOperationException("Seeded admin user was not found.");
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var document = new MarkdownDocument
            {
                UserId = admin.Id,
                Title = "Post with comments"
            };
            var rootComment = new Comment
            {
                Document = document,
                UserId = admin.Id,
                Content = "Root comment",
                IsApproved = true
            };
            var replyUser = new User
            {
                UserName = $"reply-{Guid.NewGuid():N}",
                Email = $"reply-{Guid.NewGuid():N}@example.com",
                DisplayName = replyAuthor
            };
            var createUserResult = await userManager.CreateAsync(replyUser);
            Assert.IsTrue(createUserResult.Succeeded,
                string.Join(Environment.NewLine, createUserResult.Errors.Select(e => e.Description)));

            db.Comments.AddRange(rootComment, new Comment
            {
                Document = document,
                UserId = replyUser.Id,
                ParentComment = rootComment,
                Content = "Reply comment",
                IsApproved = true
            });
            await db.SaveChangesAsync();
        }

        var response = await Http.GetAsync("/Admin/Comments");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(html, replyAuthor);
    }
}
