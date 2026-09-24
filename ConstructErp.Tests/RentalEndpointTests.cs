using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ConstructErp.Application.Common;
using ConstructErp.Application.Equipment;
using ConstructErp.Application.Projects;
using ConstructErp.Application.Rentals;

namespace ConstructErp.Tests;

[Collection(nameof(ApiCollection))]
public sealed class RentalEndpointTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static DateOnly Today =>
        DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(BusinessCalendar.Offset).DateTime);

    [Fact]
    public async Task Vendors_are_records_with_both_languages()
    {
        var vendors = await GetAsync<List<VendorDto>>("/api/vendors");
        var delta = vendors.First(v => v.Code == "VEN-001");

        // The prototype's bare string "Delta Heavy Rentals" is now a row that
        // can be contacted and totalled.
        Assert.Equal("Delta Heavy Rentals", delta.Name.En);
        Assert.False(string.IsNullOrWhiteSpace(delta.Name.Ar));
        Assert.False(string.IsNullOrWhiteSpace(delta.Phone));
    }

    [Fact]
    public async Task Vendor_spend_is_summed_from_its_rentals()
    {
        var vendor = (await GetAsync<List<VendorDto>>("/api/vendors")).First(v => v.RentalCount > 0);
        var rentals = await GetAsync<List<RentalDto>>($"/api/rentals?vendorId={vendor.Id}");

        Assert.Equal(rentals.Count, vendor.RentalCount);
        Assert.Equal(rentals.Sum(r => r.Amount), vendor.TotalSpend);
        Assert.Equal(rentals.Count(r => r.ReturnedOn is null), vendor.OpenRentalCount);
    }

    [Fact]
    public async Task A_vendor_with_hire_history_cannot_be_deleted()
    {
        var vendor = (await GetAsync<List<VendorDto>>("/api/vendors")).First(v => v.RentalCount > 0);

        var response = await (await factory.AdminAsync()).DeleteAsync($"/api/vendors/{vendor.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Rental_resolves_vendor_asset_and_project_through_foreign_keys()
    {
        var rental = (await GetAsync<List<RentalDto>>("/api/rentals")).First();

        Assert.NotEqual(Guid.Empty, rental.VendorId);
        Assert.False(string.IsNullOrWhiteSpace(rental.VendorName.En));
        Assert.False(string.IsNullOrWhiteSpace(rental.EquipmentCode));
        Assert.NotNull(rental.ProjectCode);
    }

    [Fact]
    public async Task Overdue_is_derived_from_the_dates_not_stored()
    {
        var overdue = await GetAsync<List<RentalDto>>("/api/rentals?overdue=true");

        Assert.NotEmpty(overdue);

        foreach (var rental in overdue)
        {
            // Nobody typed this. Each of these rows is overdue because its
            // return date has passed and it has not come back — provable from
            // the two dates on the response itself.
            Assert.Equal("Overdue", rental.Status);
            Assert.Null(rental.ReturnedOn);
            Assert.True(rental.ExpectedReturnOn < Today);
            Assert.Equal(Today.DayNumber - rental.ExpectedReturnOn.DayNumber, rental.DaysOverdue);
        }
    }

    [Fact]
    public async Task Every_listed_status_agrees_with_its_own_dates()
    {
        var rentals = await GetAsync<List<RentalDto>>("/api/rentals");

        Assert.NotEmpty(rentals);

        foreach (var rental in rentals)
        {
            var expected = rental switch
            {
                { ReturnedOn: not null } => "Returned",
                _ when rental.ExpectedReturnOn < Today => "Overdue",
                { ReturnBookedOn: not null } => "Return Scheduled",
                _ => "Active",
            };

            Assert.Equal(expected, rental.Status);
        }
    }

    [Fact]
    public async Task Recording_a_return_clears_an_overdue_rental()
    {
        var client = await factory.AdminAsync();
        var (vendor, asset, project) = await ReferencesAsync();
        var code = $"RNT-T{Random.Shared.Next(1000, 9999)}";

        // Created with a return date already in the past. There is no status
        // field on the request to say so — the API works it out.
        var created = await client.PostAsJsonAsync("/api/rentals", new SaveRentalRequest(
            code, vendor.Id, asset.Id, project.Id,
            Today.AddDays(-40), Today.AddDays(-9), 5000m, null), Json);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var rental = (await created.Content.ReadFromJsonAsync<RentalDto>(Json))!;
        Assert.Equal("Overdue", rental.Status);
        Assert.Equal(9, rental.DaysOverdue);

        var returned = await client.PostAsJsonAsync(
            $"/api/rentals/{rental.Id}/return", new RecordReturnRequest(Today), Json);

        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);

        var closed = (await returned.Content.ReadFromJsonAsync<RentalDto>(Json))!;
        Assert.Equal("Returned", closed.Status);
        Assert.Equal(0, closed.DaysOverdue);

        // And it drops out of the overdue list without anyone editing a status.
        var overdue = await GetAsync<List<RentalDto>>("/api/rentals?overdue=true");
        Assert.DoesNotContain(overdue, r => r.Id == rental.Id);

        await client.DeleteAsync($"/api/rentals/{rental.Id}");
    }

    [Fact]
    public async Task Booking_a_return_schedules_it_but_a_passed_due_date_still_wins()
    {
        var client = await factory.AdminAsync();
        var (vendor, asset, project) = await ReferencesAsync();
        var code = $"RNT-T{Random.Shared.Next(1000, 9999)}";

        var created = await client.PostAsJsonAsync("/api/rentals", new SaveRentalRequest(
            code, vendor.Id, asset.Id, project.Id,
            Today.AddDays(-10), Today.AddDays(6), 3000m, null), Json);

        var rental = (await created.Content.ReadFromJsonAsync<RentalDto>(Json))!;
        Assert.Equal("Active", rental.Status);

        var booked = await client.PostAsJsonAsync(
            $"/api/rentals/{rental.Id}/book-return", new BookReturnRequest(Today), Json);

        var scheduled = (await booked.Content.ReadFromJsonAsync<RentalDto>(Json))!;
        Assert.Equal("Return Scheduled", scheduled.Status);

        // Move the due date into the past. The booking stays, but a collection
        // that has not happened reads Overdue so it keeps getting chased.
        var moved = await client.PutAsJsonAsync($"/api/rentals/{rental.Id}", new SaveRentalRequest(
            code, vendor.Id, asset.Id, project.Id,
            Today.AddDays(-10), Today.AddDays(-1), 3000m, null), Json);

        var late = (await moved.Content.ReadFromJsonAsync<RentalDto>(Json))!;
        Assert.Equal("Overdue", late.Status);
        Assert.NotNull(late.ReturnBookedOn);

        await client.DeleteAsync($"/api/rentals/{rental.Id}");
    }

    [Fact]
    public async Task A_return_cannot_be_recorded_twice()
    {
        var client = await factory.AdminAsync();
        var (vendor, asset, project) = await ReferencesAsync();
        var code = $"RNT-T{Random.Shared.Next(1000, 9999)}";

        var created = await client.PostAsJsonAsync("/api/rentals", new SaveRentalRequest(
            code, vendor.Id, asset.Id, project.Id,
            Today.AddDays(-5), Today.AddDays(5), 1000m, null), Json);
        var rental = (await created.Content.ReadFromJsonAsync<RentalDto>(Json))!;

        await client.PostAsJsonAsync(
            $"/api/rentals/{rental.Id}/return", new RecordReturnRequest(Today), Json);

        var again = await client.PostAsJsonAsync(
            $"/api/rentals/{rental.Id}/return", new RecordReturnRequest(Today), Json);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        await client.DeleteAsync($"/api/rentals/{rental.Id}");
    }

    [Fact]
    public async Task Rental_round_trips_arabic_notes_and_three_decimal_money()
    {
        var client = await factory.AdminAsync();
        var (vendor, asset, project) = await ReferencesAsync();
        var code = $"RNT-T{Random.Shared.Next(1000, 9999)}";

        var request = new SaveRentalRequest(
            code, vendor.Id, asset.Id, project.Id,
            Today, Today.AddDays(30), 7250.125m,
            new LocalizedTextDto("Includes operator", "يشمل المشغل"));

        // Explicit UTF-8, for the same reason as the project test: anything
        // less mangles the Arabic before it leaves the client.
        var content = new StringContent(
            JsonSerializer.Serialize(request, Json), Encoding.UTF8, "application/json");

        var created = await client.PostAsync("/api/rentals", content);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var rental = (await created.Content.ReadFromJsonAsync<RentalDto>(Json))!;

        Assert.Equal("يشمل المشغل", rental.Notes.Ar);
        Assert.Equal(7250.125m, rental.Amount);

        var deleted = await client.DeleteAsync($"/api/rentals/{rental.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var afterDelete = await client.GetAsync($"/api/rentals/{rental.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task A_return_date_before_the_start_is_rejected()
    {
        var (vendor, asset, project) = await ReferencesAsync();

        var response = await (await factory.AdminAsync()).PostAsJsonAsync("/api/rentals",
            new SaveRentalRequest(
                $"RNT-T{Random.Shared.Next(1000, 9999)}", vendor.Id, asset.Id, project.Id,
                Today, Today.AddDays(-1), 100m, null),
            Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_vendor_is_rejected()
    {
        var (_, asset, project) = await ReferencesAsync();

        var response = await (await factory.AdminAsync()).PostAsJsonAsync("/api/rentals",
            new SaveRentalRequest(
                $"RNT-T{Random.Shared.Next(1000, 9999)}", Guid.NewGuid(), asset.Id, project.Id,
                Today, Today.AddDays(10), 100m, null),
            Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<(VendorDto Vendor, EquipmentDto Asset, ProjectDto Project)> ReferencesAsync()
    {
        var vendor = (await GetAsync<List<VendorDto>>("/api/vendors")).First();
        var asset = (await GetAsync<List<EquipmentDto>>("/api/equipment")).First();
        var project = (await GetAsync<List<ProjectDto>>("/api/projects")).First();

        return (vendor, asset, project);
    }

    private async Task<T> GetAsync<T>(string url) =>
        (await (await factory.AdminAsync()).GetFromJsonAsync<T>(url, Json))!;
}
