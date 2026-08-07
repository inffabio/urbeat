using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Services;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace Urbeat.Infrastructure.Services;

public sealed class OrderService : IOrderService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEfUnitOfWork _efUnitOfWork;
    private readonly INotificationService _notificationService;

    public OrderService(
        ApplicationDbContext dbContext,
        IEfUnitOfWork efUnitOfWork,
        INotificationService notificationService)
    {
        _dbContext = dbContext;
        _efUnitOfWork = efUnitOfWork;
        _notificationService = notificationService;
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
                Total = x.Total,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<OrderDetailsResponseDto?> GetCustomerOrderAsync(Guid customerUserId, Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == orderId && x.CustomerUserId == customerUserId, cancellationToken);

        return order is null ? null : await LoadDetailsAsync(order, cancellationToken);
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

        var items = orderPage
            .Select(x => new OrderSummaryResponseDto
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
                Total = x.Total,
                CreatedAtUtc = x.CreatedAtUtc
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

    private static string? BuildAddressSummary(Order order)
    {
        var streetLine = string.Join(", ", new[] { order.AddressStreet, order.AddressNumber }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var parts = new[] { streetLine, order.AddressNeighborhood }.Where(x => !string.IsNullOrWhiteSpace(x));
        var summary = string.Join(" - ", parts);
        return string.IsNullOrWhiteSpace(summary) ? null : summary;
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

        return deliveries.Select(x => new SellerDeliverySummaryResponseDto
            {
                Id = x.Id,
                Code = x.Code,
                CustomerName = names.GetValueOrDefault(x.CustomerUserId),
                CustomerPhoneNumber = phones.GetValueOrDefault(x.CustomerUserId),
                AddressSummary = BuildAddressSummary(x),
                Status = x.Status,
                Total = x.Total,
                CreatedAtUtc = x.CreatedAtUtc
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

        var previousStatus = order.Status;
        order.Status = request.NewStatus;
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

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        var details = await LoadDetailsAsync(order, cancellationToken);
        return new UpdateOrderStatusResultDto
        {
            Order = details
        };
    }

    private async Task<OrderDetailsResponseDto> LoadDetailsAsync(Order order, CancellationToken cancellationToken)
    {
        var items = await _dbContext.OrderItems
            .AsNoTracking()
            .Where(x => x.OrderId == order.Id)
            .OrderBy(x => x.CreatedAtUtc)
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
                AdditionalNames = x.AdditionalNames
            })
            .ToListAsync(cancellationToken);

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
