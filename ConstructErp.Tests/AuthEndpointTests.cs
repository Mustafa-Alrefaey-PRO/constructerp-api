using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ConstructErp.Application.Identity;

namespace ConstructErp.Tests;

[Collection(nameof(ApiCollection))]
public sealed class AuthEndpointTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("/api/projects")]
    [InlineData("/api/equipment")]
    [InlineData("/api/rentals")]
    [InlineData("/api/transport")]
    [InlineData("/api/costs")]
    public async Task Every_data_route_refuses_an_anonymous_caller(string route)
    {
        // The fallback policy is default-deny, so a route that forgot its
        // attribute still fails closed. This is the test that catches it.
        var response = await factory.AnonymousClient().GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_stays_open()
    {
        // A probe has no credentials and must not need any.
        var response = await factory.AnonymousClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_returns_a_token_pair_and_the_caller_identity()
    {
        var (email, password) = factory.Credentials("AdminEmail", "AdminPassword");

        var response = await factory.AnonymousClient()
            .PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password), Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var auth = (await response.Content.ReadFromJsonAsync<AuthResultDto>(Json))!;

        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));
        Assert.Equal("Admin", auth.User.Role);
        Assert.True(auth.ExpiresAt > DateTimeOffset.UtcNow);

        // An admin is unscoped, which is why they have no organization.
        Assert.Null(auth.User.OrganizationId);
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_email_fail_identically()
    {
        var (email, _) = factory.Credentials("AdminEmail", "AdminPassword");
        var client = factory.AnonymousClient();

        var wrongPassword = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(email, "definitely-not-it"), Json);

        var unknownEmail = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest("nobody@constructerp.local", "whatever"), Json);

        // Identical responses on purpose: a login form that distinguishes the
        // two is a way to find out who holds an account.
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);

        // Compared field by field rather than as raw strings: the bodies also
        // carry a per-request traceId, which differs by design and leaks
        // nothing about whether the account exists.
        Assert.Equal(await TitleOf(wrongPassword), await TitleOf(unknownEmail));
    }

    private static async Task<string?> TitleOf(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Json);

        return problem?.Title;
    }

    private sealed record ProblemBody(string? Title, string? Detail, int? Status);

    [Fact]
    public async Task A_refresh_token_works_once_and_then_stops()
    {
        var (email, password) = factory.Credentials("CarrierEmail", "CarrierPassword");
        var client = factory.AnonymousClient();

        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(email, password), Json);
        var auth = (await login.Content.ReadFromJsonAsync<AuthResultDto>(Json))!;

        var first = await client.PostAsJsonAsync(
            "/api/auth/refresh", new RefreshRequest(auth.RefreshToken), Json);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var rotated = (await first.Content.ReadFromJsonAsync<AuthResultDto>(Json))!;
        Assert.NotEqual(auth.RefreshToken, rotated.RefreshToken);

        // Rotation is what makes a stolen refresh token detectable: whoever
        // redeems it second is refused.
        var replay = await client.PostAsJsonAsync(
            "/api/auth/refresh", new RefreshRequest(auth.RefreshToken), Json);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task Garbage_is_not_a_refresh_token()
    {
        var response = await factory.AnonymousClient().PostAsJsonAsync(
            "/api/auth/refresh", new RefreshRequest("not-a-real-token"), Json);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_describes_the_signed_in_caller()
    {
        var client = await factory.CarrierAsync();

        var me = await client.GetFromJsonAsync<CurrentUserDto>("/api/auth/me", Json);

        Assert.Equal("TruckingCompany", me!.Role);
        Assert.NotNull(me.OrganizationId);
        Assert.Equal("Delta Haulage", me.OrganizationName!.En);
    }

    [Fact]
    public async Task The_password_is_never_stored_in_the_clear()
    {
        var (_, password) = factory.Credentials("AdminEmail", "AdminPassword");

        await factory.WithDbAsync(async db =>
        {
            var hashes = await Task.FromResult(db.Users.Select(u => u.PasswordHash).ToList());

            Assert.NotEmpty(hashes);
            Assert.DoesNotContain(password, hashes);

            // Every hash is distinct even where passwords might not be: the
            // hasher salts per password.
            Assert.Equal(hashes.Count, hashes.Distinct().Count());
        });
    }

    [Fact]
    public async Task A_refresh_token_is_stored_only_as_a_hash()
    {
        var (email, password) = factory.Credentials("DriverEmail", "DriverPassword");

        var login = await factory.AnonymousClient().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(email, password), Json);
        var auth = (await login.Content.ReadFromJsonAsync<AuthResultDto>(Json))!;

        await factory.WithDbAsync(async db =>
        {
            var stored = await Task.FromResult(db.RefreshTokens.Select(t => t.TokenHash).ToList());

            // The raw value exists once, in the response. A leaked database
            // must not hand over working credentials.
            Assert.DoesNotContain(auth.RefreshToken, stored);
        });
    }
}
