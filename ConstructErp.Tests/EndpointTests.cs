using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ConstructErp.Application.Equipment;
using ConstructErp.Application.Projects;

namespace ConstructErp.Tests;

[Collection(nameof(ApiCollection))]
public sealed class EndpointTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Health_reports_ok()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Equipment_list_resolves_its_project_through_the_foreign_key()
    {
        var equipment = await GetAsync<List<EquipmentDto>>("/api/equipment");

        Assert.NotEmpty(equipment);

        var assigned = equipment.First(e => e.ProjectId is not null);

        // Resolved by id, and the display fields come along so a row needs no
        // second request to render.
        Assert.NotNull(assigned.ProjectCode);
        Assert.NotNull(assigned.ProjectName);
    }

    [Fact]
    public async Task Equipment_list_returns_both_languages()
    {
        var equipment = await GetAsync<List<EquipmentDto>>("/api/equipment");
        var crane = equipment.First(e => e.Code == "EQ-104");

        // Both languages on every response: the client's language toggle must
        // not require a refetch.
        Assert.Equal("Crawler Crane 80T", crane.Name.En);
        Assert.False(string.IsNullOrWhiteSpace(crane.Name.Ar));
    }

    [Fact]
    public async Task Project_round_trips_arabic_and_three_decimal_money()
    {
        var client = factory.CreateClient();
        var code = $"PRJ-T{Random.Shared.Next(1000, 9999)}";

        var request = new SaveProjectRequest(
            code,
            new(En: "Seaport Terminal", Ar: "محطة الميناء"),
            new(En: "Port Authority", Ar: "هيئة الموانئ"),
            "N. Saleh",
            new(En: "Shuwaikh", Ar: "الشويخ"),
            "Active",
            412000.125m,
            5,
            null,
            null);

        // Explicit UTF-8 encoding. Anything less and the Arabic is mangled
        // before it leaves the client, which looks exactly like a server bug.
        var content = new StringContent(
            JsonSerializer.Serialize(request, Json), Encoding.UTF8, "application/json");

        var created = await client.PostAsync("/api/projects", content);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var project = (await created.Content.ReadFromJsonAsync<ProjectDto>(Json))!;

        Assert.Equal("محطة الميناء", project.Name.Ar);
        Assert.Equal(412000.125m, project.Budget);

        var deleted = await client.DeleteAsync($"/api/projects/{project.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var afterDelete = await client.GetAsync($"/api/projects/{project.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Duplicate_project_code_is_rejected_as_a_conflict()
    {
        var client = factory.CreateClient();
        var existing = (await GetAsync<List<ProjectDto>>("/api/projects")).First();

        var request = new SaveProjectRequest(
            existing.Code,
            new(En: "Clash", Ar: null),
            new(En: "Client", Ar: null),
            null,
            null,
            "Active",
            1m,
            0,
            null,
            null);

        var response = await client.PostAsJsonAsync("/api/projects", request, Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("Nonsense", 1, "unknown status")]
    [InlineData("Active", -5, "negative budget")]
    public async Task Invalid_project_is_rejected_with_a_reason(
        string status, decimal budget, string because)
    {
        var request = new SaveProjectRequest(
            $"PRJ-X{Random.Shared.Next(1000, 9999)}",
            new(En: "Invalid", Ar: null),
            new(En: "Client", Ar: null),
            null,
            null,
            status,
            budget,
            0,
            null,
            null);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/projects", request, Json);

        // A 400 naming the problem, not a 500 carrying a constraint violation.
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, because);
    }

    [Fact]
    public async Task Equipment_pointing_at_an_unknown_project_is_rejected()
    {
        var types = await GetAsync<List<EquipmentTypeDto>>("/api/equipment/types");

        var request = new SaveEquipmentRequest(
            $"EQ-X{Random.Shared.Next(1000, 9999)}",
            new(En: "Orphan", Ar: null),
            types[0].Id,
            "Owned",
            Guid.NewGuid(),
            "Idle",
            10,
            1m,
            null);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/equipment", request, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Out_of_range_utilization_is_clamped_rather_than_refused()
    {
        var client = factory.CreateClient();
        var types = await GetAsync<List<EquipmentTypeDto>>("/api/equipment/types");

        var request = new SaveEquipmentRequest(
            $"EQ-C{Random.Shared.Next(1000, 9999)}",
            new(En: "Clamped", Ar: null),
            types[0].Id,
            "Owned",
            null,
            "Idle",
            150,
            1m,
            null);

        var response = await client.PostAsJsonAsync("/api/equipment", request, Json);
        var created = (await response.Content.ReadFromJsonAsync<EquipmentDto>(Json))!;

        // A typed 150 is an obvious slip, not a reason to block the save.
        Assert.Equal(100, created.Utilization);

        await client.DeleteAsync($"/api/equipment/{created.Id}");
    }

    private async Task<T> GetAsync<T>(string url) =>
        (await factory.CreateClient().GetFromJsonAsync<T>(url, Json))!;
}
