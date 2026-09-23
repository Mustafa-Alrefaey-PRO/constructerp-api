using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ConstructErp.Application.Common;
using ConstructErp.Application.Equipment;
using ConstructErp.Application.Projects;
using ConstructErp.Application.Transport;

namespace ConstructErp.Tests;

[Collection(nameof(ApiCollection))]
public sealed class TransportEndpointTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Every_listed_status_agrees_with_its_own_timestamps()
    {
        var moves = await GetAsync<List<TransportMoveDto>>("/api/transport");

        Assert.NotEmpty(moves);

        foreach (var move in moves)
        {
            var expected = move switch
            {
                { CancelledAt: not null } => "Cancelled",
                { ArrivedAt: not null } => "Completed",
                { DepartedAt: not null } => "In Transit",
                { ApprovedAt: not null } => "Scheduled",
                _ => "Awaiting Approval",
            };

            Assert.Equal(expected, move.Status);
        }
    }

    [Fact]
    public async Task Move_resolves_its_asset_through_the_foreign_key()
    {
        var move = (await GetAsync<List<TransportMoveDto>>("/api/transport")).First();

        Assert.NotEqual(Guid.Empty, move.EquipmentId);
        Assert.False(string.IsNullOrWhiteSpace(move.EquipmentCode));
        Assert.False(string.IsNullOrWhiteSpace(move.EquipmentName.En));
    }

    [Fact]
    public async Task A_move_cannot_depart_before_it_is_approved()
    {
        var client = factory.CreateClient();
        var move = await CreateAsync();

        Assert.Equal("Awaiting Approval", move.Status);
        Assert.DoesNotContain("depart", move.AvailableActions);

        // There is no status field to write, so the only way to try is the
        // transition itself — and it refuses.
        var refused = await client.PostAsJsonAsync(
            $"/api/transport/{move.Id}/depart", new TransportEventRequest(null), Json);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        var body = await refused.Content.ReadFromJsonAsync<ErrorBody>(Json);
        Assert.Contains("approved", body!.Error);

        // Unchanged: a refused transition records nothing.
        var after = await GetAsync<TransportMoveDto>($"/api/transport/{move.Id}");
        Assert.Equal("Awaiting Approval", after.Status);
        Assert.Null(after.DepartedAt);

        await client.DeleteAsync($"/api/transport/{move.Id}");
    }

    [Fact]
    public async Task The_full_lifecycle_moves_only_through_its_transitions()
    {
        var client = factory.CreateClient();
        var move = await CreateAsync();

        var approved = await TransitionAsync(move.Id, "approve");
        Assert.Equal("Scheduled", approved.Status);
        Assert.NotNull(approved.ApprovedAt);
        Assert.Contains("depart", approved.AvailableActions);

        var departed = await TransitionAsync(move.Id, "depart");
        Assert.Equal("In Transit", departed.Status);

        var arrived = await TransitionAsync(move.Id, "arrive");
        Assert.Equal("Completed", arrived.Status);

        // Completed is terminal: nothing further is offered.
        Assert.Empty(arrived.AvailableActions);

        var cancelAttempt = await client.PostAsJsonAsync(
            $"/api/transport/{move.Id}/cancel", new TransportEventRequest(null), Json);
        Assert.Equal(HttpStatusCode.Conflict, cancelAttempt.StatusCode);

        await client.DeleteAsync($"/api/transport/{move.Id}");
    }

    [Fact]
    public async Task Approving_twice_is_refused()
    {
        var client = factory.CreateClient();
        var move = await CreateAsync();

        await TransitionAsync(move.Id, "approve");

        var again = await client.PostAsJsonAsync(
            $"/api/transport/{move.Id}/approve", new TransportEventRequest(null), Json);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        await client.DeleteAsync($"/api/transport/{move.Id}");
    }

    [Fact]
    public async Task A_missed_slot_is_reported_as_late_without_anyone_flagging_it()
    {
        var client = factory.CreateClient();

        // Booked for two hours ago, approved, never departed.
        var move = await CreateAsync(scheduledInHours: -2);
        await TransitionAsync(move.Id, "approve");

        var late = await GetAsync<List<TransportMoveDto>>("/api/transport?late=true");

        Assert.Contains(late, m => m.Id == move.Id);
        Assert.True(late.First(m => m.Id == move.Id).IsLate);

        // Recording the departure clears it — no "late" field was ever set.
        await TransitionAsync(move.Id, "depart");

        var after = await GetAsync<List<TransportMoveDto>>("/api/transport?late=true");
        Assert.DoesNotContain(after, m => m.Id == move.Id);

        await client.DeleteAsync($"/api/transport/{move.Id}");
    }

    [Fact]
    public async Task Cancelling_stops_the_move_and_takes_it_out_of_the_open_list()
    {
        var client = factory.CreateClient();
        var move = await CreateAsync();

        var cancelled = await TransitionAsync(move.Id, "cancel");
        Assert.Equal("Cancelled", cancelled.Status);

        var open = await GetAsync<List<TransportMoveDto>>("/api/transport?open=true");
        Assert.DoesNotContain(open, m => m.Id == move.Id);

        await client.DeleteAsync($"/api/transport/{move.Id}");
    }

    [Fact]
    public async Task Move_round_trips_arabic_and_three_decimal_money()
    {
        var client = factory.CreateClient();
        var (asset, project) = await ReferencesAsync();
        var code = $"TRP-T{Random.Shared.Next(1000, 9999)}";

        var request = new SaveTransportMoveRequest(
            code, asset.Id, project.Id,
            new LocalizedTextDto("Yard A", "الساحة أ"),
            new LocalizedTextDto("Service Center", "مركز الخدمة"),
            "Return move",
            DateTimeOffset.UtcNow.AddHours(6),
            1875.125m,
            new LocalizedTextDto("Escort required", "مطلوب مرافقة"));

        // Explicit UTF-8, as everywhere Arabic is posted.
        var content = new StringContent(
            JsonSerializer.Serialize(request, Json), Encoding.UTF8, "application/json");

        var created = await client.PostAsync("/api/transport", content);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var move = (await created.Content.ReadFromJsonAsync<TransportMoveDto>(Json))!;

        Assert.Equal("الساحة أ", move.Origin.Ar);
        Assert.Equal("مركز الخدمة", move.Destination.Ar);
        Assert.Equal(1875.125m, move.Cost);
        Assert.Equal("Return move", move.Kind);

        var deleted = await client.DeleteAsync($"/api/transport/{move.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var afterDelete = await client.GetAsync($"/api/transport/{move.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Theory]
    [InlineData("Nonsense kind", 100)]
    [InlineData("Delivery", -5)]
    public async Task An_invalid_move_is_rejected(string kind, decimal cost)
    {
        var (asset, project) = await ReferencesAsync();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/transport",
            new SaveTransportMoveRequest(
                $"TRP-T{Random.Shared.Next(1000, 9999)}", asset.Id, project.Id,
                new LocalizedTextDto("A", null), new LocalizedTextDto("B", null),
                kind, DateTimeOffset.UtcNow.AddHours(2), cost, null),
            Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record ErrorBody(string Error);

    private async Task<TransportMoveDto> CreateAsync(double scheduledInHours = 6)
    {
        var (asset, project) = await ReferencesAsync();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/transport",
            new SaveTransportMoveRequest(
                $"TRP-T{Random.Shared.Next(10000, 99999)}", asset.Id, project.Id,
                new LocalizedTextDto("Yard A", "الساحة أ"),
                new LocalizedTextDto("Site", "الموقع"),
                "Delivery", DateTimeOffset.UtcNow.AddHours(scheduledInHours), 500m, null),
            Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<TransportMoveDto>(Json))!;
    }

    private async Task<TransportMoveDto> TransitionAsync(Guid id, string action)
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/transport/{id}/{action}", new TransportEventRequest(null), Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<TransportMoveDto>(Json))!;
    }

    private async Task<(EquipmentDto Asset, ProjectDto Project)> ReferencesAsync()
    {
        var asset = (await GetAsync<List<EquipmentDto>>("/api/equipment")).First();
        var project = (await GetAsync<List<ProjectDto>>("/api/projects")).First();

        return (asset, project);
    }

    private async Task<T> GetAsync<T>(string url) =>
        (await factory.CreateClient().GetFromJsonAsync<T>(url, Json))!;
}
