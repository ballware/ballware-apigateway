using Ballware.ApiGateway.Service.Configuration;
using Microsoft.Extensions.Primitives;

namespace Ballware.ApiGateway.Service.Middleware;

public sealed class AuthorizationTokenHeaderMiddleware(
    RequestDelegate next,
    IReadOnlyList<AuthorizationTokenHeaderRoute> routes)
{
    public Task InvokeAsync(HttpContext context)
    {
        var route = routes.FirstOrDefault(candidate =>
            context.Request.Path.StartsWithSegments(candidate.PathPrefix));

        if (route == null)
        {
            return next(context);
        }

        if (context.Request.Headers.TryGetValue(route.HeaderName, out var tokenValues) &&
            TryGetToken(tokenValues, out var token))
        {
            context.Request.Headers.Authorization = $"Bearer {token}";
        }

        return next(context);
    }

    private static bool TryGetToken(StringValues values, out string token)
    {
        token = values.FirstOrDefault()?.Trim() ?? string.Empty;

        const string bearerPrefix = "Bearer ";
        if (token.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            token = token[bearerPrefix.Length..].Trim();
        }

        return !string.IsNullOrWhiteSpace(token);
    }
}
