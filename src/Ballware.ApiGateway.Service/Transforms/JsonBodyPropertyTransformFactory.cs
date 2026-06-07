using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Ballware.ApiGateway.Service.Transforms;

public sealed class JsonBodyPropertyTransformFactory : ITransformFactory
{
    private const string TransformKey = "JsonBodyProperty";
    private const string ValueKey = "JsonBodyPropertyValue";

    public bool Validate(
        TransformRouteValidationContext context,
        IReadOnlyDictionary<string, string> transformValues)
    {
        if (!transformValues.TryGetValue(TransformKey, out var propertyName))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(propertyName))
        {
            context.Errors.Add(new ArgumentException(
                "A non-empty property name is required for JsonBodyProperty.",
                nameof(transformValues)));
        }

        if (!transformValues.TryGetValue(ValueKey, out var propertyValue) ||
            string.IsNullOrWhiteSpace(propertyValue))
        {
            context.Errors.Add(new ArgumentException(
                "A non-empty JSON value is required for JsonBodyPropertyValue.",
                nameof(transformValues)));
        }
        else
        {
            try
            {
                JsonNode.Parse(propertyValue);
            }
            catch (JsonException exception)
            {
                context.Errors.Add(new ArgumentException(
                    "JsonBodyPropertyValue must contain valid JSON.",
                    nameof(transformValues),
                    exception));
            }
        }

        return true;
    }

    public bool Build(
        TransformBuilderContext context,
        IReadOnlyDictionary<string, string> transformValues)
    {
        if (!transformValues.TryGetValue(TransformKey, out var propertyName))
        {
            return false;
        }

        var propertyValue = transformValues[ValueKey];

        context.AddRequestTransform(async transformContext =>
        {
            var request = transformContext.HttpContext.Request;

            if (request.Body == Stream.Null)
            {
                return;
            }

            using var reader = new StreamReader(
                request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: false);

            var requestBody = await reader.ReadToEndAsync(transformContext.CancellationToken);

            var rootNode = JsonNode.Parse(requestBody)?.AsObject() ?? new JsonObject();
            rootNode[propertyName] = JsonNode.Parse(propertyValue);

            var transformedBody = Encoding.UTF8.GetBytes(
                rootNode.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = false
                }));

            request.Body = new MemoryStream(transformedBody);
            request.ContentLength = transformedBody.Length;
            request.ContentType = "application/json; charset=utf-8";

            if (transformContext.ProxyRequest.Content == null)
            {
                return;
            }

            transformContext.ProxyRequest.Content.Headers.ContentLength = transformedBody.Length;
            transformContext.ProxyRequest.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/json")
                {
                    CharSet = Encoding.UTF8.WebName
                };
        });

        return true;
    }
}
