using AutoMapper;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Urbeat.Application.DTOs;
using Urbeat.Application.Mappings;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Xunit.Abstractions;

namespace Urbeat.IntegrationTests.Relational;

/// <summary>
/// The business-hours upsert replaces the whole week by deleting the existing rows and inserting
/// the new schedule in a single SaveChanges. PostgreSQL enforces the unique index on
/// (StoreId, DayOfWeek), so this test proves that a second update of the same days does not
/// violate it. The InMemory provider has no unique constraints and cannot reproduce that risk,
/// so the test runs against Testcontainers PostgreSQL and is skipped when Docker is unavailable.
/// </summary>
public sealed class StoreBusinessHoursServiceRelationalTests
{
    private readonly ITestOutputHelper _output;

    public StoreBusinessHoursServiceRelationalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task UpsertAsync_ShouldUpdateTheSameDaysTwice_WithoutUniqueIndexViolation()
    {
        var container = await StartPostgresOrSkipAsync();
        if (container is null)
        {
            return;
        }

        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(container.GetConnectionString())
                .Options;

            Guid storeId;
            var ownerUserId = Guid.NewGuid();

            await using (var setupContext = new ApplicationDbContext(options))
            {
                await setupContext.Database.EnsureCreatedAsync();

                var store = new Store
                {
                    OwnerUserId = ownerUserId,
                    Name = "Loja Horários",
                    Slug = "loja-horarios",
                    PhoneNumber = "11999999999"
                };

                setupContext.Stores.Add(store);
                await setupContext.SaveChangesAsync();
                storeId = store.Id;
            }

            var mapper = new MapperConfiguration(configuration => configuration.AddProfile<EntityToDtoProfile>())
                .CreateMapper();

            await using (var firstContext = new ApplicationDbContext(options))
            {
                var service = new StoreBusinessHoursService(
                    firstContext,
                    mapper,
                    new EfUnitOfWork(firstContext));

                var result = await service.UpsertAsync(
                    ownerUserId,
                    storeId,
                    BuildRequest(
                        (DayOfWeek.Monday, true, new TimeOnly(9, 0), new TimeOnly(18, 0)),
                        (DayOfWeek.Tuesday, true, new TimeOnly(10, 0), new TimeOnly(20, 0))),
                    ipAddress: null);

                result.NotFound.Should().BeFalse();
                result.Forbidden.Should().BeFalse();
                result.Hours.Should().NotBeNull();
            }

            await using (var secondContext = new ApplicationDbContext(options))
            {
                var service = new StoreBusinessHoursService(
                    secondContext,
                    mapper,
                    new EfUnitOfWork(secondContext));

                var result = await service.UpsertAsync(
                    ownerUserId,
                    storeId,
                    BuildRequest(
                        (DayOfWeek.Monday, true, new TimeOnly(0, 0), new TimeOnly(23, 59)),
                        (DayOfWeek.Tuesday, true, new TimeOnly(10, 0), new TimeOnly(20, 0)),
                        (DayOfWeek.Wednesday, false, new TimeOnly(8, 0), new TimeOnly(8, 0))),
                    ipAddress: null);

                result.NotFound.Should().BeFalse();
                result.Forbidden.Should().BeFalse();
                result.Hours.Should().NotBeNull();
                result.Hours!.Items.Should().HaveCount(3);
            }

            await using var verifyContext = new ApplicationDbContext(options);
            var hours = await verifyContext.StoreBusinessHours
                .Include(hour => hour.Shifts)
                .Where(hour => hour.StoreId == storeId)
                .OrderBy(hour => hour.DayOfWeek)
                .ToListAsync();

            hours.Should().HaveCount(3);
            hours.Select(hour => hour.DayOfWeek).Should().OnlyHaveUniqueItems();
            hours.Should().ContainSingle(hour => hour.DayOfWeek == DayOfWeek.Monday)
                .Which.Shifts.Should().ContainSingle()
                .Which.StartTime.Should().Be(new TimeOnly(0, 0));
            hours.Should().ContainSingle(hour => hour.DayOfWeek == DayOfWeek.Wednesday)
                .Which.Shifts.Should().BeEmpty();

            var auditEvents = await verifyContext.AuditLogs
                .Where(log => log.EntityId == storeId)
                .Select(log => log.Event)
                .ToListAsync();

            auditEvents.Count(evt => evt == "StoreBusinessHoursUpserted").Should().Be(2);
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    private static UpsertStoreBusinessHoursRequestDto BuildRequest(
        params (DayOfWeek DayOfWeek, bool IsOpen, TimeOnly Start, TimeOnly End)[] days)
        => new()
        {
            Items = days.Select(day => new StoreBusinessHourItemDto
            {
                DayOfWeek = day.DayOfWeek,
                IsOpen = day.IsOpen,
                Shifts =
                [
                    new StoreBusinessHourShiftDto
                    {
                        StartTime = day.Start,
                        EndTime = day.End
                    }
                ]
            }).ToList()
        };

    private async Task<PostgreSqlContainer?> StartPostgresOrSkipAsync()
    {
        PostgreSqlContainer? container = null;

        try
        {
            container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("urbeat")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await container.StartAsync();
            return container;
        }
        catch (Exception exception)
        {
            if (container is not null)
            {
                await container.DisposeAsync();
            }

            _output.WriteLine($"SKIPPED (Docker unavailable): {exception.Message}");
            return null;
        }
    }
}
