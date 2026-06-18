using System.Text;
using AIRoundTableSecretSharingAPI.Data;
using AIRoundTableSecretSharingAPI.Repositories;
using AIRoundTableSecretSharingAPI.Services;
using AIRoundTableSecretSharingCommon.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtKey = builder.Configuration["Jwt:Key"]!;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IProducerRepository, EfProducerRepository>();
builder.Services.AddScoped<ISubmissionRepository, EfSubmissionRepository>();
builder.Services.AddScoped<IKeyRepository, EfKeyRepository>();
builder.Services.AddSingleton<CiphertextStore>();

var app = builder.Build();

// Migrate and seed on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    if (!db.Producers.Any())
    {
        var startDate = new DateTime(2025, 1, 1);
        db.Producers.AddRange(
            new ProducerInfo { ProducerId = "partnerA", DisplayName = "Partner A", JoinedDate = startDate, IsActive = true },
            new ProducerInfo { ProducerId = "partnerB", DisplayName = "Partner B", JoinedDate = startDate, IsActive = true },
            new ProducerInfo { ProducerId = "partnerC", DisplayName = "Partner C", JoinedDate = startDate, IsActive = true }
        );
        db.Epochs.Add(new ProducerEpoch
        {
            EpochId = 1,
            StartDate = startDate,
            EndDate = null,
            ProducerIds = new List<string> { "partnerA", "partnerB", "partnerC" },
            ProducerCount = 3
        });
        await db.SaveChangesAsync();
        Console.WriteLine("Seeded 3 producers and initial epoch");
    }
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
