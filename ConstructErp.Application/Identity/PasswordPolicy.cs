namespace ConstructErp.Application.Identity;

/// <summary>
/// What a new password must satisfy: at least 8 characters, with a letter and
/// a digit. Long enough that guessing is impractical behind the login lockout,
/// without composition rules that push people to write passwords down.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    /// <summary>Null when acceptable, otherwise the reason it is not.</summary>
    public static string? Check(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            return $"The new password must be at least {MinLength} characters.";
        }

        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            return "The new password must contain at least one letter and one digit.";
        }

        return null;
    }
}
