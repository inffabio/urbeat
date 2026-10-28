using FluentValidation;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Services;

namespace Urbeat.Application.Validators;

public sealed class InviteDeliveryDriverRequestDtoValidator : AbstractValidator<InviteDeliveryDriverRequestDto>
{
    public InviteDeliveryDriverRequestDtoValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty()
            .MinimumLength(3)
            .MaximumLength(120);

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(255);

        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(20)
            .Matches(@"^[0-9+()\-\s]+$")
            .WithMessage("Informe um telefone válido.");
    }
}

public sealed class AcceptDeliveryDriverInviteRequestDtoValidator : AbstractValidator<AcceptDeliveryDriverInviteRequestDto>
{
    public AcceptDeliveryDriverInviteRequestDtoValidator()
    {
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(100);

        RuleFor(x => x.FullName)
            .MinimumLength(3)
            .MaximumLength(120)
            .When(x => !string.IsNullOrWhiteSpace(x.FullName));
    }
}

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
