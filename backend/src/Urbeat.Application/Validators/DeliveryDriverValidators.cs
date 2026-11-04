using FluentValidation;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Services;

namespace Urbeat.Application.Validators;

public sealed class AssignDeliveryDriverRequestDtoValidator : AbstractValidator<AssignDeliveryDriverRequestDto>
{
    public AssignDeliveryDriverRequestDtoValidator()
    {
        RuleFor(x => x.DeliveryDriverId)
            .NotEmpty();
    }
}

public sealed class ConfirmDeliveryPinRequestDtoValidator : AbstractValidator<ConfirmDeliveryPinRequestDto>
{
    public ConfirmDeliveryPinRequestDtoValidator()
    {
        RuleFor(x => x.Pin)
            .NotEmpty()
            .Matches(@"^\d{4}$")
            .WithMessage("O PIN deve ter 4 dígitos.");

        RuleFor(x => x.CashReceivedAmount)
            .Must(value => !value.HasValue || OrderPaymentPolicy.HasCurrencyPrecision(value.Value))
            .WithMessage("O valor recebido deve ter no máximo duas casas decimais.")
            .When(x => x.CashReceivedAmount.HasValue);

        RuleFor(x => x.CashReceivedAmount)
            .GreaterThan(0m)
            .WithMessage("O valor recebido deve ser maior que zero.")
            .When(x => x.CashReceivedAmount.HasValue);

        RuleFor(x => x.CashReceivedAmount)
            .LessThanOrEqualTo(OrderPaymentPolicy.MaxCashAmount)
            .WithMessage("O valor recebido excede o limite permitido.")
            .When(x => x.CashReceivedAmount.HasValue);
    }
}

public sealed class UpsertStoreDeliveryDriverRequestDtoValidator : AbstractValidator<UpsertStoreDeliveryDriverRequestDto>
{
    public UpsertStoreDeliveryDriverRequestDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MinimumLength(3)
            .MaximumLength(120);

        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .WithMessage("Informe o telefone principal do motoboy.")
            .MaximumLength(20)
            .Must(BrazilianMobilePhone.IsValid)
            .WithMessage("Informe um celular com DDD, no formato (##) #####-####.");

        RuleFor(x => x.WhatsAppPhoneNumber)
            .MaximumLength(20)
            .Must(BrazilianMobilePhone.IsValid)
            .WithMessage("Informe um WhatsApp com DDD, no formato (##) #####-####.")
            .When(x => !string.IsNullOrWhiteSpace(x.WhatsAppPhoneNumber));
    }
}

public sealed class CreateDeliveryRouteRequestDtoValidator : AbstractValidator<CreateDeliveryRouteRequestDto>
{
    public CreateDeliveryRouteRequestDtoValidator()
    {
        RuleFor(x => x.DeliveryDriverId).NotEmpty();
        RuleFor(x => x.OrderIds)
            .NotEmpty()
            .WithMessage("Selecione ao menos um pedido para a rota.");
        RuleFor(x => x.OrderIds)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("A rota não pode conter pedidos duplicados.")
            .When(x => x.OrderIds.Count > 0);
    }
}

public sealed class UpdateDeliveryRouteRequestDtoValidator : AbstractValidator<UpdateDeliveryRouteRequestDto>
{
    public UpdateDeliveryRouteRequestDtoValidator()
    {
        RuleFor(x => x.DeliveryDriverId).NotEmpty();
        RuleFor(x => x.OrderIds)
            .NotEmpty()
            .WithMessage("Selecione ao menos um pedido para a rota.");
        RuleFor(x => x.OrderIds)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("A rota não pode conter pedidos duplicados.")
            .When(x => x.OrderIds.Count > 0);
    }
}

public sealed class SendDeliveryRoutesRequestDtoValidator
    : AbstractValidator<SendDeliveryRoutesRequestDto>
{
    public SendDeliveryRoutesRequestDtoValidator()
    {
        RuleFor(x => x.OrderIds)
            .NotEmpty()
            .WithMessage("Selecione ao menos um pedido em entrega para enviar.");
        RuleFor(x => x.OrderIds)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("A seleção não pode conter pedidos duplicados.")
            .When(x => x.OrderIds.Count > 0);
        RuleFor(x => x.PhysicalConfirmation)
            .Equal(true)
            .WithMessage("Confirme fisicamente os pedidos antes de preparar os links de entrega.");
    }
}
