using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ConstructErp.Application.Common;
using ConstructErp.Application.Equipment;
using ConstructErp.Application.Requests;

namespace ConstructErp.Tests;

/// <summary>
/// The workflow over HTTP, including the rule the product exists to enforce.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class RequestEndpointTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_request_walks_the_full_lifecycle()
    {
        var client = factory.CreateClient();
        var request = await CreateDraftAsync(client);

        Assert.Equal("Draft", request.Status);
        Assert.Equal("Request", request.Stage);
        Assert.Equal(12, request.Checks.Count);
        Assert.All(request.Checks, check => Assert.False(check.Passed));

        // Submit is refused while the gate is unticked.
        var early = await client.PostAsync($"/api/requests/{request.Id}/submit", null);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        request = await PassAllAsync(client, request, "PreRequest");
        Assert.Contains("submit", request.AvailableActions);

        request = await PostAsync(client, $"/api/requests/{request.Id}/submit");
        Assert.Equal("Submitted", request.Status);
        Assert.Equal("Approval", request.Stage);

        request = await PostAsync(client, $"/api/requests/{request.Id}/approve");
        Assert.Equal("Approved", request.Status);

        // Receiving is refused until the second gate passes.
        var tooEarly = await client.PostAsync($"/api/requests/{request.Id}/receive", null);
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);

        request = await PassAllAsync(client, request, "PreReceiving");
        request = await PostAsync(client, $"/api/requests/{request.Id}/receive");
        Assert.Equal("Received", request.Status);

        request = await PostAsync(client, $"/api/requests/{request.Id}/inspect",
            new InspectRequest(true, null));
        Assert.Equal("Ready to Use", request.Status);
        Assert.Equal("Inspection", request.Stage);
    }

    [Fact]
    public async Task Equipment_cannot_be_set_to_working_without_a_completed_request()
    {
        var client = factory.CreateClient();

        // An asset that is not already working and has no completed request.
        var assets = await GetAsync<List<EquipmentDto>>(client, "/api/equipment");
        var asset = assets.First(a => a.Status != "Working");

        var attempt = await client.PutAsJsonAsync(
            $"/api/equipment/{asset.Id}", ToSave(asset, "Working"), Json);

        Assert.Equal(HttpStatusCode.Conflict, attempt.StatusCode);

        var body = await attempt.Content.ReadAsStringAsync();
        Assert.Contains("pre-use inspection", body);
    }

    [Fact]
    public async Task Equipment_can_be_set_to_working_once_its_request_is_ready()
    {
        var client = factory.CreateClient();
        var assets = await GetAsync<List<EquipmentDto>>(client, "/api/equipment");
        var asset = assets.First(a => a.Status != "Working");

        var request = await CreateDraftAsync(client, asset.Id);
        request = await PassAllAsync(client, request, "PreRequest");
        request = await PostAsync(client, $"/api/requests/{request.Id}/submit");
        request = await PostAsync(client, $"/api/requests/{request.Id}/approve");
        request = await PassAllAsync(client, request, "PreReceiving");
        request = await PostAsync(client, $"/api/requests/{request.Id}/receive");
        await PostAsync(client, $"/api/requests/{request.Id}/inspect", new InspectRequest(true, null));

        var allowed = await client.PutAsJsonAsync(
            $"/api/equipment/{asset.Id}", ToSave(asset, "Working"), Json);

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        // Put it back so the shared database stays as the other tests expect.
        await client.PutAsJsonAsync($"/api/equipment/{asset.Id}", ToSave(asset, asset.Status), Json);
    }

    [Fact]
    public async Task A_failed_inspection_parks_the_request_rather_than_releasing_it()
    {
        var client = factory.CreateClient();
        var request = await CreateDraftAsync(client);

        request = await PassAllAsync(client, request, "PreRequest");
        request = await PostAsync(client, $"/api/requests/{request.Id}/submit");
        request = await PostAsync(client, $"/api/requests/{request.Id}/approve");
        request = await PassAllAsync(client, request, "PreReceiving");
        request = await PostAsync(client, $"/api/requests/{request.Id}/receive");

        request = await PostAsync(client, $"/api/requests/{request.Id}/inspect",
            new InspectRequest(false, new LocalizedTextDto("Hydraulic leak", "تسرب هيدروليكي")));

        Assert.Equal("Inspection Pending", request.Status);
        Assert.Equal("Hydraulic leak", request.RejectionReason?.En);
    }

    [Fact]
    public async Task Rejection_requires_a_reason()
    {
        var client = factory.CreateClient();
        var request = await CreateDraftAsync(client);

        request = await PassAllAsync(client, request, "PreRequest");
        request = await PostAsync(client, $"/api/requests/{request.Id}/submit");

        var blank = await client.PostAsJsonAsync($"/api/requests/{request.Id}/reject",
            new RejectRequest(new LocalizedTextDto(string.Empty, null)), Json);

        // Nobody can act on "rejected" with no explanation.
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

        var rejected = await PostAsync(client, $"/api/requests/{request.Id}/reject",
            new RejectRequest(new LocalizedTextDto("Over budget", "تجاوز الميزانية")));

        Assert.Equal("Rejected", rejected.Status);
        Assert.Equal("Over budget", rejected.RejectionReason?.En);
    }

    [Fact]
    public async Task An_approved_request_can_no_longer_be_edited()
    {
        var client = factory.CreateClient();
        var request = await CreateDraftAsync(client);

        request = await PassAllAsync(client, request, "PreRequest");
        request = await PostAsync(client, $"/api/requests/{request.Id}/submit");
        request = await PostAsync(client, $"/api/requests/{request.Id}/approve");

        var edit = await client.PutAsJsonAsync($"/api/requests/{request.Id}",
            new SaveRequestRequest(request.Code, request.EquipmentId, request.ProjectId,
                "Owned", "Someone Else", null, null, null, null, 999m), Json);

        // Changing the asset or cost of an approved request would invalidate
        // the approval that was given.
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
    }

    [Fact]
    public async Task Return_date_before_required_date_is_rejected()
    {
        var client = factory.CreateClient();
        var assets = await GetAsync<List<EquipmentDto>>(client, "/api/equipment");

        var body = new SaveRequestRequest(
            NextCode(), assets[0].Id, null, "Owned", "K. Mansour",
            new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 10), null, null, 100m);

        var response = await client.PostAsJsonAsync("/api/requests", body, Json);

        // Only checkable because these are real dates now, not "Jul 20".
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- helpers -------------------------------------------------------------

    private static string NextCode() => $"REQ-T{Random.Shared.Next(10000, 99999)}";

    private async Task<RequestDto> CreateDraftAsync(HttpClient client, Guid? equipmentId = null)
    {
        var assets = await GetAsync<List<EquipmentDto>>(client, "/api/equipment");

        var body = new SaveRequestRequest(
            NextCode(),
            equipmentId ?? assets[0].Id,
            null,
            "Owned",
            "K. Mansour",
            new DateOnly(2026, 7, 20),
            new DateOnly(2026, 7, 28),
            new LocalizedTextDto("East Gate", "البوابة الشرقية"),
            new LocalizedTextDto("Foundation works", "أعمال الأساسات"),
            5520.125m);

        var response = await client.PostAsJsonAsync("/api/requests", body, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<RequestDto>(Json))!;
    }

    private async Task<RequestDto> PassAllAsync(HttpClient client, RequestDto request, string kind)
    {
        var latest = request;

        foreach (var check in request.Checks.Where(c => c.Kind == kind))
        {
            latest = await PutAsync(client,
                $"/api/requests/{request.Id}/checks/{check.Id}", new SetCheckRequest(true));
        }

        return latest;
    }

    private static async Task<RequestDto> PostAsync<T>(HttpClient client, string url, T body)
    {
        var response = await client.PostAsJsonAsync(url, body, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<RequestDto>(Json))!;
    }

    private static async Task<RequestDto> PostAsync(HttpClient client, string url)
    {
        var response = await client.PostAsync(url, null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<RequestDto>(Json))!;
    }

    private static async Task<RequestDto> PutAsync<T>(HttpClient client, string url, T body)
    {
        var response = await client.PutAsJsonAsync(url, body, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<RequestDto>(Json))!;
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<T>(url, Json))!;

    private static SaveEquipmentRequest ToSave(EquipmentDto asset, string status) => new(
        asset.Code,
        asset.Name,
        asset.EquipmentTypeId,
        asset.Ownership,
        asset.ProjectId,
        status,
        asset.Utilization,
        asset.DailyCost,
        asset.NextAction);
}
