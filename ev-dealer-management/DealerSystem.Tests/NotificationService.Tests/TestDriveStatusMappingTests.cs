using AutoMapper;
using CustomerService.DTOs;
using CustomerService.Models;
using CustomerService.Profiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// TestDrives.Status is declared NOT NULL in the very first migration
/// (20251126150401_InitialCustomerServiceMigration.cs), but the entity carries
/// a C# initializer — TestDrive.Status = "Đã lên lịch". A convention-based
/// AutoMapper map copies the nullable DTO member over that initializer, so
/// omitting `status` in the payload wrote null into a NOT NULL column:
///
///   POST /api/testdrives    without status -> HTTP 500
///                             "NOT NULL constraint failed: TestDrives.Status"
///                             thrown at TestDriveService.cs:38
///   PUT  /api/testdrives/{id} without status -> HTTP 500
///                             thrown at TestDriveService.cs:89
///
/// The PUT case is the more dangerous of the two: UpdateTestDriveAsync maps
/// ONTO the loaded entity, so a caller updating only the appointment date
/// silently blanked the status of a booking that already existed — and then
/// the save failed, taking the whole update with it.
///
/// The fix is a Condition on both maps so a null/empty source status leaves
/// the destination's own value alone. These tests pin that in the MAPPER,
/// not in the service, because that is the single place both defects flow
/// from and it needs neither a database nor an HTTP host to run.
///
/// The counter-tests (…WithStatus_…) are not padding: if ForMember(Condition)
/// were to suppress convention mapping entirely, Status would go permanently
/// null in BOTH directions — a worse bug than the one being closed. They fail
/// loudly in that case instead of letting it ship.
/// </summary>
public class TestDriveStatusMappingTests
{
    // The literal is duplicated from Models/TestDrive.cs rather than read from
    // the entity, on purpose: a test that reads the value it is asserting
    // against passes even when the initializer is gone.
    private const string DefaultStatus = "Đã lên lịch";

    private static IMapper BuildMapper()
    {
        // Same wiring as CustomerService/Program.cs:149 — AddAutoMapper over
        // the profile assembly. Going through DI rather than constructing
        // MapperConfiguration directly means the test exercises the path the
        // running service actually uses.
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddAutoMapper(cfg => cfg.AddProfile<MappingProfile>());
        return services.BuildServiceProvider().GetRequiredService<IMapper>();
    }

    private static CreateTestDriveRequest CreateRequestWithoutStatus() => new()
    {
        CustomerId = 1,
        VehicleId = 1,
        DealerId = 1,
        AppointmentDate = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void Map_CreateRequest_NullStatus_KeepsEntityDefaultStatus()
    {
        var mapper = BuildMapper();

        var testDrive = mapper.Map<TestDrive>(CreateRequestWithoutStatus());

        Assert.Equal(DefaultStatus, testDrive.Status);
    }

    [Fact]
    public void Map_CreateRequest_EmptyStatus_KeepsEntityDefaultStatus()
    {
        var mapper = BuildMapper();
        var request = CreateRequestWithoutStatus();
        request.Status = string.Empty;

        var testDrive = mapper.Map<TestDrive>(request);

        Assert.Equal(DefaultStatus, testDrive.Status);
    }

    [Fact]
    public void Map_CreateRequest_WithStatus_UsesProvidedStatus()
    {
        var mapper = BuildMapper();
        var request = CreateRequestWithoutStatus();
        request.Status = "Đã hủy";

        var testDrive = mapper.Map<TestDrive>(request);

        Assert.Equal("Đã hủy", testDrive.Status);
    }

    [Fact]
    public void Map_OntoExistingTestDrive_NullStatus_KeepsExistingStatus()
    {
        var mapper = BuildMapper();
        // Stands in for the entity loaded by UpdateTestDriveAsync, already
        // carrying a status from a previous request.
        var existing = new TestDrive { Id = 7, Status = "Hoàn thành" };

        mapper.Map(new UpdateTestDriveRequest { AppointmentDate = DateTime.UtcNow }, existing);

        Assert.Equal("Hoàn thành", existing.Status);
    }

    [Fact]
    public void Map_OntoExistingTestDrive_WithStatus_OverwritesExistingStatus()
    {
        var mapper = BuildMapper();
        var existing = new TestDrive { Id = 7, Status = "Đã lên lịch" };

        mapper.Map(new UpdateTestDriveRequest { Status = "Hoàn thành" }, existing);

        Assert.Equal("Hoàn thành", existing.Status);
    }
}