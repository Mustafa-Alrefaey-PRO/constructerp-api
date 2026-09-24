using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ConstructErp.Application.Transport;

namespace ConstructErp.Tests;

/// <summary>
/// Row-level scoping: whose records each role can actually read.
/// </summary>
/// <remarks>
/// These are the tests that matter most in this slice. A role check only says
/// which endpoints you may call; it says nothing about whose rows come back.
/// Getting that wrong is invisible with one carrier and a data leak with two.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class ScopingTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_carrier_sees_fewer_moves_than_an_admin()
    {
        var all = await MovesAsync(await factory.AdminAsync());
        var mine = await MovesAsync(await factory.CarrierAsync());

        Assert.NotEmpty(all);
        Assert.NotEmpty(mine);

        // Strictly fewer, not merely "some". If the seed gave every move to
        // one carrier, an unfiltered query would pass this while proving
        // nothing — TRP-5003 is deliberately in-house so a row is excluded.
        Assert.True(
            mine.Count < all.Count,
            $"Carrier saw {mine.Count} of {all.Count} moves; expected the in-house move to be hidden.");

        Assert.All(mine, move => Assert.NotNull(move.CarrierId));
    }

    [Fact]
    public async Task A_carrier_cannot_reach_another_organisations_move_by_id()
    {
        var admin = await factory.AdminAsync();
        var carrier = await factory.CarrierAsync();

        var mine = await MovesAsync(carrier);
        var hidden = (await MovesAsync(admin)).First(m => mine.All(x => x.Id != m.Id));

        // Guessing an id must not work either. The filter applies to every
        // query, not just the list endpoint — which is the whole reason it
        // lives on the model rather than in each handler.
        var response = await carrier.GetAsync($"/api/transport/{hidden.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_carrier_cannot_act_on_a_move_it_cannot_see()
    {
        var admin = await factory.AdminAsync();
        var carrier = await factory.CarrierAsync();

        var mine = await MovesAsync(carrier);
        var hidden = (await MovesAsync(admin)).First(m => mine.All(x => x.Id != m.Id));

        var response = await carrier.PostAsJsonAsync(
            $"/api/transport/{hidden.Id}/approve", new TransportEventRequest(null), Json);

        // Not 403: as far as this caller is concerned the row does not exist,
        // and saying "forbidden" would confirm that it does.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_driver_sees_only_their_own_assignments()
    {
        var driver = await MovesAsync(await factory.DriverAsync());
        var carrier = await MovesAsync(await factory.CarrierAsync());

        Assert.NotEmpty(driver);

        // A driver is scoped tighter than their own carrier, never wider.
        Assert.True(driver.Count <= carrier.Count);
        Assert.All(driver, move => Assert.NotNull(move.DriverId));
    }

    [Theory]
    [InlineData("/api/projects")]
    [InlineData("/api/equipment")]
    [InlineData("/api/rentals")]
    [InlineData("/api/vendors")]
    [InlineData("/api/costs")]
    [InlineData("/api/requests")]
    public async Task A_carrier_is_refused_the_internal_modules(string route)
    {
        // Scoping decides which rows; the role decides which doors. A haulage
        // contractor has no business in the fleet register or the cost ledger.
        var response = await (await factory.CarrierAsync()).GetAsync(route);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_driver_is_refused_the_internal_modules()
    {
        var response = await (await factory.DriverAsync()).GetAsync("/api/projects");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_admin_is_not_scoped_to_any_organisation()
    {
        var all = await MovesAsync(await factory.AdminAsync());

        // Includes the in-house move that belongs to no carrier at all.
        Assert.Contains(all, move => move.CarrierId is null);
    }

    private static async Task<List<TransportMoveDto>> MovesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<TransportMoveDto>>("/api/transport", Json))!;
}
