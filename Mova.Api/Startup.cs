using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mova.Api.Configurations;
using Mova.Api.Middlewares;
using Mova.Shared.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Mova.Application;
using Mova.Infrastructure;

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
                    policy.WithOrigins("https://localhost:3000", "https://yourdomain.com")
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
                if (type.IsGenericType)
                {
                    var typeName = type.Name.Split('`')[0];
                    var genericArgs = string.Join("_", type.GetGenericArguments().Select(t => t.Name));
                    return $"{typeName}_{genericArgs}";
                }
                
                if (type.IsNested)
                    return $"{type.DeclaringType!.Name}.{type.Name}";
                
                return type.Name;
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


        if (app.Environment.IsDevelopment())
        {
            app.UseCors("AllowAllDev");
        }
        else
        {
            app.UseCors("AllowSpecificOrigin");
        }


        app.UseMiddleware<ExceptionHandlingMiddleware>();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }

        app.UseAuthentication();

        app.UseAuthorization();

        app.UseStaticFiles();

        app.MapControllers();
    }
}