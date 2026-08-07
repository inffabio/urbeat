using System.Data;
using Dapper;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Services;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Persistence.ReadRepositories;

public sealed class StoreReadRepository : IStoreReadRepository
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDapperUnitOfWork _dapperUnitOfWork;

    public StoreReadRepository(ApplicationDbContext dbContext, IDapperUnitOfWork dapperUnitOfWork)
    {
        _dbContext = dbContext;
        _dapperUnitOfWork = dapperUnitOfWork;
    }

    public async Task<StoreResponseDto?> GetByOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return await _dbContext.Stores
                .AsNoTracking()
                .Where(x => x.OwnerUserId == ownerUserId)
                .Select(x => new StoreResponseDto
                {
                    Id = x.Id,
                    OwnerUserId = x.OwnerUserId,
                    Name = x.Name,
                    Slug = x.Slug,
                     PhoneNumber = x.PhoneNumber,
                     Document = x.Document,
                     PixKey = x.PixKey,
                     InstagramUrl = x.InstagramUrl,
                     FacebookUrl = x.FacebookUrl,
                     TikTokUrl = x.TikTokUrl,
                     WebsiteUrl = x.WebsiteUrl,
                    Description = x.Description,
                    CuisineType = x.CuisineType != null ? x.CuisineType.Name : string.Empty,
                    BannerUrl = x.BannerUrl,
                    LogoUrl = x.LogoUrl,
                    
                    IsOpen = x.IsOpen,
                    IsSubscriptionBlocked = x.IsSubscriptionBlocked,
                    SupportsDelivery = x.SupportsDelivery,
                    SupportsPickup = x.SupportsPickup,
                    InitialMinute = x.InitialMinute,
                    FinalMinute = x.FinalMinute,
                    MaxDeliveryRadiusKm = x.MaxDeliveryRadiusKm,
                    LastImportedRadiusKm = x.LastImportedRadiusKm,
                    DeliveryFee = x.DeliveryFee,
                    MinimumOrderValue = x.MinimumOrderValue,
                    AverageRating = x.AverageRating,
                    TotalReviews = x.TotalReviews,
                    FreeShippingThreshold = x.FreeShippingThreshold,
                    FreeShippingToday = x.FreeShippingToday,
                    DeliveryAreas = x.DeliveryAreas.Select(a => new StoreDeliveryAreaDto
                    {
                         Id = a.Id,
                         Neighborhood = a.Neighborhood,
                         DeliveryFee = a.DeliveryFee,
                         MinimumOrderValue = a.MinimumOrderValue,
                         FreeShippingThreshold = a.FreeShippingThreshold,
                         IsActive = a.IsActive,
                         Notes = a.Notes
                    }).ToList()
                })
                .SingleOrDefaultAsync(cancellationToken);
        }

        return await _dapperUnitOfWork.ExecuteAsync(async (connection, ct) =>
        {
            const string sql = """
                SELECT
                    "Id",
                    "OwnerUserId",
                    "Name",
                    "Slug",
                    "Slug",
                    "PhoneNumber",
                    "Document",
                    "PixKey",
                    "InstagramUrl",
                    "FacebookUrl",
                    "TikTokUrl",
                    "WebsiteUrl",
                    "Description",
                    (SELECT "Name" FROM "CuisineTypes" WHERE "Id" = "Stores"."CuisineTypeId") AS "CuisineType",
                    "BannerUrl",
                    "LogoUrl",
                    "IsOpen",
                    "IsSubscriptionBlocked",
                    "SupportsDelivery",
                    "SupportsPickup",
                    "InitialMinute",
                    "FinalMinute",
                    "MaxDeliveryRadiusKm",
                    "LastImportedRadiusKm",
                    "DeliveryFee",
                    "MinimumOrderValue",
                    "AverageRating",
                    "TotalReviews",
                    "FreeShippingThreshold",
                    "FreeShippingToday"
                FROM "Stores"
                WHERE "OwnerUserId" = @OwnerUserId
                LIMIT 1;
                """;

            var store = await connection.QueryFirstOrDefaultAsync<StoreResponseDto>(
                  new CommandDefinition(sql, new { OwnerUserId = ownerUserId }, cancellationToken: ct));

              if (store is not null)
              {
                  const string areasSql = """
                      SELECT "Id", "Neighborhood", "DeliveryFee", "MinimumOrderValue", "FreeShippingThreshold", "IsActive", "Notes" FROM "StoreDeliveryAreas" WHERE "StoreId" = @StoreId
                  """;
                  var areas = await connection.QueryAsync<StoreDeliveryAreaDto>(
                      new CommandDefinition(areasSql, new { StoreId = store.Id }, cancellationToken: ct));
                  store.DeliveryAreas = areas.ToList();
                  
              }

              return store;
          }, cancellationToken);
    }

    public async Task<IReadOnlyCollection<StorePublicListItemDto>> ListPublicAsync(string? cuisineType, CancellationToken cancellationToken = default)
    {
        var normalizedCuisineType = string.IsNullOrWhiteSpace(cuisineType) ? null : cuisineType.Trim();

        if (!_dbContext.Database.IsRelational())
        {
            var query = _dbContext.Stores
                .AsNoTracking()
                .Where(x => !x.IsSubscriptionBlocked)
                .AsQueryable();

            if (normalizedCuisineType is not null)
            {
                query = query.Where(x => x.CuisineType != null && x.CuisineType.Name == normalizedCuisineType);
            }

            return await query
                .OrderBy(x => x.Name)
                .Select(x => new StorePublicListItemDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    Slug = x.Slug,
                    CuisineType = x.CuisineType != null ? x.CuisineType.Name : string.Empty,
                    IsOpen = x.IsOpen,
                    LogoUrl = x.LogoUrl,
                    DeliveryFee = x.DeliveryFee,
                    MinimumOrderValue = x.MinimumOrderValue,
                    AverageRating = x.AverageRating,
                    TotalReviews = x.TotalReviews,
                    FreeShippingThreshold = x.FreeShippingThreshold,
                    DeliveryAreas = x.DeliveryAreas.Select(a => new StoreDeliveryAreaDto
                    {
                         Id = a.Id,
                         Neighborhood = a.Neighborhood,
                         DeliveryFee = a.DeliveryFee,
                         MinimumOrderValue = a.MinimumOrderValue,
                         FreeShippingThreshold = a.FreeShippingThreshold,
                         IsActive = a.IsActive,
                         Notes = a.Notes
                    }).ToList()
                })
                .ToListAsync(cancellationToken);
        }

        return await _dapperUnitOfWork.ExecuteAsync(async (connection, ct) =>
        {
            const string sql = """
                SELECT
                    s."Id",
                    s."Name",
                    s."Slug",
                    s."Slug",
                    c."Name" AS "CuisineType",
                    s."IsOpen",
                    s."LogoUrl",
                    s."DeliveryFee",
                    s."MinimumOrderValue",
                    s."AverageRating",
                    s."TotalReviews",
                    s."FreeShippingThreshold"
                FROM "Stores" s
                LEFT JOIN "CuisineTypes" c ON s."CuisineTypeId" = c."Id"
                WHERE s."IsSubscriptionBlocked" = FALSE
                  AND (@CuisineType IS NULL OR c."Name" = @CuisineType)
                ORDER BY s."Name";
                """;

            var rows = await connection.QueryAsync<StorePublicListItemDto>(
                new CommandDefinition(sql, new { CuisineType = normalizedCuisineType }, cancellationToken: ct));

            return (IReadOnlyCollection<StorePublicListItemDto>)rows.ToList();
        }, cancellationToken);
    }

    public async Task<StorePublicDetailsDto?> GetPublicByIdAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        if (!_dbContext.Database.IsRelational())
        {
            var store = await _dbContext.Stores
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == storeId && !x.IsSubscriptionBlocked, cancellationToken);

            if (store is null)
            {
                return null;
            }

            var addressEntity = await _dbContext.StoreAddresses
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.StoreId == storeId, cancellationToken);

            var hourEntities = await _dbContext.StoreBusinessHours
                .AsNoTracking()
                .Include(x => x.Shifts)
                .Where(x => x.StoreId == storeId)
                .OrderBy(x => x.DayOfWeek)
                .ToListAsync(cancellationToken);

            return BuildStoreDetails(store, addressEntity, hourEntities);
        }

        return await _dapperUnitOfWork.ExecuteAsync(async (connection, ct) =>
        {
            const string storeSql = """
                SELECT
                    "Id",
                    "Name",
                    "Slug",
                    "Slug",
                    "PhoneNumber",
                    "Description",
                    (SELECT "Name" FROM "CuisineTypes" WHERE "Id" = "Stores"."CuisineTypeId") AS "CuisineType",
                    "BannerUrl",
                    "LogoUrl",
                    "IsOpen",
                    "DeliveryFee",
                    "MinimumOrderValue",
                    "AverageRating",
                    "TotalReviews",
                    "FreeShippingThreshold",
                    "FreeShippingToday",
                    "SupportsDelivery",
                    "SupportsPickup",
                    "InitialMinute",
                    "FinalMinute"
                FROM "Stores"
                WHERE "Id" = @StoreId
                  AND "IsSubscriptionBlocked" = FALSE
                LIMIT 1;
                """;

            var store = await connection.QueryFirstOrDefaultAsync<StorePublicDetailsDto>(
                new CommandDefinition(storeSql, new { StoreId = storeId }, cancellationToken: ct));

            if (store is null)
            {
                return null;
            }

            const string addressSql = """
                SELECT
                    "Street",
                    "Number",
                    "Neighborhood",
                    "City",
                    "State",
                    "ZipCode",
                    "Complement",
                    "Reference"
                FROM "StoreAddresses"
                WHERE "StoreId" = @StoreId
                LIMIT 1;
                """;

            var address = await connection.QueryFirstOrDefaultAsync<StorePublicAddressDto>(
                new CommandDefinition(addressSql, new { StoreId = storeId }, cancellationToken: ct));

            var hourEntities = await LoadBusinessHoursAsync(connection, storeId, ct);

            store.Address = address;
            ApplyOpeningHours(store, hourEntities);

            return store;
        }, cancellationToken);
    }

    public async Task<StorePublicDetailsDto?> GetPublicBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (!_dbContext.Database.IsRelational())
        {
            var store = await _dbContext.Stores
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Slug == slug && !x.IsSubscriptionBlocked, cancellationToken);

            if (store is null) return null;

            var addressEntity = await _dbContext.StoreAddresses
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.StoreId == store.Id, cancellationToken);

            var hourEntities = await _dbContext.StoreBusinessHours
                .AsNoTracking()
                .Include(x => x.Shifts)
                .Where(x => x.StoreId == store.Id)
                .OrderBy(x => x.DayOfWeek)
                .ToListAsync(cancellationToken);

            return BuildStoreDetails(store, addressEntity, hourEntities);
        }

        return await _dapperUnitOfWork.ExecuteAsync(async (connection, ct) =>
        {
            const string storeSql = """
                SELECT
                    "Id",
                    "Name",
                    "Slug",
                    "Slug",
                    "PhoneNumber",
                    "Description",
                    (SELECT "Name" FROM "CuisineTypes" WHERE "Id" = "Stores"."CuisineTypeId") AS "CuisineType",
                    "BannerUrl",
                    "LogoUrl",
                    "IsOpen",
                    "DeliveryFee",
                    "MinimumOrderValue",
                    "AverageRating",
                    "TotalReviews",
                    "FreeShippingThreshold",
                    "FreeShippingToday",
                    "SupportsDelivery",
                    "SupportsPickup",
                    "InitialMinute",
                    "FinalMinute"
                FROM "Stores"
                WHERE "Slug" = @Slug
                  AND "IsSubscriptionBlocked" = FALSE
                LIMIT 1;
                """;

            var store = await connection.QueryFirstOrDefaultAsync<StorePublicDetailsDto>(
                new CommandDefinition(storeSql, new { Slug = slug }, cancellationToken: ct));

            if (store is null) return null;

            const string addressSql = """
                SELECT
                    "Street",
                    "Number",
                    "Neighborhood",
                    "City",
                    "State",
                    "ZipCode",
                    "Complement",
                    "Reference"
                FROM "StoreAddresses"
                WHERE "StoreId" = @StoreId
                LIMIT 1;
                """;

            var address = await connection.QueryFirstOrDefaultAsync<StorePublicAddressDto>(
                new CommandDefinition(addressSql, new { StoreId = store.Id }, cancellationToken: ct));

            var hourEntities = await LoadBusinessHoursAsync(connection, store.Id, ct);

            store.Address = address;
            ApplyOpeningHours(store, hourEntities);

            return store;
        }, cancellationToken);
    }

    public async Task<StorePublicDetailsDto?> GetPublicByPathAsync(string Slug, CancellationToken cancellationToken = default)
    {
        if (!_dbContext.Database.IsRelational())
        {
            var store = await _dbContext.Stores
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Slug == Slug && !x.IsSubscriptionBlocked, cancellationToken);

            if (store is null) return null;

            var addressEntity = await _dbContext.StoreAddresses
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.StoreId == store.Id, cancellationToken);

            var hourEntities = await _dbContext.StoreBusinessHours
                .AsNoTracking()
                .Include(x => x.Shifts)
                .Where(x => x.StoreId == store.Id)
                .OrderBy(x => x.DayOfWeek)
                .ToListAsync(cancellationToken);

            return BuildStoreDetails(store, addressEntity, hourEntities);
        }

        return await _dapperUnitOfWork.ExecuteAsync(async (connection, ct) =>
        {
            const string storeSql = """
                SELECT
                    "Id",
                    "Name",
                    "Slug",
                    "Slug",
                    "PhoneNumber",
                    "Description",
                    (SELECT "Name" FROM "CuisineTypes" WHERE "Id" = "Stores"."CuisineTypeId") AS "CuisineType",
                    "BannerUrl",
                    "LogoUrl",
                    "IsOpen",
                    "DeliveryFee",
                    "MinimumOrderValue",
                    "AverageRating",
                    "TotalReviews",
                    "FreeShippingThreshold",
                    "FreeShippingToday",
                    "SupportsDelivery",
                    "SupportsPickup",
                    "InitialMinute",
                    "FinalMinute"
                FROM "Stores"
                WHERE "Slug" = @Slug
                  AND "IsSubscriptionBlocked" = FALSE
                LIMIT 1;
                """;

            var store = await connection.QueryFirstOrDefaultAsync<StorePublicDetailsDto>(
                new CommandDefinition(storeSql, new { Slug = Slug }, cancellationToken: ct));

            if (store is null) return null;

            const string addressSql = """
                SELECT
                    "Street",
                    "Number",
                    "Neighborhood",
                    "City",
                    "State",
                    "ZipCode",
                    "Complement",
                    "Reference"
                FROM "StoreAddresses"
                WHERE "StoreId" = @StoreId
                LIMIT 1;
                """;

            var address = await connection.QueryFirstOrDefaultAsync<StorePublicAddressDto>(
                new CommandDefinition(addressSql, new { StoreId = store.Id }, cancellationToken: ct));

            var hourEntities = await LoadBusinessHoursAsync(connection, store.Id, ct);

            store.Address = address;
            ApplyOpeningHours(store, hourEntities);

            return store;
        }, cancellationToken);
    }

    private static StorePublicDetailsDto BuildStoreDetails(
        Store store,
        StoreAddress? addressEntity,
        List<StoreBusinessHour> hourEntities)
    {
        var openingHours = StoreOpeningHoursCalculator.Calculate(store.IsOpen, hourEntities, DateTimeOffset.UtcNow);

        return new StorePublicDetailsDto
        {
            Id = store.Id,
            Name = store.Name,
            Slug = store.Slug,
            PhoneNumber = store.PhoneNumber,
            Description = store.Description,
            CuisineType = store.CuisineType != null ? store.CuisineType.Name : string.Empty,
            BannerUrl = store.BannerUrl,
            LogoUrl = store.LogoUrl,
            IsOpen = store.IsOpen,
            IsOpenNow = openingHours.IsOpenNow,
            NextOpeningAt = openingHours.NextOpeningAt,
            NextStatusChangeAt = openingHours.NextStatusChangeAtUtc,
            ClosedMessage = openingHours.ClosedMessage,
            SupportsDelivery = store.SupportsDelivery,
            SupportsPickup = store.SupportsPickup,
            DeliveryFee = store.DeliveryFee,
            MinimumOrderValue = store.MinimumOrderValue,
            FreeShippingThreshold = store.FreeShippingThreshold,
            FreeShippingToday = store.FreeShippingToday,
            InitialMinute = store.InitialMinute,
            FinalMinute = store.FinalMinute,
            AverageRating = store.AverageRating,
            TotalReviews = store.TotalReviews,
            Address = addressEntity is null
                ? null
                : new StorePublicAddressDto
                {
                    Street = addressEntity.Street,
                    Number = addressEntity.Number,
                    Neighborhood = addressEntity.Neighborhood,
                    City = addressEntity.City,
                    State = addressEntity.State,
                    ZipCode = addressEntity.ZipCode,
                    Complement = addressEntity.Complement,
                    Reference = addressEntity.Reference
                },
            BusinessHours = hourEntities.Select(x => new StoreBusinessHourItemDto
            {
                DayOfWeek = x.DayOfWeek,
                IsOpen = x.IsOpen,
                Shifts = x.Shifts.Select(s => new StoreBusinessHourShiftDto
                {
                    StartTime = s.StartTime,
                    EndTime = s.EndTime
                }).ToList()
            }).ToList()
        };
    }

    private static void ApplyOpeningHours(StorePublicDetailsDto store, List<StoreBusinessHour> hourEntities)
    {
        var openingHours = StoreOpeningHoursCalculator.Calculate(store.IsOpen, hourEntities, DateTimeOffset.UtcNow);
        store.IsOpenNow = openingHours.IsOpenNow;
        store.NextOpeningAt = openingHours.NextOpeningAt;
        store.NextStatusChangeAt = openingHours.NextStatusChangeAtUtc;
        store.ClosedMessage = openingHours.ClosedMessage;
        store.BusinessHours = hourEntities.Select(x => new StoreBusinessHourItemDto
        {
            DayOfWeek = x.DayOfWeek,
            IsOpen = x.IsOpen,
            Shifts = x.Shifts.Select(s => new StoreBusinessHourShiftDto
            {
                Id = s.Id,
                StartTime = s.StartTime,
                EndTime = s.EndTime
            }).ToList()
        }).ToList();
    }

    private static async Task<List<StoreBusinessHour>> LoadBusinessHoursAsync(
        IDbConnection connection,
        Guid storeId,
        CancellationToken cancellationToken)
    {
        const string hoursSql = """
            SELECT
                h."Id" AS BusinessHourId,
                CAST(h."DayOfWeek" as integer) AS DayOfWeek,
                h."IsOpen" AS IsOpen,
                s."Id" AS ShiftId,
                s."StartTime" AS StartTime,
                s."EndTime" AS EndTime
            FROM "StoreBusinessHours" h
            LEFT JOIN "StoreBusinessHourShift" s ON s."StoreBusinessHourId" = h."Id"
            WHERE h."StoreId" = @StoreId
            ORDER BY h."DayOfWeek", s."StartTime";
            """;

        var rows = await connection.QueryAsync<BusinessHourShiftRow>(
            new CommandDefinition(hoursSql, new { StoreId = storeId }, cancellationToken: cancellationToken));

        return rows
            .GroupBy(row => new { row.BusinessHourId, row.DayOfWeek, row.IsOpen })
            .Select(group => new StoreBusinessHour
            {
                StoreId = storeId,
                DayOfWeek = (DayOfWeek)group.Key.DayOfWeek,
                IsOpen = group.Key.IsOpen,
                Shifts = group
                    .Where(row => row.ShiftId.HasValue && row.StartTime.HasValue && row.EndTime.HasValue)
                    .Select(row => new StoreBusinessHourShift
                    {
                        StoreBusinessHourId = group.Key.BusinessHourId,
                        StartTime = TimeOnly.FromTimeSpan(row.StartTime!.Value),
                        EndTime = TimeOnly.FromTimeSpan(row.EndTime!.Value)
                    })
                    .ToList()
            })
            .ToList();
    }

    private sealed class BusinessHourShiftRow
    {
        public Guid BusinessHourId { get; init; }

        public int DayOfWeek { get; init; }

        public bool IsOpen { get; init; }

        public Guid? ShiftId { get; init; }

        public TimeSpan? StartTime { get; init; }

        public TimeSpan? EndTime { get; init; }
    }
}






