using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using ConstructErp.Application.Common;
using ConstructErp.Application.Equipment;
using ConstructErp.Application.Projects;
using ConstructErp.Application.Requests;
using ConstructErp.Domain.Common;

namespace ConstructErp.Tests;

/// <summary>
/// Refusals carry a stable code clients translate, not just English text.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class ErrorContractTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Body(string Error, string Code, Dictionary<string, string>? Params);

    [Fact]
    public void Every_error_code_is_unique()
    {
        var codes = typeof(ErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string)field.GetValue(null)!)
            .ToList();

        Assert.NotEmpty(codes);
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public async Task A_validation_refusal_carries_its_code_and_English_fallback()
    {
        var client = await factory.AdminAsync();

        var response = await client.PostAsJsonAsync("/api/projects", new SaveProjectRequest(
            "PRJ-ERR", new LocalizedTextDto("Bad", "سيئ"), new LocalizedTextDto("C", "ج"),
            null, null, "Active", -5, 0, null, null), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<Body>(Json))!;
        Assert.Equal(ErrorCodes.NegativeAmount, body.Code);
        Assert.False(string.IsNullOrWhiteSpace(body.Error));
    }

    [Fact]
    public async Task A_workflow_refusal_carries_its_code()
    {
        var client = await factory.AdminAsync();
        // Other tests in this collection make some assets ready; pick one with no ready request.
        var ready = (await client.GetFromJsonAsync<List<RequestDto>>("/api/requests", Json))!
            .Where(r => r.Status == "Ready to Use")
            .Select(r => r.EquipmentId)
            .ToHashSet();
        var asset = (await client.GetFromJsonAsync<List<EquipmentDto>>("/api/equipment", Json))!
            .First(e => e.Status != "Working" && !ready.Contains(e.Id));

        var response = await client.PutAsJsonAsync($"/api/equipment/{asset.Id}", new SaveEquipmentRequest(
            asset.Code, asset.Name, asset.EquipmentTypeId, asset.Ownership, asset.ProjectId,
            "Working", asset.Utilization, asset.DailyCost, asset.NextAction), Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.EquipmentNotReady, (await response.Content.ReadFromJsonAsync<Body>(Json))!.Code);
    }

    [Fact]
    public async Task A_code_clash_names_the_code_in_params()
    {
        var client = await factory.AdminAsync();
        var existing = (await client.GetFromJsonAsync<List<ProjectDto>>("/api/projects", Json))!.First();

        var response = await client.PostAsJsonAsync("/api/projects", new SaveProjectRequest(
            existing.Code, new LocalizedTextDto("Dup", "مكرر"), new LocalizedTextDto("C", "ج"),
            null, null, "Active", 0, 0, null, null), Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<Body>(Json))!;
        Assert.Equal(ErrorCodes.CodeInUse, body.Code);
        Assert.Equal(existing.Code, body.Params!["code"]);
    }
}
