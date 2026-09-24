using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ConstructErp.Application.Common;
using ConstructErp.Application.Projects;

namespace ConstructErp.Tests;

[Collection(nameof(ApiCollection))]
public sealed class CostEndpointTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Project_spend_is_summed_from_cost_entries()
    {
        var projects = await GetAsync<List<ProjectDto>>("/api/projects");
        var project = projects.First(p => p.Code == "PRJ-1001");

        var entries = await GetAsync<List<CostEntryDto>>($"/api/costs?projectId={project.Id}");

        // The project's totals are a projection of these rows, never stored, so
        // a total cannot disagree with its own detail.
        Assert.Equal(
            entries.Where(e => e.Category == "Equipment").Sum(e => e.Amount),
            project.EquipmentSpend);
        Assert.Equal(
            entries.Where(e => e.Category == "Transport").Sum(e => e.Amount),
            project.TransportSpend);
        Assert.Equal(
            entries.Where(e => e.Category == "Extras").Sum(e => e.Amount),
            project.ExtraSpend);
    }

    [Fact]
    public async Task Adding_an_entry_moves_the_project_total()
    {
        var client = await factory.AdminAsync();
        var project = (await GetAsync<List<ProjectDto>>("/api/projects"))
            .First(p => p.Code == "PRJ-1032");
        var before = project.ExtraSpend;

        var created = await client.PostAsJsonAsync("/api/costs", new SaveCostEntryRequest(
            project.Id, "Extras", 1250.375m, new DateOnly(2026, 8, 1),
            new LocalizedTextDto("Standby", "انتظار")), Json);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var entry = (await created.Content.ReadFromJsonAsync<CostEntryDto>(Json))!;

        // Three decimals survive, as everywhere else money is handled.
        Assert.Equal(1250.375m, entry.Amount);

        var after = (await GetAsync<List<ProjectDto>>("/api/projects"))
            .First(p => p.Id == project.Id);
        Assert.Equal(before + 1250.375m, after.ExtraSpend);

        await client.DeleteAsync($"/api/costs/{entry.Id}");

        var restored = (await GetAsync<List<ProjectDto>>("/api/projects"))
            .First(p => p.Id == project.Id);
        Assert.Equal(before, restored.ExtraSpend);
    }

    [Theory]
    [InlineData("Nonsense", 10)]
    [InlineData("Extras", -5)]
    public async Task Invalid_entry_is_rejected(string category, decimal amount)
    {
        var project = (await GetAsync<List<ProjectDto>>("/api/projects")).First();

        var response = await (await factory.AdminAsync()).PostAsJsonAsync("/api/costs",
            new SaveCostEntryRequest(project.Id, category, amount, new DateOnly(2026, 8, 1), null),
            Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<T> GetAsync<T>(string url) =>
        (await (await factory.AdminAsync()).GetFromJsonAsync<T>(url, Json))!;
}
