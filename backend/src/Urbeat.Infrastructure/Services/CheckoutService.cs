using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Urbeat.Infrastructure.Helpers;

namespace Urbeat.Infrastructure.Services;

public sealed class CheckoutService : ICheckoutService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEfUnitOfWork _efUnitOfWork;
    private readonly INotificationService _notificationService;
    private readonly UserManager<IdentityUser<Guid>> _userManager;
    private readonly IPricingService _pricingService;

    public CheckoutService(
        ApplicationDbContext dbContext,
        IEfUnitOfWork efUnitOfWork,
        INotificationService notificationService,
        UserManager<IdentityUser<Guid>> userManager,
        IPricingService pricingService)
    {
        _dbContext = dbContext;
        _efUnitOfWork = efUnitOfWork;
        _notificationService = notificationService;
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
            if (store.FreeShippingToday)
            {
                freeShippingApplied = true;
                deliveryFee = 0m;
            }
            else if (freeShippingThreshold is > 0m && subtotal >= freeShippingThreshold.Value)
            {
                freeShippingApplied = true;
                deliveryFee = 0m;
            }
            else if (address is not null)
            {
                var normalized = NeighborhoodNormalizer.Normalize(address.Neighborhood);
                var area = store.DeliveryAreas.FirstOrDefault(a => NeighborhoodNormalizer.Normalize(a.Neighborhood) == normalized);
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

        var order = new Order
        {
            CustomerUserId = customerUserId!.Value,
            StoreId = store.Id,
            FulfillmentType = request.FulfillmentType,
            Notes = request.Notes?.Trim(),
            PaymentMethod = request.PaymentMethod ?? Domain.Entities.PaymentMethod.CashOnDelivery,
            Status = (request.PaymentMethod ?? Domain.Entities.PaymentMethod.CashOnDelivery) is PaymentMethod.CashOnDelivery or PaymentMethod.CardOnDelivery
                ? OrderStatus.Received
                : OrderStatus.PendingPayment,
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
        }

        order.Code = await GenerateOrderCodeAsync(cancellationToken);

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
            AdditionalNames = x.Pricing.ExtraNames.Count > 0 ? string.Join(", ", x.Pricing.ExtraNames) : null
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
            PaymentMethod.PixOnline => "Pix online",
            PaymentMethod.CardOnline => "cartão online",
            _ => "pagamento"
        };

        var fulfillmentLabel = isDelivery ? "entrega" : "retirada";

        var orderMessage = order.PaymentMethod is PaymentMethod.CashOnDelivery or PaymentMethod.CardOnDelivery
            ? $"Novo pedido {order.Code} recebido para {fulfillmentLabel} com pagamento via {paymentLabel}."
            : $"Novo pedido {order.Code} para {fulfillmentLabel} com pagamento via {paymentLabel}. Aguardando confirmação.";

        await _notificationService.NotifySellerNewOrderAsync(
            store.OwnerUserId,
            order.Id,
            orderMessage,
            cancellationToken);

        if (order.PaymentMethod is PaymentMethod.CashOnDelivery or PaymentMethod.CardOnDelivery)
        {
            await _notificationService.NotifyCustomerOrderStatusChangedAsync(
                customerUserId.Value,
                order.Id,
                OrderStatus.Received,
                $"Seu pedido {order.Code} foi recebido pela loja.",
                cancellationToken);
        }

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
        return $"HAP-{new string(chars)}";
    }
}
