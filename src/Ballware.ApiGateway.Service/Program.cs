using Ballware.ApiGateway.Service.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Ocelot.Configuration.File;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;

var builder = WebApplication.CreateBuilder(args);

var environment = builder.Environment;

builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
builder.Configuration.AddJsonFile($"appsettings.{environment.EnvironmentName}.json", true, false);
builder.Configuration.AddJsonFile($"appsettings.local.json", true, false);
builder.Configuration.AddEnvironmentVariables();

AuthorizationOptions? authorizationOptions =
    builder.Configuration.GetSection("Authorization").Get<AuthorizationOptions>();

builder.Services.AddOptionsWithValidateOnStart<AuthorizationOptions>()
    .Bind(builder.Configuration.GetSection("Authorization"))
    .ValidateDataAnnotations();

if (authorizationOptions == null)
{
    throw new ConfigurationException("Required configuration for authorization is missing");
}

builder.Services.Configure<KestrelServerOptions>(builder.Configuration.GetSection("Kestrel"));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.Authority = authorizationOptions.Authority;
    options.Audience = authorizationOptions.Audience;
    options.RequireHttpsMetadata = authorizationOptions.RequireHttpsMetadata;
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters()
    {
        ValidIssuer = authorizationOptions.Authority
    };
});

var downstreamHostsSection = builder.Configuration.GetSection("DownstreamHosts");
var downstreamHosts = downstreamHostsSection.GetChildren()
    .ToDictionary(x => x.Key, x => new
    {
        Host = x.GetValue<string>("Host"),
        Port = x.GetValue<int>("Port"),
        Scheme = x.GetValue<string>("Scheme"),
    });

var ocelotRoutesSection = builder.Configuration.GetSection("Ocelot:Routes");
var updatedRoutes = new List<FileRoute>();

foreach (var route in ocelotRoutesSection.GetChildren())
{
    var serviceKey = route.GetValue<string>("ServiceKey");
    if (serviceKey != null && downstreamHosts.TryGetValue(serviceKey, out var downstreamHost))
    {
        var newRoute = route.Get<FileRoute>();

        newRoute.DownstreamScheme = downstreamHost.Scheme;
        newRoute.DownstreamHostAndPorts =
        [
            new FileHostAndPort
            {
                Host = downstreamHost.Host,
                Port = downstreamHost.Port
            }
        ];
        
        updatedRoutes.Add(newRoute);
    }
}

var ocelotConfig = new FileConfiguration
{
    Routes = updatedRoutes,
    GlobalConfiguration = new FileGlobalConfiguration
    {
        //BaseUrl = builder.Configuration["Ocelot:GlobalConfiguration:BaseUrl"]
    }
};

builder.Configuration.AddOcelot(ocelotConfig, builder.Environment, MergeOcelotJson.ToMemory);
builder.Services.AddOcelot(builder.Configuration);



var app = builder.Build();

app.UseAuthorization();
await app.UseOcelot();

await app.RunAsync();