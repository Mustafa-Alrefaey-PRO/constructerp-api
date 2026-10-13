using System.Text.RegularExpressions;

namespace ConstructErp.Domain.Identity;

/// <summary>
/// Rules for the username people sign in with.
/// </summary>
/// <remarks>
/// Lowercase letters, digits, dot, underscore and hyphen, 3–32 characters.
/// No "@", so a sign-in identifier is unambiguous: with an "@" it is an
/// email, without one it is a username.
/// </remarks>
public static partial class UserNames
{
    public const int MinLength = 3;

    public const int MaxLength = 32;

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

    public static bool IsValid(string? value) =>
        value is { Length: >= MinLength and <= MaxLength } && Allowed().IsMatch(value);

    /// <summary>
    /// The part of an email before the "@", cleaned to the allowed characters.
    /// Used to give seeded and pre-existing accounts a username.
    /// </summary>
    public static string FromEmail(string email)
    {
        var local = Normalize(email).Split('@')[0];
        var cleaned = Disallowed().Replace(local, string.Empty);

        if (cleaned.Length > MaxLength)
        {
            cleaned = cleaned[..MaxLength];
        }

        return cleaned.Length >= MinLength ? cleaned : $"user.{cleaned}".TrimEnd('.');
    }

    [GeneratedRegex("^[a-z0-9._-]+$")]
    private static partial Regex Allowed();

    [GeneratedRegex("[^a-z0-9._-]")]
    private static partial Regex Disallowed();
}
