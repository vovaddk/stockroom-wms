using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Stockroom.Api.Endpoints;
using Stockroom.Api.Http;
using Stockroom.Api.Security;
using Stockroom.Application;
using Stockroom.Application.Abstractions;
using Stockroom.Domain.Users;
using Stockroom.Infrastructure;
using Stockroom.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication(builder.Configuration.GetSection("PutAway:Strategies").Get<string[]>())
    .AddInfrastructure();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddSingleton<ITokenService, JwtTokenService>();

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.Section))
    .Validate(o => Encoding.UTF8.GetByteCount(o.SigningKey) >= 32, "Jwt:SigningKey must be at least 32 bytes.")
    .ValidateOnStart();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
    {
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = jwt.Value.ValidationParameters();
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Operator, p => p.RequireRole(Roles.Operator, Roles.Manager))
    .AddPolicy(Policies.Manager, p => p.RequireRole(Roles.Manager));

builder.Services.AddLoginRateLimit(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");
builder.Services.AddOpenApi(o => o.AddDocumentTransformer((document, _, _) =>
{
    document.Components ??= new OpenApiComponents();
    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Token from POST /api/auth/login",
    };
    document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }];
    return Task.CompletedTask;
}));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/", () => Results.Redirect("/terminal/")).ExcludeFromDescription();
app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapMasterDataEndpoints();
app.MapInboundEndpoints();
app.MapOutboundEndpoints();
app.MapInventoryEndpoints();

var database = app.Configuration.GetSection("Database");
if (database.GetValue<bool>("MigrateOnStartup"))
{
    await DatabaseInitializer.InitializeAsync(
        app.Services,
        database.GetValue<bool>("SeedDemoData"),
        app.Configuration.GetSection("DemoUsers").Get<DemoUser[]>() ?? []);
}

await app.RunAsync();
