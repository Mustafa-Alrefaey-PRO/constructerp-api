using ConstructErp.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace ConstructErp.Infrastructure.Identity;

/// <summary>
/// Password hashing and verification.
/// </summary>
/// <remarks>
/// Uses ASP.NET Core's <see cref="PasswordHasher{TUser}"/> — PBKDF2-HMAC-SHA512,
/// 100k iterations by default, per-password salt, constant-time comparison —
/// WITHOUT taking the full ASP.NET Core Identity stack.
///
/// That is a deliberate split, and worth stating because it looks like a
/// half-measure:
///
///   - The cryptography is Microsoft's. Nobody here is writing a KDF; that is
///     the part you must never hand-roll.
///   - The schema is ours. Identity brings seven tables and a claims model for
///     multi-role users; this app has one role per user and an organization
///     link, which is two columns. Adopting the whole stack to avoid writing
///     one table would leave the rest of the schema looking like nothing else
///     in the codebase.
///
/// If requirements grow to need email confirmation, 2FA or external logins,
/// migrate to full Identity rather than reimplementing those here.
/// </remarks>
public static class PasswordHashing
{
    private static readonly PasswordHasher<AppUser> Hasher = new();

    public static string Hash(AppUser user, string password) =>
        Hasher.HashPassword(user, password);

    /// <summary>
    /// Verifies a password, and reports whether the stored hash is outdated.
    /// </summary>
    /// <remarks>
    /// <c>SuccessRehashNeeded</c> means the hash was produced by older
    /// parameters than the current ones — the password is correct, and the
    /// caller should re-hash and save it so the account quietly upgrades.
    /// </remarks>
    public static PasswordVerificationResult Verify(AppUser user, string password) =>
        Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
}
