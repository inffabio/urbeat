using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Geo;
using Urbeat.Domain.Services;
using Urbeat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Urbeat.Infrastructure.Services;

public sealed class CheckoutService : ICheckoutService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEfUnitOfWork _efUnitOfWork;
    private readonly INotificationService _notificationService;
    private readonly IOutboxWriter _outboxWriter;
    private readonly UserManager<IdentityUser<Guid>> _userManager;
    private readonly IPricingService _pricingService;

    public CheckoutService(
        ApplicationDbContext dbContext,
        IEfUnitOfWork efUnitOfWork,
        INotificationService notificationService,
        IOutboxWriter outboxWriter,
        UserManager<IdentityUser<Guid>> userManager,
        IPricingService pricingService)
    {
        _dbContext = dbContext;
        _efUnitOfWork = efUnitOfWork;
        _notificationService = notificationService;
        _outboxWriter = outboxWriter;
        _userManager = userManager;
        _pricingService = pricingService;
    }

    public async Task<CheckoutResultDto> PreviewAsync(
        Guid? customerUserId,
        CheckoutRequestDto request,
        CancellationToken cancellationToken = default)
    {
        return await BuildCheckoutResultAsync(customerUserId, request, persistOrder: false, ipAddress: null, cancellationToken);
    }

    public async Task<CheckoutResultDto> ConfirmAsync(
        Guid customerUserId,
        CheckoutRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        return await BuildCheckoutResultAsync(customerUserId, request, persistOrder: true, ipAddress, cancellationToken);
    }

    private async Task<CheckoutResultDto> BuildCheckoutResultAsync(
        Guid? customerUserId,
        CheckoutRequestDto request,
        bool persistOrder,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var store = await _dbContext.Stores
            .Include(x => x.DeliveryAreas)
            .SingleOrDefaultAsync(x => x.Id == request.StoreId, cancellationToken);

        if (store is null)
        {
            Serilog.Log.Warning("{EventType} | Checkout failed | StoreId={StoreId} | Reason=store_not_found | IP={IpAddress}", "ORDER_CREATE_FAILED", request.StoreId, ipAddress);
            return new CheckoutResultDto { StoreNotFound = true };
        }

        if (!store.IsOpen)
        {
            Serilog.Log.Warning("{EventType} | Checkout failed | StoreId={StoreId} | Reason=store_closed | IP={IpAddress}", "ORDER_CREATE_FAILED", request.StoreId, ipAddress);
            return new CheckoutResultDto { StoreClosed = true };
        }

        if (store.IsSubscriptionBlocked)
        {
            Serilog.Log.Warning("{EventType} | Checkout failed | StoreId={StoreId} | Reason=store_blocked | IP={IpAddress}", "ORDER_CREATE_FAILED", request.StoreId, ipAddress);
            return new CheckoutResultDto { StoreBlocked = true };
        }

        var isDelivery = request.FulfillmentType == FulfillmentType.Delivery;

        StoreAddress? pickupStoreAddress = null;
        if (!isDelivery && persistOrder)
        {
            pickupStoreAddress = await _dbContext.StoreAddresses
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.StoreId == store.Id, cancellationToken);

            if (pickupStoreAddress is null
                || string.IsNullOrWhiteSpace(pickupStoreAddress.ZipCode)
                || string.IsNullOrWhiteSpace(pickupStoreAddress.Street)
                || string.IsNullOrWhiteSpace(pickupStoreAddress.Number)
                || string.IsNullOrWhiteSpace(pickupStoreAddress.Neighborhood)
                || string.IsNullOrWhiteSpace(pickupStoreAddress.City)
                || string.IsNullOrWhiteSpace(pickupStoreAddress.State))
            {
                Serilog.Log.Warning("{EventType} | Checkout failed | StoreId={StoreId} | Reason=pickup_store_address_missing | IP={IpAddress}", "ORDER_CREATE_FAILED", request.StoreId, ipAddress);
                return new CheckoutResultDto { AddressNotFound = true };
            }
        }

        CustomerAddress? address = null;
        if (isDelivery && request.CustomerAddressId.HasValue && customerUserId.HasValue)
        {
            address = await _dbContext.CustomerAddresses
                .SingleOrDefaultAsync(x => x.Id == request.CustomerAddressId && x.UserId == customerUserId.Value, cancellationToken);
        }

        // Endereço só é obrigatório ao confirmar o pedido (não no preview).
        if (isDelivery && persistOrder)
        {
            if (!request.CustomerAddressId.HasValue)
            {
                Serilog.Log.Warning("{EventType} | Checkout failed | CustomerUserId={CustomerUserId} | StoreId={StoreId} | Reason=delivery_without_address | IP={IpAddress}", "ORDER_CREATE_FAILED", customerUserId, request.StoreId, ipAddress);
                return new CheckoutResultDto { AddressNotFound = true };
            }

            if (!customerUserId.HasValue || address is null)
            {
                Serilog.Log.Warning("{EventType} | Checkout failed | CustomerUserId={CustomerUserId} | StoreId={StoreId} | Reason=address_not_found | IP={IpAddress}", "ORDER_CREATE_FAILED", customerUserId, request.StoreId, ipAddress);
                return new CheckoutResultDto { AddressNotFound = true };
            }
        }

        var minimumOrderValue = isDelivery ? store.MinimumOrderValue : 0m;

        // ── Preço autoritativo: recomputa cada item a partir do produto persistido ──
        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _dbContext.Products
            .Include(p => p.Additionals)
            .Include(p => p.ChoiceOptions)
            .Include(p => p.Variations)
            .Include(p => p.WeightConfig)
            .Include(p => p.OptionGroups).ThenInclude(g => g.Items)
            .Where(p => p.StoreId == store.Id && productIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        var pricedItems = new List<(CheckoutItemRequestDto Item, ItemPricingResultDto Pricing)>();
        foreach (var item in request.Items)
        {
            var product = products.FirstOrDefault(p => p.Id == item.ProductId);
            if (product is null)
            {
                Serilog.Log.Warning("{EventType} | Checkout failed | StoreId={StoreId} | Reason=product_not_found | ProductId={ProductId} | IP={IpAddress}", "ORDER_CREATE_FAILED", request.StoreId, item.ProductId, ipAddress);
                return new CheckoutResultDto { InvalidItems = true, ItemError = "Produto não encontrado ou indisponível." };
            }

            if (!product.IsAvailable)
                return new CheckoutResultDto { InvalidItems = true, ItemError = $"O produto \"{product.Name}\" está indisponível." };

            var pricing = _pricingService.PriceItem(product, item);
            if (!pricing.IsValid)
                return new CheckoutResultDto { InvalidItems = true, ItemError = pricing.Error };

            pricedItems.Add((item, pricing));
        }

        var subtotal = pricedItems.Sum(x => x.Item.Quantity * x.Pricing.UnitPrice);

        // ── Frete: grátis hoje ativo → frete 0; grátis acima do limite; senão taxa por região (bairro); sem área = bloqueia ──
        var freeShippingThreshold = store.FreeShippingThreshold;
        var freeShippingApplied = false;
        var deliveryFee = 0m;
        if (isDelivery)
        {
            if (store.FreeShippingToday
                && store.FreeShippingTodayDate == StoreOpeningHoursCalculator.GetSaoPauloDate(DateTimeOffset.UtcNow))
            {
                freeShippingApplied = true;
                deliveryFee = 0m;
            }
            else if (freeShippingThreshold is > 0m && subtotal >= freeShippingThreshold.Value)
            {
                freeShippingApplied = true;
                deliveryFee = 0m;
            }
            else if (isDelivery && request.NeighborhoodId is not null)
            {
                // Explicit canonical neighborhood from the client is a hint only: the store's own
                // active delivery areas remain authoritative and no fee is accepted from the client.
                var requestedArea = store.DeliveryAreas
                    .FirstOrDefault(a => a.NeighborhoodId == request.NeighborhoodId && a.IsActive);

                if (requestedArea is null)
                {
                    return new CheckoutResultDto { DeliveryAreaNotCovered = true };
                }

                deliveryFee = requestedArea.DeliveryFee;
            }
            else if (address is not null)
            {
                // The server is authoritative: resolve the customer address to the canonical
                // neighborhood identifier of the store city before falling back to the textual
                // name during the transition. The client name is only a hint.
                var normalized = NeighborhoodName.Normalize(address.Neighborhood);
                var resolvedNeighborhoodId = await ResolveCustomerNeighborhoodIdAsync(address, cancellationToken);

                var area = resolvedNeighborhoodId is not null
                    ? store.DeliveryAreas.FirstOrDefault(a => a.NeighborhoodId == resolvedNeighborhoodId)
                    : null;

                area ??= store.DeliveryAreas.FirstOrDefault(a => NeighborhoodName.Normalize(a.Neighborhood) == normalized);
                if (area is not null)
                {
                    deliveryFee = area.DeliveryFee;
                }
                else
                {
                    // Bairro não coberto — bloqueia a compra (o lojista precisa cadastrar)
                    return new CheckoutResultDto { DeliveryAreaNotCovered = true };
                }
            }
            else
            {
                // Delivery sem endereço (ex.: carrinho): a região é desconhecida.
                deliveryFee = 0m;
            }
        }

        if (subtotal < minimumOrderValue)
        {
            Serilog.Log.Warning("{EventType} | Checkout failed | CustomerUserId={CustomerUserId} | StoreId={StoreId} | Reason=below_minimum | IP={IpAddress}", "ORDER_CREATE_FAILED", customerUserId, request.StoreId, ipAddress);
            var belowResult = new CheckoutResultDto
            {
                BelowMinimum = true,
                Summary = new CheckoutSummaryResponseDto
                {
                    StoreId = store.Id,
                    FulfillmentType = request.FulfillmentType,
                    CustomerAddressId = address?.Id,
                    PaymentMethod = request.PaymentMethod ?? Domain.Entities.PaymentMethod.CashOnDelivery,
                    CashWithoutChange = request.CashWithoutChange,
                    CashChangeForAmount = request.CashChangeForAmount,
                    Subtotal = subtotal,
                    DeliveryFee = deliveryFee,
                    MinimumOrderValue = minimumOrderValue,
                    FreeShippingThreshold = freeShippingThreshold,
                    FreeShippingApplied = freeShippingApplied,
                    Total = subtotal + deliveryFee,
                    StoreIsOpen = store.IsOpen
                }
            };
            if (!isDelivery)
                belowResult.MinimumNotMetForPickUp = true;
            return belowResult;
        }

        var summary = new CheckoutSummaryResponseDto
        {
            StoreId = store.Id,
            FulfillmentType = request.FulfillmentType,
            CustomerAddressId = address?.Id,
            PaymentMethod = request.PaymentMethod ?? Domain.Entities.PaymentMethod.CashOnDelivery,
            CashWithoutChange = request.CashWithoutChange,
            CashChangeForAmount = request.CashChangeForAmount,
            Subtotal = subtotal,
            DeliveryFee = deliveryFee,
            MinimumOrderValue = minimumOrderValue,
            FreeShippingThreshold = freeShippingThreshold,
            FreeShippingApplied = freeShippingApplied,
            Total = subtotal + deliveryFee,
            StoreIsOpen = store.IsOpen
        };

        if (!persistOrder)
        {
            return new CheckoutResultDto { Summary = summary };
        }

        // Price security: the customer only accepts the total they saw in the preview. Any
        // divergence is rejected before payment validation, order persistence, outbox, printing or
        // notifications. Older clients that do not send ExpectedTotal keep working with the
        // authoritative total recomputed below (explicit compatibility path).
        if (request.ExpectedTotal.HasValue
            && decimal.Round(request.ExpectedTotal.Value, 2) != decimal.Round(summary.Total, 2))
        {
            Serilog.Log.Warning("{EventType} | Checkout failed | CustomerUserId={CustomerUserId} | StoreId={StoreId} | Reason=total_changed | ExpectedTotal={ExpectedTotal} | AuthoritativeTotal={AuthoritativeTotal} | IP={IpAddress}",
                "ORDER_CREATE_FAILED", customerUserId, request.StoreId, request.ExpectedTotal, summary.Total, ipAddress);

            return new CheckoutResultDto
            {
                TotalChanged = true,
                AuthoritativeTotal = summary.Total,
                Summary = summary,
                PaymentError = "O total do pedido mudou. Confira o novo valor e confirme novamente."
            };
        }

        // Authoritative payment policy for new orders. Online Pix is no longer offered by the
        // storefront; old orders keep their historical method and in-flight gateway transactions.
        if (!request.PaymentMethod.HasValue || !OrderPaymentPolicy.SupportsNewOrder(request.PaymentMethod.Value))
        {
            Serilog.Log.Warning("{EventType} | Checkout failed | CustomerUserId={CustomerUserId} | StoreId={StoreId} | Reason=payment_method_not_allowed | PaymentMethod={PaymentMethod} | IP={IpAddress}", "ORDER_CREATE_FAILED", customerUserId, request.StoreId, request.PaymentMethod, ipAddress);
            return new CheckoutResultDto
            {
                PaymentMethodNotAllowed = true,
                PaymentError = request.PaymentMethod.HasValue
                    ? "Esta forma de pagamento não está mais disponível. Atualize o aplicativo ou recarregue a página e escolha Pix na maquininha, dinheiro, débito ou crédito."
                    : "Escolha Pix na maquininha, dinheiro, débito ou crédito para este pedido."
            };
        }

        var paymentMethod = request.PaymentMethod.Value;
        // Compatibility path for cached storefronts (PWA/Capacitor) written before the cash-change
        // contract: they send cash without the selection and without ExpectedTotal. Keep them on the
        // legacy flow instead of rejecting the order. No decision is invented: the change fields stay
        // null, so RequiresHandoverConfirmation stays false and the previous PIN-only flow is used.
        var isLegacyCashCheckout = OrderPaymentPolicy.IsLegacyCashCheckout(
            request.ExpectedTotal,
            request.CashWithoutChange);

        if (OrderPaymentPolicy.IsCashOnDelivery(paymentMethod))
        {
            if (isLegacyCashCheckout)
            {
                Serilog.Log.Information("{EventType} | Checkout accepted legacy cash payload | CustomerUserId={CustomerUserId} | StoreId={StoreId} | IP={IpAddress}",
                    "CHECKOUT_LEGACY_CASH", customerUserId, request.StoreId, ipAddress);
            }
            else
            {
                var cashError = OrderPaymentPolicy.ValidateCashSelection(
                    summary.Total,
                    request.CashWithoutChange,
                    request.CashChangeForAmount);
                if (cashError is not null)
                {
                    Serilog.Log.Warning("{EventType} | Checkout failed | CustomerUserId={CustomerUserId} | StoreId={StoreId} | Reason=invalid_cash_change | IP={IpAddress}", "ORDER_CREATE_FAILED", customerUserId, request.StoreId, ipAddress);
                    return new CheckoutResultDto
                    {
                        InvalidCashChange = true,
                        PaymentError = cashError
                    };
                }
            }
        }
        else if (request.CashWithoutChange.HasValue || request.CashChangeForAmount.HasValue)
        {
            return new CheckoutResultDto
            {
                InvalidCashChange = true,
                PaymentError = "A escolha de troco só é válida para pagamento em dinheiro."
            };
        }

        var order = new Order
        {
            CustomerUserId = customerUserId!.Value,
            StoreId = store.Id,
            FulfillmentType = request.FulfillmentType,
            Notes = request.Notes?.Trim(),
            PaymentMethod = paymentMethod,
            // Every supported storefront method is paid at handover, so the order reaches the
            // seller immediately. Online Pix/Card remain readable for legacy orders only.
            Status = OrderStatus.Received,
            // Null for legacy cached checkouts: no cash decision is invented for them.
            CashWithoutChange = request.CashWithoutChange,
            CashChangeForAmount = request.CashChangeForAmount,
            Subtotal = summary.Subtotal,
            DeliveryFee = summary.DeliveryFee,
            Total = summary.Total
        };

        if (isDelivery && address is not null)
        {
            order.CustomerAddressId = address.Id;
            order.AddressCep = address.Cep;
            order.AddressStreet = address.Street;
            order.AddressNumber = address.Number;
            order.AddressNeighborhood = address.Neighborhood;
            order.AddressCity = address.City;
            order.AddressState = address.State;
            order.AddressComplement = address.Complement;
            order.AddressReference = address.Reference;
            // Snapshot of the destination coordinates at checkout. The driver portal reads the
            // order snapshot, so a later address edit cannot move an order in progress.
            order.DeliveryLatitude = address.Latitude;
            order.DeliveryLongitude = address.Longitude;
        }
        else if (pickupStoreAddress is not null)
        {
            order.PickupStoreAddressZipCode = pickupStoreAddress.ZipCode.Trim();
            order.PickupStoreAddressStreet = pickupStoreAddress.Street.Trim();
            order.PickupStoreAddressNumber = pickupStoreAddress.Number.Trim();
            order.PickupStoreAddressNeighborhood = pickupStoreAddress.Neighborhood.Trim();
            order.PickupStoreAddressCity = pickupStoreAddress.City.Trim();
            order.PickupStoreAddressState = pickupStoreAddress.State.Trim();
            order.PickupStoreAddressComplement = pickupStoreAddress.Complement?.Trim();
            order.PickupStoreAddressReference = pickupStoreAddress.Reference?.Trim();
        }

        order.Code = await GenerateOrderCodeAsync(cancellationToken);
        order.StatusVersion = 1;

        // The delivery PIN is intentionally not created at checkout. It is generated by the
        // authoritative transition to OnDelivery, when the customer can finally share it.

        Serilog.Log.Information("{EventType} | Order created | OrderId={OrderId} | Code={Code} | CustomerUserId={CustomerUserId} | StoreId={StoreId} | Total={Total} | PaymentMethod={PaymentMethod} | FulfillmentType={FulfillmentType} | IP={IpAddress}",
            "ORDER_CREATED", order.Id, order.Code, customerUserId, store.Id, summary.Total, request.PaymentMethod, request.FulfillmentType, ipAddress);

        await _dbContext.Orders.AddAsync(order, cancellationToken);

        var items = pricedItems.Select(x => new OrderItem
        {
            OrderId = order.Id,
            ProductName = x.Pricing.ProductName,
            Quantity = x.Item.Quantity,
            UnitPrice = x.Pricing.UnitPrice,
            TotalPrice = x.Item.Quantity * x.Pricing.UnitPrice,
            Notes = x.Item.Notes?.Trim(),
            VariationName = x.Pricing.VariationName,
            WeightGrams = x.Pricing.WeightGrams,
            ChoiceOptionName = x.Pricing.ChoiceOptionName,
            AdditionalNames = x.Pricing.ExtraNames.Count > 0 ? string.Join(", ", x.Pricing.ExtraNames) : null,
            OptionPricesJson = x.Pricing.OptionPrices.Count > 0
                ? JsonSerializer.Serialize(x.Pricing.OptionPrices)
                : null
        }).ToList();

        await _dbContext.OrderItems.AddRangeAsync(items, cancellationToken);

        await _dbContext.OrderStatusHistories.AddAsync(new OrderStatusHistory
        {
            OrderId = order.Id,
            PreviousStatus = OrderStatus.Created,
            NewStatus = order.Status,
            ChangedByUserId = customerUserId.Value,
            Notes = "Initial order status set at checkout confirmation."
        }, cancellationToken);

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = customerUserId.Value,
            Event = "CheckoutConfirmed",
            Entity = nameof(Order),
            EntityId = order.Id,
            Description = "Checkout confirmed and order created.",
            IpAddress = ipAddress
        }, cancellationToken);

        var paymentLabel = order.PaymentMethod switch
        {
            PaymentMethod.CardOnDelivery => "cartão na entrega",
            PaymentMethod.CashOnDelivery => "dinheiro na entrega",
            PaymentMethod.PixOnDelivery => "Pix na maquininha",
            PaymentMethod.DebitOnDelivery => "débito na maquininha",
            PaymentMethod.CreditOnDelivery => "crédito na maquininha",
            PaymentMethod.PixOnline => "Pix online",
            PaymentMethod.CardOnline => "cartão online",
            _ => "pagamento"
        };

        var fulfillmentLabel = isDelivery ? "entrega" : "retirada";

        var isPayOnDelivery = order.PaymentMethod is PaymentMethod.CashOnDelivery
            or PaymentMethod.CardOnDelivery
            or PaymentMethod.PixOnDelivery
            or PaymentMethod.DebitOnDelivery
            or PaymentMethod.CreditOnDelivery;

        var orderMessage = isPayOnDelivery
            ? $"Novo pedido {order.Code} recebido para {fulfillmentLabel} com pagamento via {paymentLabel}."
            : $"Novo pedido {order.Code} para {fulfillmentLabel} com pagamento via {paymentLabel}. Aguardando confirmação.";

        if (isPayOnDelivery)
        {
            await _notificationService.NotifySellerNewOrderAsync(
                store.OwnerUserId,
                order.Id,
                orderMessage,
                cancellationToken);
        }

        if (isPayOnDelivery)
        {
            await _notificationService.NotifyCustomerOrderStatusChangedAsync(
                customerUserId.Value,
                order.Id,
                OrderStatus.Received,
                $"Seu pedido {order.Code} foi recebido pela loja.",
                cancellationToken);
        }

        await _outboxWriter.EnqueueAsync(
            OutboxEventTypes.OrderCreated,
            order.Id,
            new OrderCreatedEvent
            {
                OrderId = order.Id,
                StoreId = order.StoreId,
                CustomerUserId = order.CustomerUserId,
                SellerUserId = store.OwnerUserId,
                Code = order.Code,
                Status = order.Status,
                OccurredAtUtc = DateTime.UtcNow,
                Sequence = order.StatusVersion
            },
            DateTime.UtcNow,
            sequence: order.StatusVersion,
            aggregateType: nameof(Order),
            cancellationToken);

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return new CheckoutResultDto
        {
            Summary = summary,
            Confirmation = new CheckoutConfirmResponseDto
            {
                OrderId = order.Id,
                Code = order.Code,
                FulfillmentType = order.FulfillmentType,
                Status = order.Status,
                Subtotal = order.Subtotal,
                DeliveryFee = order.DeliveryFee,
                Total = order.Total
            }
        };
    }

    private const string CodeChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const int CodeLength = 8;

    /// <summary>
    /// Resolves the persisted customer address to a canonical neighborhood of the city, preferring
    /// the identifier over the free-text name. Ambiguous or unmatched addresses return null and the
    /// caller falls back to the textual comparison used before the migration.
    /// </summary>
    private async Task<Guid?> ResolveCustomerNeighborhoodIdAsync(
        CustomerAddress address,
        CancellationToken cancellationToken)
    {
        var normalizedNeighborhood = NeighborhoodName.Normalize(address.Neighborhood);
        if (normalizedNeighborhood.Length == 0)
        {
            return null;
        }

        var normalizedCity = NeighborhoodName.Normalize(address.City);
        var uf = address.State?.Trim().ToUpperInvariant();

        var cityId = normalizedCity.Length == 0
            ? (Guid?)null
            : await _dbContext.Cities
                .AsNoTracking()
                .Where(x => x.NormalizedName == normalizedCity && (uf == null || x.Uf == uf))
                .OrderBy(x => x.Name)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

        var query = _dbContext.DeliveryNeighborhoods
            .AsNoTracking()
            .Where(x => x.NormalizedName == normalizedNeighborhood);

        query = cityId is not null
            ? query.Where(x => x.CityId == cityId)
            : query.Where(x => x.City.ToLower() == address.City.ToLower().Trim());

        return await query
            .OrderByDescending(x => x.IsActive)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<string> GenerateOrderCodeAsync(CancellationToken cancellationToken)
    {
        var maxAttempts = 10;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var code = GenerateRandomCode();
            var exists = await _dbContext.Orders.AnyAsync(x => x.Code == code, cancellationToken);
            if (!exists)
                return code;
        }

        throw new InvalidOperationException("Could not generate a unique order code after multiple attempts.");
    }

    private static string GenerateRandomCode()
    {
        var chars = new char[CodeLength];
        var data = RandomNumberGenerator.GetBytes(CodeLength);
        for (var i = 0; i < CodeLength; i++)
        {
            chars[i] = CodeChars[data[i] % CodeChars.Length];
        }
        return new string(chars);
    }
}
