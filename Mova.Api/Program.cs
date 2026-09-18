using DotNetEnv;
using Mova.Api;
using Mova.Infrastructure.Identity;
using Mova.Infrastructure.Persistence.Seeding;

var currentDirectory = Directory.GetCurrentDirectory();

var projectRoot = Path.GetFullPath(
    Path.Combine(currentDirectory, "..")
);

var builder = WebApplication.CreateBuilder(args);

var envFileName = builder.Environment.IsDevelopment()
    ? ".env.dev"
    : ".env.prod";

var envPath = Path.Combine(projectRoot, envFileName);

if (!File.Exists(envPath))
{
    throw new FileNotFoundException(
        $"Required environment file was not found at: {envPath}",
        envPath);
}

Env.Load(envPath);

builder.Configuration.AddEnvironmentVariables();


builder.WebHost.UseSentry(options =>
{
    options.Dsn = builder.Configuration["Sentry:Dsn"];
    options.Debug = builder.Environment.IsDevelopment();
    options.EnableLogs = true;
});

var startup = new Startup(builder.Configuration);

startup.ConfigureServices(builder.Services);

var app = builder.Build();

await app.Services.SeedIdentityAsync();

using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider
        .GetRequiredService<DatabaseSeeder>();

    await seeder.SeedAsync();
}

startup.Configure(app);

app.Run();