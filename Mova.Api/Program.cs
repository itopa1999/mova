using DotNetEnv;
using Mova.Api;
using Mova.Infrastructure.Identity;
using Mova.Infrastructure.Persistence.Seeding;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    var currentDirectory = Directory.GetCurrentDirectory();

    var projectRoot = Path.GetFullPath(
        Path.Combine(currentDirectory, "..")
    );

    var envPath = Path.Combine(projectRoot, ".env.dev");

    if (File.Exists(envPath))
    {
        Env.Load(envPath);
    }
}

builder.Configuration.AddEnvironmentVariables();

if (builder.Environment.IsProduction())
{
    builder.WebHost.UseSentry(options =>
    {
        options.Dsn = builder.Configuration["Sentry:Dsn"];
        options.Debug = false;
        options.EnableLogs = true;
    });
    builder.Logging.AddSentry(options =>
    {
        options.Dsn = builder.Configuration["Sentry:Dsn"];
        options.MinimumEventLevel = LogLevel.Error;
        options.MinimumBreadcrumbLevel = LogLevel.Information;
        options.AttachStacktrace = true;
    });
}

var startup = new Startup(builder.Configuration);

startup.ConfigureServices(builder.Services);

var app = builder.Build();

await app.Services.SeedIdentityAsync();

await using (var scope = app.Services.CreateAsyncScope())
{
    var seeder = scope.ServiceProvider
        .GetRequiredService<DatabaseSeeder>();

    await seeder.SeedAsync();
}

startup.Configure(app);

app.Run();