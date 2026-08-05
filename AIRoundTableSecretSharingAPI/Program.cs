using AIRoundTableSecretSharingAPI.Data;
using AIRoundTableSecretSharingAPI.Repositories;
using AIRoundTableSecretSharingAPI.Services;
using AIRoundTableSecretSharingCommon.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000")
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

builder.Services.AddAuthentication()
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

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

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
