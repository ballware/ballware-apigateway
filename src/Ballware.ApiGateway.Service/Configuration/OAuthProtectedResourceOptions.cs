using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ballware.ApiGateway.Service.Configuration;

public sealed record OAuthProtectedResourceRoute(PathString Path, OAuthProtectedResourceOptions Options, IConfigurationSection MetadataSection);

public sealed class OAuthProtectedResourceOptions
{
    public string? Resource { get; set; }

    [MinLength(1)]
    public string[]? AuthorizationServers { get; set; }

    public string[]? ScopesSupported { get; set; }

    public string[]? BearerMethodsSupported { get; set; }

    public string? ResourceName { get; set; }

    public string? ResourceDocumentation { get; set; }

    public string? ResourcePolicyUri { get; set; }

    public string? ResourceTosUri { get; set; }

    public string? JwksUri { get; set; }

}

public sealed class OAuthProtectedResourceMetadata
{
    [JsonPropertyName("resource")]
    public required string Resource { get; init; }

    [JsonPropertyName("authorization_servers")]
    public required IReadOnlyList<string> AuthorizationServers { get; init; }

    [JsonPropertyName("scopes_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ScopesSupported { get; init; }

    [JsonPropertyName("bearer_methods_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? BearerMethodsSupported { get; init; }

    [JsonPropertyName("resource_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResourceName { get; init; }

    [JsonPropertyName("resource_documentation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResourceDocumentation { get; init; }

    [JsonPropertyName("resource_policy_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResourcePolicyUri { get; init; }

    [JsonPropertyName("resource_tos_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResourceTosUri { get; init; }

    [JsonPropertyName("jwks_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JwksUri { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalMetadata { get; init; }
}
