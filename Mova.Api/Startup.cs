using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hangfire;
using Mova.Api.Configurations;
using Mova.Api.Middlewares;
using Mova.Shared.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Mova.Application;
using Mova.Infrastructure;
using Mova.Infrastructure.Jobs;
using Mova.Api.RateLimiting;

namespace Mova.Api;

public class Startup(IConfiguration configuration)
{
    private readonly IConfiguration _configuration = configuration;


    // Register services
    public void ConfigureServices(IServiceCollection services)
    {
        // Controllers
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                options.JsonSerializerOptions.Converters.Add(
                    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            });
        
        // CQRS (MediatR)
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(
                typeof(AssemblyReference).Assembly
            );
        });

        // Model Behaviour setting

        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState.Values
                    .SelectMany(x => x.Errors)
                    .Select(x => x.ErrorMessage)
                    .Where(x => !string.IsNullOrWhiteSpace(x));

                var result = new BaseResult(
                    statusCode: HttpStatusCode.BadRequest,
                    message: string.Join(" | ", errors)
                );

                return new ObjectResult(result)
                {
                    StatusCode = 400
                };
            };
        });

        services.AddCors(options =>
        {
            options.AddPolicy("AllowSpecificOrigin",
                policy =>
                {
                    policy.WithOrigins(
                        "https://localhost:3000", 
                        "http://localhost:5173",
                        "http://localhost:5174",
                        "https://mova-frontend.luckystarboy01.workers.dev"
                        )
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                });

            options.AddPolicy("AllowAllDev",
                policy =>
                {
                    policy.AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowAnyHeader();
                });
        });


        services.AddAppRateLimiting();

        // Swagger
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Mova API",
                Version = "v1",
                Description = "Mova Fintech Platform API",

                Contact = new OpenApiContact
                {
                    Name = "Mova Engineering Team",
                    Email = "engineering@mova.com"
                },

                License = new OpenApiLicense
                {
                    Name = "Internal Use"
                }
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter JWT Token. Example: eyJhbGciOiJIUzI1NiIs..."
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });


            options.SwaggerDoc("v2", new OpenApiInfo
            {
                Title = "Mova API",
                Version = "v2",
                Description = "Mova Fintech Platform API V2"
            });


            options.SwaggerDoc("v3", new OpenApiInfo
            {
                Title = "Mova API",
                Version = "v3",
                Description = "Mova Fintech Platform API V3"
            });

            options.CustomSchemaIds(type =>
            {
                var fullName = type.FullName ?? type.Name;
                return fullName
                    .Replace("+", "_")
                    .Replace("`", "_")
                    .Replace("[", "_")
                    .Replace("]", "")
                    .Replace(",", "_")
                    .Replace(" ", "_");
            });

        });


        // Later add:
        services.AddInfrastructure(_configuration);
        services.Configure<SwaggerSettings>(
            _configuration.GetSection(SwaggerSettings.SectionName));

        services.AddDataProtection();
    }



    // Configure middleware pipeline
    public void Configure(WebApplication app)
    {
        var recurringJobManager = app.Services
            .GetRequiredService<IRecurringJobManager>();

        recurringJobManager.AddOrUpdate<ProcessScheduledReleasesJob>(
            "process-scheduled-releases",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.MinuteInterval(1));

        // recurringJobManager.AddOrUpdate<ProcessPayoutsJob>(
        //     "process-releases-payouts",
        //     job => job.ExecuteAsync(CancellationToken.None),
        //     Cron.MinuteInterval(2));

        // recurringJobManager.AddOrUpdate<processPendingProcessingTransactions>(
        //     "process-releases-payouts",
        //     job => job.ExecuteAsync(CancellationToken.None),
        //     Cron.HourInterval(6));

        app.UseCors("AllowSpecificOrigin");

        app.UseMiddleware<SwaggerAuthMiddleware>();

        app.UseSwagger();

        app.UseSwaggerUI(options =>
        {
            options.DisplayRequestDuration();

            options.EnablePersistAuthorization();

            options.SwaggerEndpoint(
                "/swagger/v1/swagger.json",
                "Mova API v1");


            options.SwaggerEndpoint(
                "/swagger/v2/swagger.json",
                "Mova API v2");


            options.SwaggerEndpoint(
                "/swagger/v3/swagger.json",
                "Mova API v3");


            options.DocumentTitle =
                "Mova API Documentation";
        });

        app.UseMiddleware<ExceptionHandlingMiddleware>();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }

        app.UseAuthentication();

        app.UseAuthorization();

        app.UseRateLimiter();

        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = new[]
            {
                new HangfireDashboardAuthorizationFilter()
            }
        });

        app.UseStaticFiles();

        app.MapControllers();
    }
}