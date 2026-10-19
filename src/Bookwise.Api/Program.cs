using System.Text;
using System.Text.Json.Serialization;
using Bookwise.Api.Data;
using Bookwise.Api.Domain;
using Bookwise.Api.Features.Availability;
using Bookwise.Api.Features.Bookings;
using Bookwise.Api.Features.Reminders;
using Bookwise.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<BusinessOptions>().BindConfiguration(BusinessOptions.Section).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.Section).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<SeedOptions>().BindConfiguration(SeedOptions.Section);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<BookwiseDbContext>((sp, options) =>
    options.UseSqlite(sp.GetRequiredService<IConfiguration>().GetConnectionString("Bookwise")));

builder.Services.AddScoped<AvailabilityService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<ReminderProcessor>();
builder.Services.AddSingleton<INotificationSender, LoggingNotificationSender>();
builder.Services.AddHostedService<ReminderBackgroundService>();
builder.Services.AddSingleton<IPasswordHasher<AdminUser>, PasswordHasher<AdminUser>>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
    {
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Value.Issuer,
            ValidAudience = jwt.Value.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.Key)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddBookwiseRateLimits();
builder.Services.AddHealthChecks().AddDbContextCheck<BookwiseDbContext>();
builder.Services.AddOpenApi(o => o.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "Bookwise API";
    document.Info.Description = "Appointment booking for service businesses. Public endpoints power the booking page; "
        + "admin endpoints need a JWT from POST /api/auth/login.";
    return Task.CompletedTask;
}));

var app = builder.Build();

await DataSeeder.InitialiseAsync(app.Services);

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference(o => o.WithTitle("Bookwise API"));
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

/// <summary>Exposed so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
