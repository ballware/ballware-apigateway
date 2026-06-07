using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Ballware.ApiGateway.Service.Transforms;

public sealed class CustomHeaderFromClaimTransformFactory : ITransformFactory
{
    private const string TransformKey = "CustomHeaderFromClaim";
    private const string HeaderKey = "CustomHeaderFromClaimHeader";

    public bool Validate(
        TransformRouteValidationContext context,
        IReadOnlyDictionary<string, string> transformValues)
    {
        if (!transformValues.TryGetValue(TransformKey, out var claimType))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(claimType))
        {
            context.Errors.Add(new ArgumentException(
                "A non-empty claim type is required for CustomHeaderFromClaim.",
                nameof(transformValues)));
        }

        if (!transformValues.TryGetValue(HeaderKey, out var headerName) ||
            string.IsNullOrWhiteSpace(headerName))
        {
            context.Errors.Add(new ArgumentException(
                "A non-empty header name is required for CustomHeaderFromClaimHeader.",
                nameof(transformValues)));
        }

        return true;
    }

    public bool Build(
        TransformBuilderContext context,
        IReadOnlyDictionary<string, string> transformValues)
    {
        if (!transformValues.TryGetValue(TransformKey, out var claimType))
        {
            return false;
        }

        var headerName = transformValues[HeaderKey];

        context.AddRequestTransform(transformContext =>
        {
            var claimValue = transformContext.HttpContext.User.FindFirst(claimType)?.Value;

            if (string.IsNullOrWhiteSpace(claimValue))
            {
                transformContext.HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                return ValueTask.CompletedTask;
            }

            transformContext.ProxyRequest.Headers.Remove(headerName);
            transformContext.ProxyRequest.Headers.TryAddWithoutValidation(headerName, claimValue);

            return ValueTask.CompletedTask;
        });

        return true;
    }
}
