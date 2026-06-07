using Ballware.ApiGateway.Service.Configuration;
using Ballware.ApiGateway.Service.Middleware;
using Ballware.ApiGateway.Service.Transforms;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text.Json;
using System.Text.Json.Nodes;

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

var reverseProxyRoutesSection = builder.Configuration.GetSection("ReverseProxy:Routes");

var authorizationTokenHeaderRoutes = reverseProxyRoutesSection
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

var oauthProtectedResourceRoutes = builder.Configuration.GetSection("OAuthProtectedResourceRoutes")
    .GetChildren()
    .Select(routeSection =>
    {
        var metadataSection = routeSection.GetSection("OAuthProtectedResource");

        if (!metadataSection.Exists())
        {
            return null;
        }

        var path = routeSection.GetSection("Match").GetValue<string>("Path");

        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return new OAuthProtectedResourceRoute(
            new PathString(path.TrimEnd('/')),
            metadataSection.Get<OAuthProtectedResourceOptions>() ?? new OAuthProtectedResourceOptions(),
            metadataSection);
    })
    .OfType<OAuthProtectedResourceRoute>()
    .ToArray();

builder.Services.Configure<KestrelServerOptions>(builder.Configuration.GetSection("Kestrel"));
builder.Services.AddSingleton<IReadOnlyList<AuthorizationTokenHeaderRoute>>(authorizationTokenHeaderRoutes);
builder.Services.AddSingleton<IReadOnlyList<OAuthProtectedResourceRoute>>(oauthProtectedResourceRoutes);

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
    .AddTransformFactory<AuthorizationTokenToHeaderTransformFactory>()
    .AddTransformFactory<JsonBodyPropertyTransformFactory>();


var app = builder.Build();

IdentityModelEventSource.ShowPII = environment.IsDevelopment();

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} => {StatusCode} in {Elapsed:0.0000} ms";
});

app.UseRouting();
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

app.MapGet("/.well-known/oauth-protected-resource", CreateOAuthProtectedResourceMetadata).AllowAnonymous();
app.MapGet("/.well-known/oauth-protected-resource/{**resourcePath}", CreateOAuthProtectedResourceMetadata).AllowAnonymous();

app.MapReverseProxy();

await app.RunAsync();

IResult CreateOAuthProtectedResourceMetadata(
    HttpContext context,
    IReadOnlyList<OAuthProtectedResourceRoute> routes)
{
    var route = routes.FirstOrDefault(route => context.Request.Path.Equals(route.Path, StringComparison.OrdinalIgnoreCase));

    if (route == null && context.Request.Path != "/.well-known/oauth-protected-resource")
    {
        return Results.NotFound();
    }

    var options = route?.Options ?? new OAuthProtectedResourceOptions();
    var metadataSection = route?.MetadataSection;
    var resource = !string.IsNullOrWhiteSpace(options.Resource)
        ? options.Resource
        : BuildDefaultResource(context);

    var authorizationServers = options.AuthorizationServers is { Length: > 0 }
        ? options.AuthorizationServers
        : [authorizationOptions.Authority];

    var metadata = new OAuthProtectedResourceMetadata
    {
        Resource = resource,
        AuthorizationServers = authorizationServers,
        ScopesSupported = options.ScopesSupported,
        BearerMethodsSupported = route == null
            ? null
            : options.BearerMethodsSupported is { Length: > 0 }
                ? options.BearerMethodsSupported.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                : ["header"],
        ResourceName = options.ResourceName,
        ResourceDocumentation = options.ResourceDocumentation,
        ResourcePolicyUri = options.ResourcePolicyUri,
        ResourceTosUri = options.ResourceTosUri,
        JwksUri = options.JwksUri,
        AdditionalMetadata = metadataSection == null
            ? null
            : BuildJsonExtensionData(metadataSection.GetSection("AdditionalMetadata"))
    };

    return Results.Json(metadata);
}

static string BuildDefaultResource(HttpContext context)
{
    const string WellKnownPath = "/.well-known/oauth-protected-resource";

    var path = context.Request.Path.Value ?? string.Empty;
    var resourcePath = path.StartsWith(WellKnownPath, StringComparison.OrdinalIgnoreCase)
        ? path[WellKnownPath.Length..]
        : string.Empty;

    return $"{GetPublicScheme(context)}://{GetPublicHost(context)}{resourcePath}";
}

static string GetPublicScheme(HttpContext context)
{
    var forwardedProto = GetFirstForwardedHeaderValue(context, "X-Forwarded-Proto");

    return string.IsNullOrWhiteSpace(forwardedProto)
        ? context.Request.Scheme
        : forwardedProto;
}

static HostString GetPublicHost(HttpContext context)
{
    var forwardedHost = GetFirstForwardedHeaderValue(context, "X-Forwarded-Host");

    return string.IsNullOrWhiteSpace(forwardedHost)
        ? context.Request.Host
        : new HostString(forwardedHost);
}

static string? GetFirstForwardedHeaderValue(HttpContext context, string headerName)
{
    var headerValue = context.Request.Headers[headerName].ToString();

    return string.IsNullOrWhiteSpace(headerValue)
        ? null
        : headerValue.Split(',', 2)[0].Trim();
}

static Dictionary<string, JsonElement>? BuildJsonExtensionData(IConfigurationSection section)
{
    var children = section.GetChildren().ToArray();

    if (children.Length == 0)
    {
        return null;
    }

    return children.ToDictionary(
        child => child.Key,
        child => JsonSerializer.SerializeToElement(BuildJsonNode(child)));
}

static JsonNode? BuildJsonNode(IConfigurationSection section)
{
    var children = section.GetChildren().ToArray();

    if (children.Length == 0)
    {
        return section.Value is null ? null : JsonValue.Create(section.Value);
    }

    if (children.Select(child => child.Key).All(key => int.TryParse(key, out _)))
    {
        var array = new JsonArray();

        foreach (var child in children.OrderBy(child => int.Parse(child.Key)))
        {
            array.Add(BuildJsonNode(child));
        }

        return array;
    }

    var obj = new JsonObject();

    foreach (var child in children)
    {
        obj[child.Key] = BuildJsonNode(child);
    }

    return obj;
}
