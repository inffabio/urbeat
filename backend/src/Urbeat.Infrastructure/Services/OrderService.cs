using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Services;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using System.Text.Json;

namespace Urbeat.Infrastructure.Services;

public sealed class OrderService : IOrderService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEfUnitOfWork _efUnitOfWork;
    private readonly INotificationService _notificationService;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IEncryptionService? _encryptionService;

    public OrderService(
        ApplicationDbContext dbContext,
        IEfUnitOfWork efUnitOfWork,
        INotificationService notificationService,
        IOutboxWriter outboxWriter,
        IEncryptionService? encryptionService = null)
    {
        _dbContext = dbContext;
        _efUnitOfWork = efUnitOfWork;
        _notificationService = notificationService;
        _outboxWriter = outboxWriter;
        _encryptionService = encryptionService;
    }

    public async Task<IReadOnlyCollection<OrderSummaryResponseDto>> ListCustomerOrdersAsync(Guid customerUserId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Orders
            .AsNoTracking()
            .Where(x => x.CustomerUserId == customerUserId)
            .OrderByDescending(x => x.CreatedAtUtc)
                         .Select(x => new OrderSummaryResponseDto
            {
                Id = x.Id,
                Code = x.Code,
                StoreId = x.StoreId,
                Status = x.Status,
                Subtotal = x.Subtotal,
                DeliveryFee = x.DeliveryFee,
                Total = x.Total,
                CreatedAtUtc = x.CreatedAtUtc,
                DeliveryConfirmedAtUtc = x.DeliveryConfirmedAtUtc,
                SellerCompletedAtUtc = x.SellerCompletedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<OrderDetailsResponseDto?> GetCustomerOrderAsync(Guid customerUserId, Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == orderId && x.CustomerUserId == customerUserId, cancellationToken);

        return order is null ? null : await LoadDetailsAsync(order, cancellationToken, includeDeliveryPin: true);
    }

    public async Task<PagedOrderSummaryResponseDto> ListStoreOrdersAsync(
        Guid sellerUserId,
        StoreOrdersHistoryQueryDto query,
        CancellationToken cancellationToken = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 20 : Math.Min(query.PageSize, 100);

        var storeOrdersQuery = _dbContext.Orders
            .AsNoTracking()
            .Join(
                _dbContext.Stores.AsNoTracking().Where(s => s.OwnerUserId == sellerUserId),
                order => order.StoreId,
                store => store.Id,
                (order, _) => order)
            .Where(x => x.Status != OrderStatus.PendingPayment);

        if (query.Status.HasValue)
        {
            storeOrdersQuery = storeOrdersQuery.Where(x => x.Status == query.Status.Value);
        }

        if (query.StartDateUtc.HasValue)
        {
            storeOrdersQuery = storeOrdersQuery.Where(x => x.CreatedAtUtc >= query.StartDateUtc.Value);
        }

        if (query.EndDateUtc.HasValue)
        {
            storeOrdersQuery = storeOrdersQuery.Where(x => x.CreatedAtUtc <= query.EndDateUtc.Value);
        }

        var totalItems = await storeOrdersQuery.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

        var orderPage = await storeOrdersQuery
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var orderIds = orderPage.Select(x => x.Id).ToArray();
        var customerIds = orderPage.Select(x => x.CustomerUserId).Distinct().ToArray();
        var phones = await _dbContext.Users
            .AsNoTracking()
            .Where(x => customerIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PhoneNumber })
            .ToDictionaryAsync(x => x.Id, x => x.PhoneNumber, cancellationToken);
        var names = await _dbContext.UserClaims
            .AsNoTracking()
            .Where(x => customerIds.Contains(x.UserId) && x.ClaimType == "FullName")
            .Select(x => new { x.UserId, x.ClaimValue })
            .ToDictionaryAsync(x => x.UserId, x => x.ClaimValue, cancellationToken);
        var orderItems = await _dbContext.OrderItems
            .AsNoTracking()
            .Where(x => orderIds.Contains(x.OrderId))
            .OrderBy(x => x.ProductName)
            .Select(x => new { x.OrderId, x.Quantity, x.ProductName })
            .ToListAsync(cancellationToken);
        var itemSummaries = orderItems
            .GroupBy(x => x.OrderId)
            .ToDictionary(
                x => x.Key,
                x => string.Join(", ", x.Select(item => $"{item.Quantity}x {item.ProductName}")));

        var assignments = await LoadAssignmentSummariesAsync(orderIds, cancellationToken);

        var items = orderPage
            .Select(x =>
            {
                assignments.TryGetValue(x.Id, out var assignment);
                return new OrderSummaryResponseDto
                {
                    Id = x.Id,
                    Code = x.Code,
                    StoreId = x.StoreId,
                    CustomerName = names.GetValueOrDefault(x.CustomerUserId),
                    CustomerPhoneNumber = phones.GetValueOrDefault(x.CustomerUserId),
                    FulfillmentType = x.FulfillmentType,
                    PaymentMethod = x.PaymentMethod,
                    AddressSummary = BuildAddressSummary(x),
                    ItemsSummary = itemSummaries.GetValueOrDefault(x.Id),
                    Status = x.Status,
                    Subtotal = x.Subtotal,
                    DeliveryFee = x.DeliveryFee,
                    Total = x.Total,
                    CreatedAtUtc = x.CreatedAtUtc,
                    DeliveryConfirmedAtUtc = x.DeliveryConfirmedAtUtc,
                    SellerCompletedAtUtc = x.SellerCompletedAtUtc,
                    DeliveryDriverId = assignment?.DeliveryDriverId,
                    DeliveryDriverName = assignment?.DeliveryDriverName,
                    DeliveryStartedAtUtc = assignment?.DeliveryStartedAtUtc,
                    DeliveryPinConfirmedAtUtc = x.DeliveryPinConfirmedAtUtc,
                    DeliveryRouteId = assignment?.DeliveryRouteId,
                    DeliveryRouteStatus = assignment?.DeliveryRouteStatus
                };
            })
            .ToList();

        return new PagedOrderSummaryResponseDto
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages,
            Items = items
        };
    }

    private static IReadOnlyCollection<OrderItemOptionPriceDto> DeserializeOptionPrices(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<OrderItemOptionPriceDto>();

        try
        {
            return JsonSerializer.Deserialize<List<OrderItemOptionPriceDto>>(json) ?? new List<OrderItemOptionPriceDto>();
        }
        catch (JsonException)
        {
            return Array.Empty<OrderItemOptionPriceDto>();
        }
    }

    private static string? BuildAddressSummary(Order order)
    {
        var streetLine = string.Join(", ", new[] { order.AddressStreet, order.AddressNumber }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var parts = new[] { streetLine, order.AddressNeighborhood }.Where(x => !string.IsNullOrWhiteSpace(x));
        var summary = string.Join(" - ", parts);
        return string.IsNullOrWhiteSpace(summary) ? null : summary;
    }

    private sealed record AssignmentSummary(
        Guid DeliveryDriverId,
        string DeliveryDriverName,
        DateTime? DeliveryStartedAtUtc,
        Guid? DeliveryRouteId,
        DeliveryRouteStatus? DeliveryRouteStatus);

    private async Task<Dictionary<Guid, AssignmentSummary>> LoadAssignmentSummariesAsync(
        IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0)
        {
            return [];
        }

        var rows = await (
            from assignment in _dbContext.OrderDeliveryAssignments.AsNoTracking()
            join driver in _dbContext.DeliveryDrivers.AsNoTracking()
                on assignment.DeliveryDriverId equals driver.Id
            where orderIds.Contains(assignment.OrderId)
            select new
            {
                assignment.OrderId,
                assignment.DeliveryDriverId,
                DriverName = driver.FullName,
                assignment.DeliveryStartedAtUtc
            }
        ).ToListAsync(cancellationToken);

        var routeRows = await (
            from stop in _dbContext.DeliveryRouteOrders.AsNoTracking()
            join route in _dbContext.DeliveryRoutes.AsNoTracking() on stop.DeliveryRouteId equals route.Id
            where orderIds.Contains(stop.OrderId)
            orderby route.CreatedAtUtc descending
            select new
            {
                stop.OrderId,
                RouteId = route.Id,
                route.RevokedAtUtc,
                route.CompletedAtUtc,
                route.ExpiresAtUtc
            }
        ).ToListAsync(cancellationToken);

        var routes = routeRows
            .GroupBy(x => x.OrderId)
            .ToDictionary(x => x.Key, x => x.First());
        var now = DateTime.UtcNow;

        return rows.ToDictionary(
            x => x.OrderId,
            x =>
            {
                routes.TryGetValue(x.OrderId, out var route);
                return new AssignmentSummary(
                    x.DeliveryDriverId,
                    x.DriverName,
                    x.DeliveryStartedAtUtc,
                    route?.RouteId,
                    route is null ? null : GetRouteStatus(route.RevokedAtUtc, route.CompletedAtUtc, route.ExpiresAtUtc, now));
            });
    }

    private static DeliveryRouteStatus GetRouteStatus(
        DateTime? revokedAtUtc,
        DateTime? completedAtUtc,
        DateTime expiresAtUtc,
        DateTime utcNow)
    {
        if (revokedAtUtc is not null) return DeliveryRouteStatus.Revoked;
        if (completedAtUtc is not null) return DeliveryRouteStatus.Completed;
        return expiresAtUtc <= utcNow ? DeliveryRouteStatus.Expired : DeliveryRouteStatus.Active;
    }

    public async Task<OrderDetailsResponseDto?> GetStoreOrderAsync(Guid sellerUserId, Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .Join(
                _dbContext.Stores.AsNoTracking().Where(s => s.OwnerUserId == sellerUserId),
                o => o.StoreId,
                s => s.Id,
                (o, _) => o)
            .Where(x => x.Status != OrderStatus.PendingPayment)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);

        return order is null ? null : await LoadDetailsAsync(order, cancellationToken);
    }

    public async Task<PagedSellerCustomerSummaryResponseDto> ListStoreCustomersAsync(
        Guid sellerUserId,
        StoreCustomersQueryDto query,
        CancellationToken cancellationToken = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 20 : Math.Min(query.PageSize, 100);
        var storeId = await _dbContext.Stores
            .Where(x => x.OwnerUserId == sellerUserId)
            .Select(x => x.Id)
            .SingleAsync(cancellationToken);
        var orders = await SellerVisibleOrders(sellerUserId)
            .Select(x => new { x.CustomerUserId, x.Total, x.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        var registeredCustomerIds = await _dbContext.StoreCustomers
            .AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .Select(x => x.CustomerUserId)
            .ToListAsync(cancellationToken);
        var verificationCustomerIds = await _dbContext.CustomerPhoneVerifications
            .AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var customerIds = orders.Select(x => x.CustomerUserId)
            .Concat(registeredCustomerIds)
            .Concat(verificationCustomerIds)
            .Distinct()
            .ToArray();

        if (customerIds.Length == 0)
        {
            return new PagedSellerCustomerSummaryResponseDto
            {
                Page = page,
                PageSize = pageSize,
                TotalItems = 0,
                TotalPages = 0
            };
        }

        var storeStatuses = await _dbContext.StoreCustomers.AsNoTracking()
            .Where(x => x.StoreId == storeId && customerIds.Contains(x.CustomerUserId))
            .ToDictionaryAsync(x => x.CustomerUserId, cancellationToken);
        var contacts = await _dbContext.Users
            .AsNoTracking()
            .Where(x => customerIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PhoneNumber, x.Email })
            .ToDictionaryAsync(x => x.Id, x => new { x.PhoneNumber, x.Email }, cancellationToken);
        var addresses = await _dbContext.CustomerAddresses
            .AsNoTracking()
            .Where(x => customerIds.Contains(x.UserId))
            .OrderByDescending(x => x.IsPrimary)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var addressByCustomer = addresses
            .GroupBy(x => x.UserId)
            .ToDictionary(group => group.Key, group => group.First());
        var names = await _dbContext.UserClaims
            .AsNoTracking()
            .Where(x => customerIds.Contains(x.UserId) && x.ClaimType == "FullName")
            .Select(x => new { x.UserId, x.ClaimValue })
            .ToDictionaryAsync(x => x.UserId, x => x.ClaimValue, cancellationToken);

        var currentUtc = DateTime.UtcNow;
        var ordersByCustomer = orders
            .GroupBy(x => x.CustomerUserId)
            .ToDictionary(group => group.Key);
        var customers = customerIds
            .Select(customerId =>
            {
                ordersByCustomer.TryGetValue(customerId, out var customerOrders);
                return new SellerCustomerSummaryResponseDto
                {
                    Id = customerId.ToString(),
                    Name = names.GetValueOrDefault(customerId) ?? "Cliente",
                    Email = contacts.GetValueOrDefault(customerId)?.Email ?? string.Empty,
                    Phone = contacts.GetValueOrDefault(customerId)?.PhoneNumber ?? "Nao informado",
                    Cep = addressByCustomer.GetValueOrDefault(customerId)?.Cep ?? string.Empty,
                    Street = addressByCustomer.GetValueOrDefault(customerId)?.Street ?? string.Empty,
                    Number = addressByCustomer.GetValueOrDefault(customerId)?.Number ?? string.Empty,
                    Complement = addressByCustomer.GetValueOrDefault(customerId)?.Complement ?? string.Empty,
                    Neighborhood = addressByCustomer.GetValueOrDefault(customerId)?.Neighborhood ?? string.Empty,
                    City = addressByCustomer.GetValueOrDefault(customerId)?.City ?? string.Empty,
                    State = addressByCustomer.GetValueOrDefault(customerId)?.State ?? string.Empty,
                    TotalOrders = customerOrders is null ? 0 : customerOrders.Count(),
                    TotalSpent = customerOrders is null ? 0 : customerOrders.Sum(x => x.Total),
                    LastOrderAtUtc = customerOrders is null ? null : customerOrders.Max(x => (DateTime?)x.CreatedAtUtc),
                    IsActive = storeStatuses.TryGetValue(customerId, out var storeCustomer)
                    ? storeCustomer.IsActive
                    : customerOrders is not null && IsInSameUtcMonth(customerOrders.Max(x => x.CreatedAtUtc), currentUtc)
                };
            })
            .ToList();

        var filteredCustomers = customers
            .Where(customer => MatchesSearch(customer, query.Search))
            .Where(customer => MatchesStatus(customer, query.Status))
            .ToList();

        var sortedCustomers = SortCustomers(filteredCustomers, query.Sort).ToList();
        var totalItems = sortedCustomers.Count;
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        var items = sortedCustomers
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PagedSellerCustomerSummaryResponseDto
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages,
            Metrics = new SellerCustomerMetricsResponseDto
            {
                TotalCustomers = totalItems,
                ActiveCustomers = filteredCustomers.Count(x => x.IsActive),
                RecurringCustomers = filteredCustomers.Count(x => x.TotalOrders > 2),
                NewCustomersThisMonth = orders
                    .GroupBy(x => x.CustomerUserId)
                    .Count(group => filteredCustomers.Any(customer => customer.Id == group.Key.ToString())
                        && IsInSameUtcMonth(group.Min(x => x.CreatedAtUtc), currentUtc)),
                AverageTicket = totalItems == 0 ? 0 : filteredCustomers.Sum(x => x.TotalSpent) / totalItems
            },
            Items = items
        };
    }

    public async Task<UpdateStoreCustomerResultDto> UpdateStoreCustomerAsync(Guid sellerUserId, Guid customerUserId, UpdateStoreCustomerRequestDto request, CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores.SingleOrDefaultAsync(x => x.OwnerUserId == sellerUserId, cancellationToken);
        if (store is null) return new UpdateStoreCustomerResultDto { Forbidden = true };
        if (!await IsCustomerRegisteredForStoreAsync(store.Id, customerUserId, cancellationToken))
            return new UpdateStoreCustomerResultDto { NotFound = true };
        var user = await _dbContext.Users.SingleOrDefaultAsync(x => x.Id == customerUserId, cancellationToken);
        if (user is null) return new UpdateStoreCustomerResultDto { NotFound = true };
        if (!string.Equals(user.Email, request.Email.Trim(), StringComparison.OrdinalIgnoreCase) && await _dbContext.Users.AnyAsync(x => x.Id != customerUserId && x.NormalizedEmail == request.Email.Trim().ToUpperInvariant(), cancellationToken))
            return new UpdateStoreCustomerResultDto { Conflict = true };

        user.Email = request.Email.Trim();
        user.NormalizedEmail = request.Email.Trim().ToUpperInvariant();
        user.UserName = request.Email.Trim();
        user.NormalizedUserName = request.Email.Trim().ToUpperInvariant();
        user.PhoneNumber = request.Phone.Trim();
        await UpdateCustomerPrimaryAddressAsync(customerUserId, request, cancellationToken);
        var claim = await _dbContext.UserClaims.SingleOrDefaultAsync(x => x.UserId == customerUserId && x.ClaimType == "FullName", cancellationToken);
        if (claim is null) _dbContext.UserClaims.Add(new IdentityUserClaim<Guid> { UserId = customerUserId, ClaimType = "FullName", ClaimValue = request.Name.Trim() });
        else claim.ClaimValue = request.Name.Trim();
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);
        return await BuildUpdatedCustomerResultAsync(store.Id, customerUserId, cancellationToken);
    }

    public async Task<UpdateStoreCustomerResultDto> UpdateStoreCustomerStatusAsync(Guid sellerUserId, Guid customerUserId, bool isActive, CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores.SingleOrDefaultAsync(x => x.OwnerUserId == sellerUserId, cancellationToken);
        if (store is null) return new UpdateStoreCustomerResultDto { Forbidden = true };
        if (!await IsCustomerRegisteredForStoreAsync(store.Id, customerUserId, cancellationToken))
            return new UpdateStoreCustomerResultDto { NotFound = true };
        var status = await _dbContext.StoreCustomers.SingleOrDefaultAsync(x => x.StoreId == store.Id && x.CustomerUserId == customerUserId, cancellationToken);
        if (status is null) _dbContext.StoreCustomers.Add(new StoreCustomer { StoreId = store.Id, CustomerUserId = customerUserId, IsActive = isActive });
        else { status.IsActive = isActive; status.MarkAsUpdated(); }
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);
        return await BuildUpdatedCustomerResultAsync(store.Id, customerUserId, cancellationToken);
    }

    private async Task<UpdateStoreCustomerResultDto> BuildUpdatedCustomerResultAsync(Guid storeId, Guid customerUserId, CancellationToken cancellationToken)
    {
        var result = await ListStoreCustomersForCustomerAsync(storeId, customerUserId, cancellationToken);
        return new UpdateStoreCustomerResultDto { Customer = result };
    }

    private async Task<SellerCustomerSummaryResponseDto?> ListStoreCustomersForCustomerAsync(Guid storeId, Guid customerUserId, CancellationToken cancellationToken)
    {
        var orders = await _dbContext.Orders.AsNoTracking().Where(x => x.StoreId == storeId && x.CustomerUserId == customerUserId).ToListAsync(cancellationToken);
        var user = await _dbContext.Users.AsNoTracking().SingleAsync(x => x.Id == customerUserId, cancellationToken);
        var name = await _dbContext.UserClaims.AsNoTracking().Where(x => x.UserId == customerUserId && x.ClaimType == "FullName").Select(x => x.ClaimValue).SingleOrDefaultAsync(cancellationToken);
        var status = await _dbContext.StoreCustomers.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == storeId && x.CustomerUserId == customerUserId, cancellationToken);
        var address = await _dbContext.CustomerAddresses.AsNoTracking()
            .Where(x => x.UserId == customerUserId)
            .OrderByDescending(x => x.IsPrimary)
            .ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return new SellerCustomerSummaryResponseDto
        {
            Id = customerUserId.ToString(),
            Name = name ?? "Cliente",
            Email = user.Email ?? string.Empty,
            Phone = user.PhoneNumber ?? string.Empty,
            Cep = address?.Cep ?? string.Empty,
            Street = address?.Street ?? string.Empty,
            Number = address?.Number ?? string.Empty,
            Complement = address?.Complement ?? string.Empty,
            Neighborhood = address?.Neighborhood ?? string.Empty,
            City = address?.City ?? string.Empty,
            State = address?.State ?? string.Empty,
            TotalOrders = orders.Count,
            TotalSpent = orders.Sum(x => x.Total),
            LastOrderAtUtc = orders.Count == 0 ? null : orders.Max(x => x.CreatedAtUtc),
            IsActive = status?.IsActive ?? true
        };
    }

    private async Task UpdateCustomerPrimaryAddressAsync(
        Guid customerUserId,
        UpdateStoreCustomerRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Cep))
            return;

        var address = await _dbContext.CustomerAddresses
            .Where(x => x.UserId == customerUserId)
            .OrderByDescending(x => x.IsPrimary)
            .ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (address is null)
        {
            address = new CustomerAddress { UserId = customerUserId, IsPrimary = true };
            _dbContext.CustomerAddresses.Add(address);
        }

        address.Cep = new string(request.Cep.Where(char.IsDigit).ToArray());
        address.Street = request.Street?.Trim() ?? string.Empty;
        address.Number = request.Number?.Trim() ?? string.Empty;
        address.Complement = request.Complement?.Trim();
        address.Neighborhood = request.Neighborhood?.Trim() ?? string.Empty;
        address.City = request.City?.Trim() ?? string.Empty;
        address.State = request.State?.Trim().ToUpperInvariant() ?? string.Empty;
        address.IsPrimary = true;
        address.MarkAsUpdated();
    }

    private async Task<bool> IsCustomerRegisteredForStoreAsync(Guid storeId, Guid customerUserId, CancellationToken cancellationToken)
    {
        if (await _dbContext.Orders.AnyAsync(x => x.StoreId == storeId && x.CustomerUserId == customerUserId, cancellationToken))
            return true;

        if (await _dbContext.StoreCustomers.AnyAsync(
            x => x.StoreId == storeId && x.CustomerUserId == customerUserId,
            cancellationToken))
            return true;

        return await _dbContext.CustomerPhoneVerifications.AnyAsync(
            x => x.StoreId == storeId && x.UserId == customerUserId,
            cancellationToken);
    }

    private static bool MatchesSearch(SellerCustomerSummaryResponseDto customer, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return customer.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || customer.Email.Contains(search, StringComparison.OrdinalIgnoreCase)
            || customer.Phone.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesStatus(SellerCustomerSummaryResponseDto customer, string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "active" => customer.IsActive,
            "inactive" => !customer.IsActive,
            _ => true
        };
    }

    private static IEnumerable<SellerCustomerSummaryResponseDto> SortCustomers(
        IEnumerable<SellerCustomerSummaryResponseDto> customers,
        string? sort)
    {
        return sort?.Trim() switch
        {
            "nameAsc" => customers.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            "totalOrdersAsc" => customers.OrderBy(x => x.TotalOrders).ThenByDescending(x => x.LastOrderAtUtc),
            "totalOrdersDesc" => customers.OrderByDescending(x => x.TotalOrders).ThenByDescending(x => x.LastOrderAtUtc),
            "totalSpentAsc" => customers.OrderBy(x => x.TotalSpent).ThenByDescending(x => x.LastOrderAtUtc),
            "totalSpentDesc" => customers.OrderByDescending(x => x.TotalSpent).ThenByDescending(x => x.LastOrderAtUtc),
            _ => customers.OrderByDescending(x => x.LastOrderAtUtc)
        };
    }

    private static bool IsInSameUtcMonth(DateTime value, DateTime reference)
    {
        return value.Year == reference.Year && value.Month == reference.Month;
    }

    public async Task<IReadOnlyCollection<SellerDeliverySummaryResponseDto>> ListStoreDeliveriesAsync(Guid sellerUserId, CancellationToken cancellationToken = default)
    {
        var deliveries = await SellerVisibleOrders(sellerUserId)
            .Where(x => x.FulfillmentType == FulfillmentType.Delivery)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        var customerIds = deliveries.Select(x => x.CustomerUserId).Distinct().ToArray();
        var phones = await _dbContext.Users
            .AsNoTracking()
            .Where(x => customerIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PhoneNumber })
            .ToDictionaryAsync(x => x.Id, x => x.PhoneNumber, cancellationToken);
        var names = await _dbContext.UserClaims
            .AsNoTracking()
            .Where(x => customerIds.Contains(x.UserId) && x.ClaimType == "FullName")
            .Select(x => new { x.UserId, x.ClaimValue })
            .ToDictionaryAsync(x => x.UserId, x => x.ClaimValue, cancellationToken);
        var assignments = await LoadAssignmentSummariesAsync(
            deliveries.Select(x => x.Id).ToArray(),
            cancellationToken);

        return deliveries.Select(x =>
            {
                assignments.TryGetValue(x.Id, out var assignment);
                return new SellerDeliverySummaryResponseDto
                {
                    Id = x.Id,
                    Code = x.Code,
                    CustomerName = names.GetValueOrDefault(x.CustomerUserId),
                    CustomerPhoneNumber = phones.GetValueOrDefault(x.CustomerUserId),
                    AddressSummary = BuildAddressSummary(x),
                    Status = x.Status,
                    Total = x.Total,
                    CreatedAtUtc = x.CreatedAtUtc,
                    DeliveryDriverId = assignment?.DeliveryDriverId,
                    DeliveryDriverName = assignment?.DeliveryDriverName,
                    DeliveryStartedAtUtc = assignment?.DeliveryStartedAtUtc,
                    DeliveryPinConfirmedAtUtc = x.DeliveryPinConfirmedAtUtc,
                    DeliveryRouteId = assignment?.DeliveryRouteId,
                    DeliveryRouteStatus = assignment?.DeliveryRouteStatus
                };
            })
            .ToList();
    }

    private IQueryable<Order> SellerVisibleOrders(Guid sellerUserId)
    {
        return _dbContext.Orders
            .AsNoTracking()
            .Join(
                _dbContext.Stores.AsNoTracking().Where(s => s.OwnerUserId == sellerUserId),
                order => order.StoreId,
                store => store.Id,
                (order, _) => order)
            .Where(x => x.Status != OrderStatus.PendingPayment);
    }

    public async Task<UpdateOrderStatusResultDto> UpdateStatusAsync(
        Guid sellerUserId,
        Guid orderId,
        UpdateOrderStatusRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);

        if (order is null)
        {
            return new UpdateOrderStatusResultDto { NotFound = true };
        }

        var ownsStore = await _dbContext.Stores
            .AsNoTracking()
            .AnyAsync(x => x.Id == order.StoreId && x.OwnerUserId == sellerUserId, cancellationToken);

        if (!ownsStore)
        {
            return new UpdateOrderStatusResultDto { Forbidden = true };
        }

        if (!OrderStatusStateMachine.CanTransition(order.Status, request.NewStatus))
        {
            return new UpdateOrderStatusResultDto { InvalidTransition = true };
        }

        if (request.NewStatus == OrderStatus.OnDelivery)
        {
            var driverResult = await PrepareOnDeliveryAsync(
                order,
                sellerUserId,
                request.DeliveryDriverId,
                cancellationToken);
            if (driverResult is not null)
            {
                return driverResult;
            }
        }

        if (request.NewStatus == OrderStatus.Delivered
            && order.FulfillmentType == FulfillmentType.Delivery)
        {
            // No "own delivery" path: a delivery can only be concluded after a motoboy actually
            // started it. This closes the Ready -> Delivered shortcut left by the state machine.
            var started = await _dbContext.OrderDeliveryAssignments
                .AsNoTracking()
                .AnyAsync(
                    x => x.OrderId == order.Id
                        && x.StoreId == order.StoreId
                        && x.DeliveryStartedAtUtc != null,
                    cancellationToken);

            if (!started)
            {
                return new UpdateOrderStatusResultDto { DriverRequired = true };
            }
        }

        var previousStatus = order.Status;
        var changedAtUtc = DateTime.UtcNow;
        order.Status = request.NewStatus;
        order.StatusVersion += 1;
        order.MarkAsUpdated();

        await _dbContext.OrderStatusHistories.AddAsync(new OrderStatusHistory
        {
            OrderId = order.Id,
            PreviousStatus = previousStatus,
            NewStatus = request.NewStatus,
            ChangedByUserId = sellerUserId,
            Notes = request.Notes?.Trim()
        }, cancellationToken);

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = sellerUserId,
            Event = "OrderStatusUpdated",
            Entity = nameof(Order),
            EntityId = order.Id,
            Description = $"Order status changed from {previousStatus} to {request.NewStatus}.",
            IpAddress = ipAddress
        }, cancellationToken);

        Serilog.Log.Information("{EventType} | Order status changed | OrderId={OrderId} | StoreId={StoreId} | SellerUserId={SellerUserId} | PreviousStatus={PreviousStatus} | NewStatus={NewStatus} | IP={IpAddress}",
            "ORDER_STATUS_CHANGED", order.Id, order.StoreId, sellerUserId, previousStatus, request.NewStatus, ipAddress);

        await _notificationService.NotifyCustomerOrderStatusChangedAsync(
            order.CustomerUserId,
            order.Id,
            request.NewStatus,
            $"Seu pedido {order.Code} foi atualizado para {request.NewStatus}.",
            cancellationToken);

        await _outboxWriter.EnqueueAsync(
            OutboxEventTypes.OrderStatusChanged,
            order.Id,
            new OrderStatusChangedEvent
            {
                OrderId = order.Id,
                StoreId = order.StoreId,
                CustomerUserId = order.CustomerUserId,
                SellerUserId = sellerUserId,
                Code = order.Code,
                PreviousStatus = previousStatus,
                NewStatus = request.NewStatus,
                ChangedAtUtc = changedAtUtc,
                Source = "Seller",
                Sequence = order.StatusVersion
            },
            changedAtUtc,
            sequence: order.StatusVersion,
            aggregateType: nameof(Order),
            cancellationToken);

        if (previousStatus == OrderStatus.Received && request.NewStatus == OrderStatus.Preparing)
        {
            // Backend-authoritative recovery record. Only create print work when the store's
            // authoritative AutoPrint setting is enabled, so enabling it later cannot print old
            // acceptances retroactively.
            var autoPrintEnabled = await _dbContext.StorePrinterConfigs
                .AsNoTracking()
                .AnyAsync(config => config.StoreId == order.StoreId && config.AutoPrint, cancellationToken);

            if (autoPrintEnabled)
            {
                await _dbContext.OrderPrintJobs.AddAsync(new OrderPrintJob
                {
                    OrderId = order.Id,
                    StoreId = order.StoreId,
                    SellerUserId = sellerUserId,
                    Code = order.Code,
                    AcceptanceSequence = order.StatusVersion,
                    AcceptedAtUtc = changedAtUtc,
                    Status = OrderPrintJobStatus.Pending
                }, cancellationToken);

                // Durable print trigger. It is emitted only for the acceptance transition and
                // only when the store's authoritative AutoPrint setting is enabled. A stale local
                // dashboard config can never turn printing on by itself.
                await _outboxWriter.EnqueueAsync(
                    OutboxEventTypes.OrderAcceptedForPrinting,
                    order.Id,
                    new OrderAcceptedForPrintingEvent
                    {
                        OrderId = order.Id,
                        StoreId = order.StoreId,
                        CustomerUserId = order.CustomerUserId,
                        SellerUserId = sellerUserId,
                        Code = order.Code,
                        AcceptedAtUtc = changedAtUtc,
                        Sequence = order.StatusVersion
                    },
                    changedAtUtc,
                    sequence: order.StatusVersion,
                    aggregateType: nameof(Order),
                    cancellationToken);
            }
        }

        try
        {
            await _efUnitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent mutation of the same order already advanced the status and its
            // StatusVersion. Discard this losing write so the winning transition, its history, and
            // the enqueued outbox sequence remain authoritative; the caller must re-read the order.
            foreach (var entry in _dbContext.ChangeTracker.Entries().ToList())
            {
                entry.State = EntityState.Detached;
            }

            Serilog.Log.Warning(
                "Order status update lost a concurrent transition | OrderId={OrderId} | NewStatus={NewStatus}",
                order.Id, request.NewStatus);

            return new UpdateOrderStatusResultDto { ConcurrentUpdate = true };
        }

        var details = await LoadDetailsAsync(order, cancellationToken);
        return new UpdateOrderStatusResultDto
        {
            Order = details
        };
    }

    /// <summary>
    /// Moving a delivery order to OnDelivery is only valid with an active driver from the same
    /// store. The method authoritatively creates/updates the assignment and the delivery PIN
    /// before the status change is persisted by the caller.
    /// </summary>
    private async Task<UpdateOrderStatusResultDto?> PrepareOnDeliveryAsync(
        Order order,
        Guid sellerUserId,
        Guid? requestedDriverId,
        CancellationToken cancellationToken)
    {
        if (order.FulfillmentType != FulfillmentType.Delivery || order.SellerCompletedAtUtc is not null)
        {
            return new UpdateOrderStatusResultDto { InvalidTransition = true };
        }

        var assignment = await _dbContext.OrderDeliveryAssignments
            .SingleOrDefaultAsync(x => x.OrderId == order.Id, cancellationToken);

        StoreDeliveryDriver? link;
        if (requestedDriverId is { } driverId && driverId != Guid.Empty)
        {
            link = await _dbContext.StoreDeliveryDrivers
                .Include(x => x.DeliveryDriver)
                .SingleOrDefaultAsync(
                    x => x.Id == driverId
                        && x.StoreId == order.StoreId
                        && x.Status == StoreDeliveryDriverStatus.Active,
                    cancellationToken);

            if (link is null)
            {
                return new UpdateOrderStatusResultDto { DriverNotAvailable = true };
            }
        }
        else if (assignment is not null)
        {
            link = await _dbContext.StoreDeliveryDrivers
                .Include(x => x.DeliveryDriver)
                .SingleOrDefaultAsync(
                    x => x.StoreId == order.StoreId
                        && x.DeliveryDriverId == assignment.DeliveryDriverId
                        && x.Status == StoreDeliveryDriverStatus.Active,
                    cancellationToken);

            if (link is null)
            {
                return new UpdateOrderStatusResultDto { DriverRequired = true };
            }
        }
        else
        {
            return new UpdateOrderStatusResultDto { DriverRequired = true };
        }

        if (assignment is not null
            && assignment.DeliveryStartedAtUtc is not null
            && assignment.DeliveryDriverId != link.DeliveryDriverId)
        {
            return new UpdateOrderStatusResultDto { InvalidTransition = true };
        }

        // The transition to OnDelivery is the authoritative moment to issue the PIN. The PIN is
        // always the last four digits of the customer's phone: without that deterministic value
        // the customer could not share it with the motoboy, so the transition is rejected.
        var customerPhone = await _dbContext.Users
            .AsNoTracking()
            .Where(x => x.Id == order.CustomerUserId)
            .Select(x => x.PhoneNumber)
            .SingleOrDefaultAsync(cancellationToken);

        if (!DeliveryPinCode.TryFromCustomerPhone(customerPhone, out var pin))
        {
            return new UpdateOrderStatusResultDto { CustomerPhoneRequired = true };
        }

        order.DeliveryPinHash = DeliveryPinCode.Hash(order, pin);
        order.DeliveryPinEncrypted = _encryptionService?.Encrypt(pin);
        order.DeliveryPinConfirmedAtUtc = null;
        order.DeliveryPinConfirmedByDriverId = null;

        var now = DateTime.UtcNow;
        if (assignment is null)
        {
            assignment = new OrderDeliveryAssignment
            {
                OrderId = order.Id,
                StoreId = order.StoreId,
                DeliveryDriverId = link.DeliveryDriverId,
                AssignedByUserId = sellerUserId,
                AssignedAtUtc = now,
                DeliveryStartedAtUtc = now,
                PinAttemptCount = 0
            };
            await _dbContext.OrderDeliveryAssignments.AddAsync(assignment, cancellationToken);
        }
        else
        {
            assignment.PinAttemptCount = 0;
            assignment.LastPinAttemptAtUtc = null;
            assignment.DeliveryDriverId = link.DeliveryDriverId;
            assignment.AssignedByUserId = sellerUserId;
            assignment.AssignedAtUtc = now;
            assignment.DeliveryStartedAtUtc ??= now;
            assignment.AdvanceConcurrency();
            assignment.MarkAsUpdated();
        }

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = sellerUserId,
            Event = "OrderDeliveryPinIssued",
            Entity = nameof(Order),
            EntityId = order.Id,
            Description = $"PIN de entrega emitido para o pedido {order.Code} ao sair para entrega."
        }, cancellationToken);

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = sellerUserId,
            Event = "OrderDeliveryDriverAssigned",
            Entity = nameof(Order),
            EntityId = order.Id,
            Description = $"Pedido {order.Code} atribuído ao motoboy {link.DeliveryDriver?.FullName ?? link.DisplayName} ao sair para entrega."
        }, cancellationToken);

        return null;
    }

    public async Task<UpdateOrderConfirmationResultDto> ConfirmDeliveryAsync(
        Guid customerUserId,
        Guid orderId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);

        if (order is null)
        {
            return new UpdateOrderConfirmationResultDto { NotFound = true };
        }

        if (order.CustomerUserId != customerUserId)
        {
            return new UpdateOrderConfirmationResultDto { Forbidden = true };
        }

        if (order.Status != OrderStatus.Delivered || order.FulfillmentType != FulfillmentType.Delivery)
        {
            return new UpdateOrderConfirmationResultDto { InvalidState = true };
        }

        if (order.DeliveryConfirmedAtUtc is not null)
        {
            return new UpdateOrderConfirmationResultDto
            {
                Order = await LoadDetailsAsync(order, cancellationToken)
            };
        }

        order.DeliveryConfirmedAtUtc = DateTime.UtcNow;
        order.MarkAsUpdated();

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = customerUserId,
            Event = "OrderDeliveryConfirmed",
            Entity = nameof(Order),
            EntityId = order.Id,
            Description = "Customer confirmed order delivery.",
            IpAddress = ipAddress
        }, cancellationToken);

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return new UpdateOrderConfirmationResultDto
        {
            Order = await LoadDetailsAsync(order, cancellationToken)
        };
    }

    public async Task<CompleteSellerOrderResultDto> CompleteForSellerBoardAsync(
        Guid sellerUserId,
        Guid orderId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);

        if (order is null)
        {
            return new CompleteSellerOrderResultDto { NotFound = true };
        }

        var ownsStore = await _dbContext.Stores
            .AsNoTracking()
            .AnyAsync(x => x.Id == order.StoreId && x.OwnerUserId == sellerUserId, cancellationToken);

        if (!ownsStore)
        {
            return new CompleteSellerOrderResultDto { Forbidden = true };
        }

        if (order.SellerCompletedAtUtc is not null)
        {
            return new CompleteSellerOrderResultDto
            {
                Order = await LoadDetailsAsync(order, cancellationToken)
            };
        }

        if (order.Status != OrderStatus.Delivered)
        {
            return new CompleteSellerOrderResultDto { InvalidState = true };
        }

        if (order.FulfillmentType == FulfillmentType.Delivery && order.DeliveryConfirmedAtUtc is null)
        {
            return new CompleteSellerOrderResultDto { InvalidState = true };
        }

        order.SellerCompletedAtUtc = DateTime.UtcNow;
        order.MarkAsUpdated();

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = sellerUserId,
            Event = "OrderSellerCompleted",
            Entity = nameof(Order),
            EntityId = order.Id,
            Description = "Seller marked order as completed on the day board.",
            IpAddress = ipAddress
        }, cancellationToken);

        try
        {
            await _efUnitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            var current = await _dbContext.Orders
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);

            if (current is null)
            {
                return new CompleteSellerOrderResultDto { NotFound = true };
            }

            return new CompleteSellerOrderResultDto
            {
                Order = await LoadDetailsAsync(current, cancellationToken)
            };
        }

        return new CompleteSellerOrderResultDto
        {
            Order = await LoadDetailsAsync(order, cancellationToken)
        };
    }

    private async Task<OrderDetailsResponseDto> LoadDetailsAsync(
        Order order,
        CancellationToken cancellationToken,
        bool includeDeliveryPin = false)
    {
        var itemRows = await _dbContext.OrderItems
            .AsNoTracking()
            .Where(x => x.OrderId == order.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.ProductName,
                x.Quantity,
                x.UnitPrice,
                x.TotalPrice,
                x.Notes,
                x.VariationName,
                x.WeightGrams,
                x.ChoiceOptionName,
                x.AdditionalNames,
                x.OptionPricesJson
            })
            .ToListAsync(cancellationToken);

        var items = itemRows
            .Select(x => new OrderItemResponseDto
            {
                ProductName = x.ProductName,
                Quantity = x.Quantity,
                UnitPrice = x.UnitPrice,
                TotalPrice = x.TotalPrice,
                Notes = x.Notes,
                VariationName = x.VariationName,
                WeightGrams = x.WeightGrams,
                ChoiceOptionName = x.ChoiceOptionName,
                AdditionalNames = x.AdditionalNames,
                OptionPrices = DeserializeOptionPrices(x.OptionPricesJson)
            })
            .ToList();

        var customer = await _dbContext.Users
            .AsNoTracking()
            .Where(x => x.Id == order.CustomerUserId)
            .Select(x => new { x.PhoneNumber })
            .SingleOrDefaultAsync(cancellationToken);

        var customerName = await _dbContext.UserClaims
            .AsNoTracking()
            .Where(x => x.UserId == order.CustomerUserId && x.ClaimType == "FullName")
            .Select(x => x.ClaimValue)
            .SingleOrDefaultAsync(cancellationToken);

        var history = await _dbContext.OrderStatusHistories
            .AsNoTracking()
            .Where(x => x.OrderId == order.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new OrderStatusHistoryResponseDto
            {
                CreatedAtUtc = x.CreatedAtUtc,
                PreviousStatus = x.PreviousStatus,
                NewStatus = x.NewStatus,
                ChangedByUserId = x.ChangedByUserId,
                Notes = x.Notes
            })
            .ToListAsync(cancellationToken);

        var assignment = await (
            from current in _dbContext.OrderDeliveryAssignments.AsNoTracking()
            join driver in _dbContext.DeliveryDrivers.AsNoTracking()
                on current.DeliveryDriverId equals driver.Id
            where current.OrderId == order.Id
            select new
            {
                current.DeliveryDriverId,
                DriverName = driver.FullName,
                current.DeliveryStartedAtUtc
            }
        ).SingleOrDefaultAsync(cancellationToken);

        var routeInfo = await (
            from stop in _dbContext.DeliveryRouteOrders.AsNoTracking()
            join route in _dbContext.DeliveryRoutes.AsNoTracking() on stop.DeliveryRouteId equals route.Id
            where stop.OrderId == order.Id
            orderby route.CreatedAtUtc descending
            select new
            {
                RouteId = route.Id,
                route.RevokedAtUtc,
                route.CompletedAtUtc,
                route.ExpiresAtUtc
            }
        ).FirstOrDefaultAsync(cancellationToken);

        string? deliveryPin = null;
        if (includeDeliveryPin
            && order.FulfillmentType == FulfillmentType.Delivery
            && order.Status == OrderStatus.OnDelivery
            && order.DeliveryConfirmedAtUtc is null
            && !string.IsNullOrWhiteSpace(order.DeliveryPinEncrypted)
            && _encryptionService is not null)
        {
            try
            {
                deliveryPin = _encryptionService.Decrypt(order.DeliveryPinEncrypted);
            }
            catch (Exception exception)
            {
                // A key rotation can make an old PIN unreadable. The delivery itself stays valid
                // and the customer keeps the existing manual confirmation flow.
                Serilog.Log.Warning(exception,
                    "Could not decrypt delivery PIN | OrderId={OrderId}",
                    order.Id);
            }
        }

        return new OrderDetailsResponseDto
        {
            Id = order.Id,
            Code = order.Code,
            CustomerUserId = order.CustomerUserId,
            CustomerName = customerName,
            CustomerPhoneNumber = customer?.PhoneNumber,
            StoreId = order.StoreId,
            FulfillmentType = order.FulfillmentType,
            Status = order.Status,
            PaymentMethod = order.PaymentMethod,
            Subtotal = order.Subtotal,
            DeliveryFee = order.DeliveryFee,
            Total = order.Total,
            CreatedAtUtc = order.CreatedAtUtc,
            DeliveryConfirmedAtUtc = order.DeliveryConfirmedAtUtc,
            SellerCompletedAtUtc = order.SellerCompletedAtUtc,
            DeliveryDriverId = assignment?.DeliveryDriverId,
            DeliveryDriverName = assignment?.DriverName,
            DeliveryStartedAtUtc = assignment?.DeliveryStartedAtUtc,
            DeliveryPinConfirmedAtUtc = order.DeliveryPinConfirmedAtUtc,
            DeliveryRouteId = routeInfo?.RouteId,
            DeliveryRouteStatus = routeInfo is null
                ? null
                : GetRouteStatus(
                    routeInfo.RevokedAtUtc,
                    routeInfo.CompletedAtUtc,
                    routeInfo.ExpiresAtUtc,
                    DateTime.UtcNow),
            DeliveryPin = deliveryPin,
            AddressCep = order.AddressCep,
            AddressStreet = order.AddressStreet,
            AddressNumber = order.AddressNumber,
            AddressNeighborhood = order.AddressNeighborhood,
            AddressCity = order.AddressCity,
            AddressState = order.AddressState,
            AddressComplement = order.AddressComplement,
            AddressReference = order.AddressReference,
            Notes = order.Notes,
            Items = items,
            History = history
        };
    }
}
