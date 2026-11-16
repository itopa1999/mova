
using System.Net;
using DotNetEnv;
using Microsoft.AspNetCore.HttpOverrides;
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

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;

    options.KnownProxies.Add(IPAddress.Loopback);
});

var startup = new Startup(builder.Configuration, builder.Environment);

startup.ConfigureServices(builder.Services);

var app = builder.Build();

app.UseForwardedHeaders();

await using (var scope = app.Services.CreateAsyncScope())
{
    await app.Services.SeedIdentityAsync();

    var seeder = scope.ServiceProvider
        .GetRequiredService<DatabaseSeeder>();

    await seeder.SeedAsync();
}

startup.Configure(app);

app.Run();
