using ConstructErp.Domain.Requests;

namespace ConstructErp.Api.Endpoints;

/// <summary>The body of every refusal: English fallback, stable code, values for the message.</summary>
public sealed record ErrorBody(string Error, string Code, IReadOnlyDictionary<string, string>? Params = null);

/// <summary>
/// The only way endpoints refuse: always with a code from
/// <see cref="Domain.Common.ErrorCodes"/>, so clients can show the reason in
/// the user's language. See ErrorCodes for the rules on codes.
/// </summary>
public static class ApiErrors
{
    public static IResult BadRequest(string code, string message, IReadOnlyDictionary<string, string>? args = null) =>
        Results.BadRequest(new ErrorBody(message, code, args));

    public static IResult Conflict(string code, string message, IReadOnlyDictionary<string, string>? args = null) =>
        Results.Conflict(new ErrorBody(message, code, args));

    /// <summary>A workflow rule said no.</summary>
    public static IResult Refused(TransitionResult result) =>
        Results.Conflict(new ErrorBody(result.Reason ?? string.Empty, result.Code ?? ErrorCodesFallback, result.Params));

    public static IReadOnlyDictionary<string, string> Args(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value);

    /// <summary>Only reached if a rule forgot its code; the contract test catches that.</summary>
    private const string ErrorCodesFallback = "refused";
}
