using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Urbeat.Application.Dtos;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Urbeat.UnitTests.Infrastructure;

public class LandingPageContentServiceTests
{
    private readonly ApplicationDbContext _context;
    private readonly LandingPageContentService _service;

    public LandingPageContentServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"LandingPageDb_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _service = new LandingPageContentService(_context);
    }

    [Fact]
    public async Task CreateAsync_ShouldAddEntityAndReturnDto()
    {
        // Arrange
        var request = new LandingPageContentRequestDto
        {
            Section = "Hero",
            Key = "Title",
            Value = "Test Title",
            DisplayOrder = 1,
            IsActive = true,
            Description = "Test Description"
        };

        // Act
        var result = await _service.CreateAsync(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Section.Should().Be("Hero");
        result.Key.Should().Be("Title");
        result.Value.Should().Be("Test Title");
        result.DisplayOrder.Should().Be(1);
        result.IsActive.Should().BeTrue();
        result.Description.Should().Be("Test Description");

        var entity = await _context.LandingPageContents.FindAsync(result.Id);
        entity.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnOnlyActiveEntities()
    {
        // Arrange
        await _context.LandingPageContents.AddRangeAsync(
            new LandingPageContent { Section = "Hero", Key = "Title", Value = "Active", IsActive = true },
            new LandingPageContent { Section = "Hero", Key = "Subtitle", Value = "Inactive", IsActive = false }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetAllAsync(CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result.First().Value.Should().Be("Active");
    }

    [Fact]
    public async Task GetBySectionAsync_ShouldReturnOrderedEntities()
    {
        // Arrange
        await _context.LandingPageContents.AddRangeAsync(
            new LandingPageContent { Section = "Hero", Key = "Title2", Value = "Second", DisplayOrder = 2, IsActive = true },
            new LandingPageContent { Section = "Hero", Key = "Title1", Value = "First", DisplayOrder = 1, IsActive = true }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetBySectionAsync("Hero", CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.First().Key.Should().Be("Title1");
        result.Last().Key.Should().Be("Title2");
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpdateEntityAndMarkAsUpdated()
    {
        // Arrange
        var entity = new LandingPageContent { Section = "Hero", Key = "Title", Value = "Old", IsActive = true };
        _context.LandingPageContents.Add(entity);
        await _context.SaveChangesAsync();

        var request = new LandingPageContentRequestDto
        {
            Section = "Hero",
            Key = "Title",
            Value = "New",
            DisplayOrder = 1,
            IsActive = true,
            Description = "Updated"
        };

        // Act
        var result = await _service.UpdateAsync(entity.Id, request, CancellationToken.None);

        // Assert
        result.Value.Should().Be("New");
        result.Description.Should().Be("Updated");
        result.UpdatedAt.Should().BeAfter(result.CreatedAt);
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrowKeyNotFoundException_WhenEntityDoesNotExist()
    {
        // Arrange
        var request = new LandingPageContentRequestDto { Section = "Hero", Key = "Title", Value = "New" };

        // Act
        Func<Task> act = async () => await _service.UpdateAsync(Guid.NewGuid(), request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveEntity()
    {
        // Arrange
        var entity = new LandingPageContent { Section = "Hero", Key = "Title", Value = "Test", IsActive = true };
        _context.LandingPageContents.Add(entity);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.DeleteAsync(entity.Id, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        var deletedEntity = await _context.LandingPageContents.FindAsync(entity.Id);
        deletedEntity.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_ShouldReturnFalse_WhenEntityDoesNotExist()
    {
        // Act
        var result = await _service.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }
}
