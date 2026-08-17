using System.ComponentModel.DataAnnotations;

namespace PassingTrace.Identity.AuthorizationServer.Mobile;

public sealed record CreateRegistrationIntentRequest(
    [Required] string Username,
    [Required] string ClientId,
    [Required] string RedirectUri,
    [Required] string CodeChallenge,
    string? State,
    string? Nonce);

public sealed record CompleteRegistrationRequest(
    Guid IntentId,
    [Required] string Username,
    [Required] string Password,
    [Required] string BootstrapCode,
    string DeviceName = "My Android");

public sealed record CreateAuthorizationLaunchRequest(
    [Required] string ClientId,
    [Required] string RedirectUri,
    [Required] string CodeChallenge,
    string? State,
    string? Nonce,
    Guid DeviceId,
    [Required] string DeviceSecret);

public sealed record RegistrationIntentResponse(
    Guid IntentId,
    string RequestHash,
    int ExpiresIn);

public sealed record RegistrationResponse(
    string AuthorizeUrl,
    int ExpiresIn,
    Guid DeviceId,
    string DeviceSecret);

public sealed record AuthorizationLaunchResponse(string AuthorizeUrl, int ExpiresIn);
