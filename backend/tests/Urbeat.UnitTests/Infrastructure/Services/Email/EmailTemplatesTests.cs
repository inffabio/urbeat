using FluentAssertions;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Services.Email;

namespace Urbeat.UnitTests.Infrastructure.Services.Email;

public sealed class EmailTemplatesTests
{
    [Fact]
    public void BuildRolePromotionConfirmation_ShouldUseTheApprovedSellerSubjectAndCopy()
    {
        var (subject, html) = EmailTemplates.BuildRolePromotionConfirmation(
            UserRoleType.Seller,
            "https://app.urbeat.test/confirmacao-email#promotionToken=abc",
            expiresMinutes: 30);

        subject.Should().Be("Confirme seu novo acesso de lojista no urbeat");
        html.Should().Contain("acréscimo de um acesso de lojista");
        html.Should().Contain("Confirmar acesso de lojista");
        html.Should().Contain("não altera seu acesso de cliente");
        html.Should().Contain("não altera sua senha atual");
        html.Should().Contain("não solicita sua senha por e-mail");
        html.Should().Contain("use o urbeat também como lojista");
        html.Should().Contain("30 minutos");
        html.Should().Contain("apenas uma vez");
        html.Should().Contain("Se você não solicitou esse acesso, ignore esta mensagem");
        html.Should().Contain("o urbeat nunca solicita sua senha por e-mail");
        html.Should().Contain("Equipe urbeat");
        html.Should().Contain("#promotionToken=abc");
        html.Should().NotContain("?promotionToken=");
    }

    [Fact]
    public void BuildRolePromotionConfirmation_ShouldUseTheApprovedCustomerCopy()
    {
        var (subject, html) = EmailTemplates.BuildRolePromotionConfirmation(
            UserRoleType.Customer,
            "https://app.urbeat.test/confirmacao-email#promotionToken=abc",
            expiresMinutes: 30);

        subject.Should().Be("Confirme seu novo acesso de cliente no urbeat");
        html.Should().Contain("acréscimo de um acesso de cliente");
        html.Should().Contain("Confirmar acesso de cliente");
        html.Should().Contain("não altera seu acesso de lojista");
        html.Should().Contain("use o urbeat também como cliente");
    }

    [Fact]
    public void BuildRolePromotionConfirmation_ShouldEscapeTheConfirmationUrl()
    {
        var (_, html) = EmailTemplates.BuildRolePromotionConfirmation(
            UserRoleType.Seller,
            "https://app.urbeat.test/confirmacao-email#promotionToken=a&b=<script>",
            expiresMinutes: 30);

        html.Should().NotContain("<script>");
        html.Should().Contain("&amp;b=");
    }
}
