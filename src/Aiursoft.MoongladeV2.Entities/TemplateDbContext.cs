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

    public DbSet<DocumentShare> DocumentShares => Set<DocumentShare>();

    public DbSet<LocalizedDocument> LocalizedDocuments => Set<LocalizedDocument>();

    public DbSet<SearchEmbedding> SearchEmbeddings => Set<SearchEmbedding>();

    public DbSet<LocalizedAbstract> LocalizedAbstracts => Set<LocalizedAbstract>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MarkdownDocument>()
            .HasIndex(d => d.Slug)
            .IsUnique()
            .HasFilter("[Slug] IS NOT NULL");
    }
}
