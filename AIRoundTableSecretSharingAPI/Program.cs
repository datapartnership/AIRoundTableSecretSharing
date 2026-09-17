using System.Text.Json.Serialization;
using AIRoundTableSecretSharingAPI.Data;
using AIRoundTableSecretSharingAPI.Repositories;
using AIRoundTableSecretSharingAPI.Services;
using AIRoundTableSecretSharingCommon.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
    });
builder.Services.AddOpenApi();

// CORS origins are configurable via "Cors:AllowedOrigins" (a comma-separated string),
// which can be overridden with the Cors__AllowedOrigins environment variable.
var corsOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireClaim("groups", builder.Configuration["AzureAd:AdminGroupId"]!));
    // Admins are also permitted on partner endpoints
    options.AddPolicy("Partner", policy =>
        policy.RequireClaim("groups",
            builder.Configuration["AzureAd:PartnerGroupId"]!,
            builder.Configuration["AzureAd:AdminGroupId"]!));
});

var azureAdConfiguration = builder.Configuration.GetSection("AzureAd");

builder.Services.AddAuthentication()
    .AddMicrosoftIdentityWebApi(
        jwtBearerOptions =>
        {
            azureAdConfiguration.Bind(jwtBearerOptions);

            if (builder.Environment.IsDevelopment())
            {
                jwtBearerOptions.BackchannelHttpHandler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };

                var instance = azureAdConfiguration["Instance"]?.TrimEnd('/')
                    ?? throw new InvalidOperationException("AzureAd:Instance is required.");
                var tenantId = azureAdConfiguration["TenantId"]
                    ?? throw new InvalidOperationException("AzureAd:TenantId is required.");

                // Accept both the v2.0 issuer (login.microsoftonline.com/{tenant}/v2.0) and the
                // v1.0 issuer (sts.windows.net/{tenant}/) — Entra ID issues the latter whenever the
                // API app registration's "accessTokenAcceptedVersion" is 1 or unset, regardless of
                // how the token was requested.
                var validIssuers = new[]
                {
                    $"{instance}/{tenantId}/v2.0",
                    $"https://sts.windows.net/{tenantId}/",
                };

                jwtBearerOptions.TokenValidationParameters.ValidIssuers = validIssuers;
                jwtBearerOptions.TokenValidationParameters.IssuerValidator =
                    (issuer, _, validationParameters) =>
                    {
                        if (validationParameters.ValidIssuers?.Contains(issuer, StringComparer.Ordinal) == true)
                        {
                            return issuer;
                        }

                        throw new SecurityTokenInvalidIssuerException(
                            $"Issuer '{issuer}' does not match any configured issuer.");
                    };
            }
        },
        microsoftIdentityOptions => azureAdConfiguration.Bind(microsoftIdentityOptions));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IProducerRepository, EfProducerRepository>();
builder.Services.AddScoped<ISubmissionRepository, EfSubmissionRepository>();
builder.Services.AddScoped<IKeyRepository, EfKeyRepository>();
builder.Services.AddScoped<ICiphertextRepository, EfCiphertextRepository>();
builder.Services.AddScoped<IClientCredentialService, DbClientCredentialService>();

var app = builder.Build();

// Migrate on startup — no seed data; partners self-register via POST /registry/producers/me
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "index.html" }
});

app.UseStaticFiles();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
