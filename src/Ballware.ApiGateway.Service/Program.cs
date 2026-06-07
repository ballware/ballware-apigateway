using Ballware.ApiGateway.Service.Configuration;
using Ballware.ApiGateway.Service.Middleware;
using Ballware.ApiGateway.Service.Transforms;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var environment = builder.Environment;

builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
builder.Configuration.AddJsonFile($"appsettings.{environment.EnvironmentName}.json", true, false);
builder.Configuration.AddJsonFile($"appsettings.local.json", true, false);
builder.Configuration.AddEnvironmentVariables();

builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

AuthorizationOptions? authorizationOptions =
    builder.Configuration.GetSection("Authorization").Get<AuthorizationOptions>();

builder.Services.AddOptionsWithValidateOnStart<AuthorizationOptions>()
    .Bind(builder.Configuration.GetSection("Authorization"))
    .ValidateDataAnnotations();

if (authorizationOptions == null)
{
    throw new ConfigurationException("Required configuration for authorization is missing");
}

var authorizationTokenHeaderRoutes = builder.Configuration
    .GetSection("ReverseProxy:Routes")
    .GetChildren()
    .Select(routeSection =>
    {
        var path = routeSection.GetSection("Match").GetValue<string>("Path");
        var headerName = routeSection.GetSection("Metadata").GetValue<string>("AuthorizationTokenHeader");

        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(headerName))
        {
            return null;
        }

        var pathPrefix = path.Split('{', 2)[0].TrimEnd('/');

        return string.IsNullOrWhiteSpace(pathPrefix)
            ? null
            : new AuthorizationTokenHeaderRoute(new PathString(pathPrefix), headerName);
    })
    .OfType<AuthorizationTokenHeaderRoute>()
    .ToArray();

builder.Services.Configure<KestrelServerOptions>(builder.Configuration.GetSection("Kestrel"));
builder.Services.AddSingleton<IReadOnlyList<AuthorizationTokenHeaderRoute>>(authorizationTokenHeaderRoutes);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.IncludeErrorDetails = true;
    options.Authority = authorizationOptions.Authority;
    options.Audience = authorizationOptions.Audience;
    options.RequireHttpsMetadata = authorizationOptions.RequireHttpsMetadata;
    options.TokenValidationParameters = new TokenValidationParameters()
    {
        ValidIssuer = authorizationOptions.Issuer ?? authorizationOptions.Authority
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var isWebSocketRequest = context.HttpContext.WebSockets.IsWebSocketRequest;
            var acceptHeader = context.Request.Headers.Accept.ToString();
            var isServerSentEventsRequest = acceptHeader.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(accessToken) && (isWebSocketRequest || isServerSentEventsRequest))
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransformFactory<CustomHeaderFromClaimTransformFactory>()
    .AddTransformFactory<AuthorizationTokenToHeaderTransformFactory>();


var app = builder.Build();

IdentityModelEventSource.ShowPII = environment.IsDevelopment();

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} => {StatusCode} in {Elapsed:0.0000} ms";
});

app.UseCors();
app.UseWebSockets();
app.Use(async (context, next) =>
{
    if (HttpMethods.IsOptions(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        return;
    }

    await next();
});
app.UseMiddleware<AuthorizationTokenHeaderMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/_diag/ping", () => Results.Ok(new { Status = "ok" })).AllowAnonymous();
app.MapReverseProxy();

await app.RunAsync();
