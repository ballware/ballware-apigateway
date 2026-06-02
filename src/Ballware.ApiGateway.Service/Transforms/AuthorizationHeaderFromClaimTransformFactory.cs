using System.Net.Http.Headers;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Ballware.ApiGateway.Service.Transforms;

public sealed class AuthorizationHeaderFromClaimTransformFactory : ITransformFactory
{
    public bool Validate(
        TransformRouteValidationContext context,
        IReadOnlyDictionary<string, string> transformValues)
    {
        if (!transformValues.TryGetValue("AuthorizationHeaderFromClaim", out var claimType))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(claimType))
        {
            context.Errors.Add(new ArgumentException(
                "A non-empty claim type is required for AuthorizationHeaderFromClaim.",
                nameof(transformValues)));
        }

        return true;
    }

    public bool Build(
        TransformBuilderContext context,
        IReadOnlyDictionary<string, string> transformValues)
    {
        if (!transformValues.TryGetValue("AuthorizationHeaderFromClaim", out var claimType))
        {
            return false;
        }

        var scheme = transformValues.GetValueOrDefault("AuthorizationScheme", "Bearer");

        context.AddRequestTransform(transformContext =>
        {
            var claimValue = transformContext.HttpContext.User.FindFirst(claimType)?.Value;

            if (string.IsNullOrWhiteSpace(claimValue))
            {
                transformContext.HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                return ValueTask.CompletedTask;
            }

            transformContext.ProxyRequest.Headers.Authorization = string.IsNullOrWhiteSpace(scheme)
                ? new AuthenticationHeaderValue(claimValue)
                : new AuthenticationHeaderValue(scheme, claimValue);

            return ValueTask.CompletedTask;
        });

        return true;
    }
}
