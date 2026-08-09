using Aiursoft.DbTools;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Entities;

public abstract class TemplateDbContext(DbContextOptions options) : IdentityDbContext<User>(options), ICanMigrate
{
    public DbSet<GlobalSetting> GlobalSettings { get; set; }

    public virtual  Task MigrateAsync(CancellationToken cancellationToken) =>
        Database.MigrateAsync(cancellationToken);

    public virtual  Task<bool> CanConnectAsync() =>
        Database.CanConnectAsync();

    public DbSet<MarkdownDocument> MarkdownDocuments => Set<MarkdownDocument>();

    public DbSet<LocalizedDocument> LocalizedDocuments => Set<LocalizedDocument>();

    public DbSet<SearchEmbedding> SearchEmbeddings => Set<SearchEmbedding>();

    public DbSet<LocalizedAbstract> LocalizedAbstracts => Set<LocalizedAbstract>();

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<PostSlugAlias> PostSlugAliases => Set<PostSlugAlias>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MarkdownDocument>()
            .HasIndex(d => new { d.SlugDate, d.Slug })
            .IsUnique();

        builder.Entity<MarkdownDocument>()
            .Property(d => d.IsPublic)
            .IsConcurrencyToken();

        builder.Entity<PostSlugAlias>()
            .HasIndex(a => new { a.PublishedDate, a.Slug })
            .IsUnique();

        builder.Entity<PostSlugAlias>()
            .HasOne(a => a.Document)
            .WithMany(d => d.SlugAliases)
            .HasForeignKey(a => a.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Comment>()
            .HasOne(c => c.Document)
            .WithMany(d => d.Comments)
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Comment>()
            .HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Comment>()
            .HasOne(c => c.ParentComment)
            .WithMany(c => c.Replies)
            .HasForeignKey(c => c.ParentCommentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
