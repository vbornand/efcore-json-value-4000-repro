using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

// A JSON collection whose element carries a string longer than 4000 characters. Read whole, the
// text comes back; projected element by element, EF reads it with JSON_VALUE over OPENJSON's [value]
// column, which returns at most 4000 characters and null beyond.

await using var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();
await container.StartAsync();

var text = new string('x', 4500);

foreach (var (label, configure) in new (string, Action<DbContextOptionsBuilder>)[]
{
    ("UseAzureSql, compatibility level 170 (json column)", o => o.UseAzureSql(Database("Json170"), s => s.UseCompatibilityLevel(170))),
    ("UseSqlServer, compatibility level 160 (nvarchar(max) column)", o => o.UseSqlServer(Database("Nvarchar160"), s => s.UseCompatibilityLevel(160))),
})
{
    var sql = new List<string>();
    var options = new DbContextOptionsBuilder<BlogContext>();
    configure(options);
    options.LogTo(sql.Add, [Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted]);

    await using var context = new BlogContext(options.Options);
    await context.Database.EnsureCreatedAsync();
    context.Blogs.Add(new Blog { Notes = [new Note { Language = "en", Text = text }] });
    await context.SaveChangesAsync();
    context.ChangeTracker.Clear();

    Console.WriteLine($"== {label}");

    var whole = await context.Blogs.Select(b => b.Notes).SingleAsync();
    Console.WriteLine($"Select(b => b.Notes):                    {Describe(whole[0].Text)}");

    sql.Clear();
    var projected = await context.Blogs
        .Select(b => b.Notes.Select(n => new { n.Language, n.Text }).ToList())
        .SingleAsync();
    Console.WriteLine($"Select(b => b.Notes.Select(n => new {{ n.Language, n.Text }})): {Describe(projected[0].Text)}");
    Console.WriteLine(sql.Last().Split("CommandTimeout='30']")[^1].Trim());
    Console.WriteLine();
}

string Database(string name) => container.GetConnectionString().Replace("Database=master", $"Database={name}");

static string Describe(string? value) => value is null ? "null" : $"{value.Length} characters";

public sealed class BlogContext(DbContextOptions<BlogContext> options) : DbContext(options)
{
    public DbSet<Blog> Blogs => Set<Blog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Blog>().ComplexCollection(b => b.Notes, n => n.ToJson());
}

public sealed class Blog
{
    public int Id { get; set; }
    public List<Note> Notes { get; set; } = [];
}

public sealed class Note
{
    public string Language { get; set; } = null!;
    public string Text { get; set; } = null!;
}
