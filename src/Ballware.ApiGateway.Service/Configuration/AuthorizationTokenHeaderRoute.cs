using Microsoft.AspNetCore.Http;

namespace Ballware.ApiGateway.Service.Configuration;

public sealed record AuthorizationTokenHeaderRoute(PathString PathPrefix, string HeaderName);
