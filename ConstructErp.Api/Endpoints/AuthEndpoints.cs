using ConstructErp.Application.Common;
using ConstructErp.Application.Identity;
using ConstructErp.Domain.Identity;
using ConstructErp.Infrastructure.Identity;
using ConstructErp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ConstructErp.Api.Endpoints;

/// <summary>
/// Sign in, stay signed in, sign out.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>After this many failures the account is locked briefly.</summary>
    private const int MaxFailedAttempts = 5;

    private const int LockoutMinutes = 15;

    /// <summary>
    /// A throwaway user used only to burn the same time a real verify costs.
    /// </summary>
    /// <remarks>
    /// Without this, an unknown email returns immediately while a known one
    /// pays for a PBKDF2 verification. That difference is measurable, and it
    /// turns response latency into the account-enumeration oracle the
    /// identical error messages were meant to close.
    /// </remarks>
    private static readonly AppUser TimingDecoy = new()
    {
        Email = "decoy@invalid",
        PasswordHash = PasswordHashing.Hash(new AppUser(), Guid.NewGuid().ToString()),
    };

    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", Login).WithName("Login").AllowAnonymous();
        group.MapPost("/refresh", Refresh).WithName("RefreshToken").AllowAnonymous();
        group.MapPost("/logout", Logout).WithName("Logout").RequireAuthorization();
        group.MapGet("/me", Me).WithName("Me").RequireAuthorization();
        group.MapPut("/me", UpdateProfile).WithName("UpdateProfile").RequireAuthorization();
        group.MapPost("/change-password", ChangePassword).WithName("ChangePassword")
            .RequireAuthorization();

        return group;
    }

    /// <summary>
    /// Exchanges a username (or email) and password for a token pair.
    /// </summary>
    /// <remarks>
    /// Every failure returns the same message. Saying "no such user" versus
    /// "wrong password" turns the login form into a way to enumerate who has
    /// an account, which is worth more to an attacker than it is to a user who
    /// mistyped their own address.
    ///
    /// Lockout is the other half: without it, a correct-password check is just
    /// a slow oracle to brute force.
    /// </remarks>
    private static async Task<IResult> Login(
        LoginRequest body,
        ErpDbContext db,
        TokenService tokens,
        TimeProvider clock,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var login = body.Login?.Trim().ToLowerInvariant() ?? string.Empty;

        // An "@" can only be an email: usernames are not allowed to contain one.
        var byEmail = login.Contains('@');
        var user = await db.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => byEmail ? u.Email == login : u.UserName == login, ct);

        if (user is null || !user.IsActive)
        {
            // Verify against the decoy anyway, so this path costs what the
            // real one costs. The result is discarded.
            PasswordHashing.Verify(TimingDecoy, body.Password ?? string.Empty);

            return InvalidCredentials();
        }

        if (user.IsLockedOut(now))
        {
            return Results.Problem(
                title: "Account temporarily locked.",
                detail: "Too many failed attempts. Try again shortly.",
                statusCode: StatusCodes.Status423Locked);
        }

        var verification = PasswordHashing.Verify(user, body.Password ?? string.Empty);

        if (verification == PasswordVerificationResult.Failed)
        {
            user.FailedAttempts++;

            if (user.FailedAttempts >= MaxFailedAttempts)
            {
                user.LockedUntil = now.AddMinutes(LockoutMinutes);
                user.FailedAttempts = 0;
            }

            await db.SaveChangesAsync(ct);

            return InvalidCredentials();
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // The password is right but the hash used older parameters. Upgrade
            // it now, while the plaintext is in hand — there is no other moment
            // when this is possible.
            user.PasswordHash = PasswordHashing.Hash(user, body.Password!);
        }

        user.FailedAttempts = 0;
        user.LockedUntil = null;
        user.LastLoginAt = now;

        return Results.Ok(await IssueAsync(user, db, tokens, ct));
    }

    /// <summary>
    /// Trades a refresh token for a new pair, revoking the one presented.
    /// </summary>
    /// <remarks>
    /// Rotation on every use. Presenting an already-redeemed token fails,
    /// which is what makes a stolen refresh token stop working as soon as
    /// either party uses it once.
    /// </remarks>
    private static async Task<IResult> Refresh(
        RefreshRequest body,
        ErpDbContext db,
        TokenService tokens,
        TimeProvider clock,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var hash = TokenService.HashRefreshToken(body.RefreshToken ?? string.Empty);

        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .ThenInclude(u => u!.Organization)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored?.User is null || !stored.IsActive(now) || !stored.User.IsActive)
        {
            return Results.Problem(
                title: "Invalid refresh token.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        stored.RevokedAt = now;

        return Results.Ok(await IssueAsync(stored.User, db, tokens, ct));
    }

    /// <summary>
    /// Revokes every refresh token the user holds.
    /// </summary>
    /// <remarks>
    /// Their current access token keeps working until it expires — a JWT
    /// cannot be recalled once signed. That window is why the access token
    /// lifetime is deliberately short.
    /// </remarks>
    private static async Task<IResult> Logout(
        ErpDbContext db, ICurrentUser currentUser, TimeProvider clock, CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var now = clock.GetUtcNow();

        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, now), ct);

        return Results.NoContent();
    }

    /// <summary>
    /// Changes the signed-in user's password.
    /// </summary>
    /// <remarks>
    /// The current password is required even though the caller holds a valid
    /// token: a session left open on a shared machine must not be enough to
    /// take the account over. Wrong guesses count toward the same lockout as
    /// sign-in. On success every refresh token is revoked — any other session
    /// ends — and this session gets a fresh pair so it carries on.
    /// </remarks>
    private static async Task<IResult> ChangePassword(
        ChangePasswordRequest body,
        ErpDbContext db,
        ICurrentUser currentUser,
        TokenService tokens,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var now = clock.GetUtcNow();
        var user = await db.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);

        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (user.IsLockedOut(now))
        {
            return Results.Problem(
                title: "Account temporarily locked.",
                detail: "Too many failed attempts. Try again shortly.",
                statusCode: StatusCodes.Status423Locked);
        }

        if (PasswordHashing.Verify(user, body.CurrentPassword ?? string.Empty)
            == PasswordVerificationResult.Failed)
        {
            user.FailedAttempts++;

            if (user.FailedAttempts >= MaxFailedAttempts)
            {
                user.LockedUntil = now.AddMinutes(LockoutMinutes);
                user.FailedAttempts = 0;
            }

            await db.SaveChangesAsync(ct);

            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["currentPassword"] = ["The current password is incorrect."],
                },
                title: "The current password is incorrect.");
        }

        if (PasswordPolicy.Check(body.NewPassword) is { } reason)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["newPassword"] = [reason] },
                title: reason);
        }

        if (body.NewPassword == body.CurrentPassword)
        {
            const string same = "The new password must be different from the current one.";

            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["newPassword"] = [same] },
                title: same);
        }

        user.PasswordHash = PasswordHashing.Hash(user, body.NewPassword);
        user.FailedAttempts = 0;
        user.LockedUntil = null;

        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, now), ct);

        return Results.Ok(await IssueAsync(user, db, tokens, ct));
    }

    /// <summary>
    /// Updates the signed-in user's display name, username and email.
    /// </summary>
    /// <remarks>
    /// Role and organization are not editable here: those decide what a person
    /// may do and see, so a user must not be able to change their own. The
    /// response carries a fresh token pair, because the access token holds the
    /// name and email.
    /// </remarks>
    private static async Task<IResult> UpdateProfile(
        UpdateProfileRequest body,
        ErpDbContext db,
        ICurrentUser currentUser,
        TokenService tokens,
        CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var user = await db.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);

        if (user is null)
        {
            return Results.Unauthorized();
        }

        var displayName = body.DisplayName?.Trim() ?? string.Empty;
        var userName = UserNames.Normalize(body.UserName);
        var email = body.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var errors = new Dictionary<string, string[]>();

        if (displayName.Length is 0 or > 128)
        {
            errors["displayName"] = ["Enter a name of up to 128 characters."];
        }

        if (!UserNames.IsValid(userName))
        {
            errors["userName"] =
                [$"A username is {UserNames.MinLength}–{UserNames.MaxLength} lowercase letters, digits, dots, underscores or hyphens."];
        }

        if (email.Length > 256 || !System.Net.Mail.MailAddress.TryCreate(email, out var parsed)
            || parsed.Address != email)
        {
            errors["email"] = ["Enter a valid email address."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors, title: errors.Values.First()[0]);
        }

        if (await db.Users.AnyAsync(u => u.Id != userId && u.UserName == userName, ct))
        {
            return Taken("userName", "This username is already taken.");
        }

        if (await db.Users.AnyAsync(u => u.Id != userId && u.Email == email, ct))
        {
            return Taken("email", "This email is already used by another account.");
        }

        user.DisplayName = displayName;
        user.UserName = userName;
        user.Email = email;

        return Results.Ok(await IssueAsync(user, db, tokens, ct));
    }

    private static IResult Taken(string field, string message) => Results.Problem(
        title: message,
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["field"] = field });

    private static async Task<IResult> Me(
        ErpDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var user = await db.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        return user is null ? Results.Unauthorized() : Results.Ok(Describe(user));
    }

    private static async Task<AuthResultDto> IssueAsync(
        AppUser user, ErpDbContext db, TokenService tokens, CancellationToken ct)
    {
        var access = tokens.CreateAccessToken(user);
        var (raw, record) = tokens.CreateRefreshToken(user);

        db.RefreshTokens.Add(record);
        await db.SaveChangesAsync(ct);

        return new AuthResultDto(access.Token, access.ExpiresAt, raw, Describe(user));
    }

    private static CurrentUserDto Describe(AppUser user) => new(
        user.Id,
        user.UserName,
        user.Email,
        user.DisplayName,
        user.Role.ToString(),
        user.OrganizationId,
        user.Organization is null
            ? null
            : new LocalizedTextDto(user.Organization.Name.En, user.Organization.Name.Ar));

    private static IResult InvalidCredentials() => Results.Problem(
        title: "Invalid username or password.",
        statusCode: StatusCodes.Status401Unauthorized);
}
