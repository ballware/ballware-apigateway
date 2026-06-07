using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Ballware.ApiGateway.Service.Transforms;

public sealed class AuthorizationTokenToHeaderTransformFactory : ITransformFactory
{
    private const string TokenFormat = "Token";
    private const string BearerFormat = "Bearer";

    public bool Validate(
        TransformRouteValidationContext context,
        IReadOnlyDictionary<string, string> transformValues)
    {
        if (!transformValues.TryGetValue("AuthorizationTokenToHeader", out var headerName))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(headerName))
        {
            context.Errors.Add(new ArgumentException(
                "A non-empty header name is required for AuthorizationTokenToHeader.",
                nameof(transformValues)));
        }

        if (transformValues.TryGetValue("AuthorizationTokenToHeaderFormat", out var format) &&
            !string.Equals(format, TokenFormat, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(format, BearerFormat, StringComparison.OrdinalIgnoreCase))
        {
            context.Errors.Add(new ArgumentException(
                $"AuthorizationTokenToHeaderFormat must be either '{TokenFormat}' or '{BearerFormat}'.",
                nameof(transformValues)));
        }

        return true;
    }

    public bool Build(
        TransformBuilderContext context,
        IReadOnlyDictionary<string, string> transformValues)
    {
        if (!transformValues.TryGetValue("AuthorizationTokenToHeader", out var headerName))
        {
            return false;
        }

        var format = transformValues.GetValueOrDefault("AuthorizationTokenToHeaderFormat", TokenFormat);

        context.AddRequestTransform(transformContext =>
        {
            var authorizationHeader = transformContext.HttpContext.Request.Headers.Authorization.ToString();
            const string bearerPrefix = "Bearer ";

            if (string.IsNullOrWhiteSpace(authorizationHeader) ||
                !authorizationHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                transformContext.HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                return ValueTask.CompletedTask;
            }

            var token = authorizationHeader[bearerPrefix.Length..].Trim();

            if (string.IsNullOrWhiteSpace(token))
            {
                transformContext.HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                return ValueTask.CompletedTask;
            }

            transformContext.ProxyRequest.Headers.Remove(headerName);
            transformContext.ProxyRequest.Headers.TryAddWithoutValidation(
                headerName,
                string.Equals(format, BearerFormat, StringComparison.OrdinalIgnoreCase)
                    ? $"{bearerPrefix}{token}"
                    : token);

            return ValueTask.CompletedTask;
        });

        return true;
    }
}
