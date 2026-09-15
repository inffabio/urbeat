using AutoMapper;
using FluentAssertions;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Mappings;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class StoreServicePublishTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly StoreService _sut;

    public StoreServicePublishTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-store-publish-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);

        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<EntityToDtoProfile>()).CreateMapper();

        _sut = new StoreService(
            _db,
            mapper,
            Mock.Of<IStoreReadRepository>(),
            new EfUnitOfWork(_db),
            Mock.Of<IImageUploadService>());
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public void NewStore_ShouldDefaultToUnpublished()
    {
        var store = new Store();

        store.IsPublished.Should().BeFalse();
    }

    [Fact]
    public async Task MarkAsPublishedAsync_ShouldPersistAndReturnPublishedStore()
    {
        var ownerUserId = Guid.NewGuid();
        var store = new Store
        {
            OwnerUserId = ownerUserId,
            Name = "Loja Teste",
            Slug = "loja-teste",
            PhoneNumber = "11999999999",
            IsPublished = false
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        var result = await _sut.MarkAsPublishedAsync(ownerUserId, store.Id, ipAddress: null);

        result.NotFound.Should().BeFalse();
        result.Forbidden.Should().BeFalse();
        result.Store.Should().NotBeNull();
        result.Store!.IsPublished.Should().BeTrue();

        var persisted = await _db.Stores.SingleAsync(x => x.Id == store.Id);
        persisted.IsPublished.Should().BeTrue();
        persisted.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task MarkAsPublishedAsync_ShouldNotPublishAnotherOwnersStore()
    {
        var store = new Store
        {
            OwnerUserId = Guid.NewGuid(),
            Name = "Loja Teste",
            Slug = "loja-teste",
            PhoneNumber = "11999999999",
            IsPublished = false
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        var result = await _sut.MarkAsPublishedAsync(Guid.NewGuid(), store.Id, ipAddress: null);

        result.Forbidden.Should().BeTrue();

        var persisted = await _db.Stores.SingleAsync(x => x.Id == store.Id);
        persisted.IsPublished.Should().BeFalse();
    }
}
