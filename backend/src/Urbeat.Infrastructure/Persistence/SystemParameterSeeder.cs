using Urbeat.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Persistence;

public sealed class SystemParameterSeeder
{
    private readonly ApplicationDbContext _dbContext;

    public SystemParameterSeeder(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await _dbContext.SystemParameters.AnyAsync(cancellationToken))
            return;

        var parameters = new List<SystemParameter>
        {
            // ── Security ──────────────────────────────────────────────
            new() { Key = "Password.RequireDigit", Value = "true", Type = SystemParameterType.Boolean, Group = "Security", Description = "Exige dígito na senha" },
            new() { Key = "Password.RequireLowercase", Value = "true", Type = SystemParameterType.Boolean, Group = "Security", Description = "Exige minúscula na senha" },
            new() { Key = "Password.RequireUppercase", Value = "true", Type = SystemParameterType.Boolean, Group = "Security", Description = "Exige maiúscula na senha" },
            new() { Key = "Password.RequireNonAlphanumeric", Value = "false", Type = SystemParameterType.Boolean, Group = "Security", Description = "Exige caractere especial" },
            new() { Key = "Password.RequiredLength", Value = "8", Type = SystemParameterType.Int32, Group = "Security", Description = "Comprimento mínimo da senha" },
            new() { Key = "Lockout.MaxFailedAccessAttempts", Value = "5", Type = SystemParameterType.Int32, Group = "Security", Description = "Tentativas máximas antes de bloquear" },
            new() { Key = "Lockout.DefaultLockoutMinutes", Value = "15", Type = SystemParameterType.Int32, Group = "Security", Description = "Duração do bloqueio em minutos" },
            new() { Key = "Jwt.ExpirationMinutes", Value = "15", Type = SystemParameterType.Int32, Group = "Security", Description = "Expiração do token JWT em minutos" },
            new() { Key = "Jwt.RefreshTokenDays", Value = "7", Type = SystemParameterType.Int32, Group = "Security", Description = "Dias de validade do refresh token" },
            new() { Key = "Jwt.ClockSkewMinutes", Value = "1", Type = SystemParameterType.Int32, Group = "Security", Description = "Margem de tolerância do JWT" },

            // ── Business Rules ────────────────────────────────────────
            new() { Key = "Customer.MaxAddresses", Value = "3", Type = SystemParameterType.Int32, Group = "Business", Description = "Máximo de endereços por cliente" },
            new() { Key = "Order.DefaultPageSize", Value = "20", Type = SystemParameterType.Int32, Group = "Business", Description = "Tamanho padrão de página em listagens" },
            new() { Key = "Order.MaxPageSize", Value = "100", Type = SystemParameterType.Int32, Group = "Business", Description = "Tamanho máximo de página" },
            new() { Key = "Order.CodePrefix", Value = "HAP-", Type = SystemParameterType.String, Group = "Business", Description = "Prefixo do código do pedido" },
            new() { Key = "Order.CodeLength", Value = "8", Type = SystemParameterType.Int32, Group = "Business", Description = "Comprimento do código do pedido" },
            new() { Key = "Order.CodeChars", Value = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", Type = SystemParameterType.String, Group = "Business", Description = "Caracteres permitidos no código" },
            new() { Key = "Order.CodeMaxAttempts", Value = "10", Type = SystemParameterType.Int32, Group = "Business", Description = "Tentativas máximas de geração de código único" },
            new() { Key = "Notification.MaxReturned", Value = "50", Type = SystemParameterType.Int32, Group = "Business", Description = "Máximo de notificações retornadas" },
            new() { Key = "Store.DefaultDeliveryFee", Value = "0", Type = SystemParameterType.Decimal, Group = "Business", Description = "Taxa de entrega padrão" },
            new() { Key = "Store.DefaultMinimumOrderValue", Value = "0", Type = SystemParameterType.Decimal, Group = "Business", Description = "Valor mínimo do pedido padrão" },
            new() { Key = "Subscription.DueSoonDays", Value = "3", Type = SystemParameterType.Int32, Group = "Business", Description = "Dias antes do vencimento para notificar" },
            new() { Key = "Subscription.DefaultBillingCycleDays", Value = "30", Type = SystemParameterType.Int32, Group = "Business", Description = "Ciclo de cobrança padrão em dias" },
            new() { Key = "Subscription.BillingType", Value = "UNDEFINED", Type = SystemParameterType.String, Group = "Business", Description = "Tipo de cobrança no Asaas" },
            new() { Key = "Subscription.Cycle", Value = "MONTHLY", Type = SystemParameterType.String, Group = "Business", Description = "Ciclo de cobrança no Asaas" },
            new() { Key = "Subscription.Description", Value = "Assinatura Urbeat", Type = SystemParameterType.String, Group = "Business", Description = "Descrição da assinatura no Asaas" },

            // ── URLs / Endpoints ─────────────────────────────────────
            new() { Key = "Url.FrontendBaseUrl", Value = "http://localhost:4200", Type = SystemParameterType.String, Group = "Url", Description = "URL base do frontend" },
            new() { Key = "Url.EmailConfirmPath", Value = "/confirm-email", Type = SystemParameterType.String, Group = "Url", Description = "Caminho de confirmação de e-mail" },
            new() { Key = "Url.ViaCep", Value = "https://viacep.com.br", Type = SystemParameterType.String, Group = "Url", Description = "Base URL da API ViaCep" },
            new() { Key = "Url.ViaCepPath", Value = "/ws/{0}/json/", Type = SystemParameterType.String, Group = "Url", Description = "Path da consulta ViaCep" },
            new() { Key = "Url.MercadoPago", Value = "https://api.mercadopago.com", Type = SystemParameterType.String, Group = "Url", Description = "Base URL da API Mercado Pago" },
            new() { Key = "Url.MercadoPagoCheckoutPath", Value = "/checkout/preferences", Type = SystemParameterType.String, Group = "Url", Description = "Path do checkout MP" },
            new() { Key = "Url.MercadoPagoPaymentPath", Value = "/v1/payments/{0}", Type = SystemParameterType.String, Group = "Url", Description = "Path de consulta de pagamento MP" },
            new() { Key = "Url.Asaas", Value = "https://api.asaas.com", Type = SystemParameterType.String, Group = "Url", Description = "Base URL da API Asaas" },
            new() { Key = "Url.AsaasCustomersPath", Value = "/v3/customers", Type = SystemParameterType.String, Group = "Url", Description = "Path de clientes Asaas" },
            new() { Key = "Url.AsaasSubscriptionsPath", Value = "/v3/subscriptions", Type = SystemParameterType.String, Group = "Url", Description = "Path de assinaturas Asaas" },
            new() { Key = "Url.HangfireDashboard", Value = "/hangfire", Type = SystemParameterType.String, Group = "Url", Description = "Path do dashboard Hangfire" },
            new() { Key = "Url.SellerHub", Value = "/hubs/seller-notifications", Type = SystemParameterType.String, Group = "Url", Description = "Path do hub SignalR do vendedor" },
            new() { Key = "Url.CustomerHub", Value = "/hubs/customer-notifications", Type = SystemParameterType.String, Group = "Url", Description = "Path do hub SignalR do cliente" },

            // ── Email ─────────────────────────────────────────────────
            new() { Key = "Email.FromAddress", Value = "nao-responda@urbeat.com.br", Type = SystemParameterType.String, Group = "Email", Description = "Remetente padrão de e-mails" },
            new() { Key = "Email.FromName", Value = "Urbeat", Type = SystemParameterType.String, Group = "Email", Description = "Nome do remetente" },
            new() { Key = "Email.SmtpPort", Value = "587", Type = SystemParameterType.Int32, Group = "Email", Description = "Porta SMTP" },
            new() { Key = "Email.UseStartTls", Value = "true", Type = SystemParameterType.Boolean, Group = "Email", Description = "Habilitar STARTTLS" },
            new() { Key = "Email.LogOnly", Value = "false", Type = SystemParameterType.Boolean, Group = "Email", Description = "Apenas log (sem envio real)" },
            new() { Key = "Email.ConfirmationCustomerSubject", Value = "Confirme seu e-mail no Urbeat", Type = SystemParameterType.String, Group = "Email", Description = "Assunto do e-mail de confirmação (cliente)" },
            new() { Key = "Email.ConfirmationCustomerTitle", Value = "Bem-vindo(a) ao Urbeat!", Type = SystemParameterType.String, Group = "Email", Description = "Título do e-mail de confirmação (cliente)" },
            new() { Key = "Email.ConfirmationCustomerGreeting", Value = "Olá!", Type = SystemParameterType.String, Group = "Email", Description = "Saudação do e-mail de confirmação (cliente)" },
            new() { Key = "Email.ConfirmationCustomerButton", Value = "Confirmar meu e-mail", Type = SystemParameterType.String, Group = "Email", Description = "Texto do botão de confirmação (cliente)" },
            new() { Key = "Email.ConfirmationSellerSubject", Value = "Confirme seu e-mail no Urbeat — Loja", Type = SystemParameterType.String, Group = "Email", Description = "Assunto do e-mail de confirmação (vendedor)" },
            new() { Key = "Email.ConfirmationSellerTitle", Value = "Sua loja está quase pronta!", Type = SystemParameterType.String, Group = "Email", Description = "Título do e-mail de confirmação (vendedor)" },
            new() { Key = "Email.ConfirmationSellerGreeting", Value = "Olá, vendedor(a)!", Type = SystemParameterType.String, Group = "Email", Description = "Saudação do e-mail de confirmação (vendedor)" },
            new() { Key = "Email.ConfirmationSellerButton", Value = "Confirmar e-mail da loja", Type = SystemParameterType.String, Group = "Email", Description = "Texto do botão de confirmação (vendedor)" },
            new() { Key = "Email.ConfirmationLinkExpiry", Value = "O link expira em 24 horas.", Type = SystemParameterType.String, Group = "Email", Description = "Aviso de expiração do link" },

            // ── HTTP / Timeouts ───────────────────────────────────────
            new() { Key = "Http.MercadoPagoTimeoutSeconds", Value = "10", Type = SystemParameterType.Int32, Group = "Http", Description = "Timeout HTTP para API do Mercado Pago" },
            new() { Key = "Http.ViaCepTimeoutSeconds", Value = "5", Type = SystemParameterType.Int32, Group = "Http", Description = "Timeout HTTP para API ViaCep" },
            new() { Key = "Http.AsaasTimeoutSeconds", Value = "10", Type = SystemParameterType.Int32, Group = "Http", Description = "Timeout HTTP para API Asaas" },
            new() { Key = "Http.MercadoPagoRetryCount", Value = "3", Type = SystemParameterType.Int32, Group = "Http", Description = "Tentativas de retry MP" },
            new() { Key = "Http.MercadoPagoRetryDelayMs", Value = "200", Type = SystemParameterType.Int32, Group = "Http", Description = "Delay inicial do retry MP (ms)" },
            new() { Key = "Http.MercadoPagoCircuitBreakerThreshold", Value = "5", Type = SystemParameterType.Int32, Group = "Http", Description = "Limite do circuit breaker MP" },
            new() { Key = "Http.MercadoPagoCircuitBreakerSeconds", Value = "30", Type = SystemParameterType.Int32, Group = "Http", Description = "Duração do circuit breaker MP (s)" },
            new() { Key = "Http.ViaCepRetryCount", Value = "2", Type = SystemParameterType.Int32, Group = "Http", Description = "Tentativas de retry ViaCep" },
            new() { Key = "Http.ViaCepRetryDelayMs", Value = "150", Type = SystemParameterType.Int32, Group = "Http", Description = "Delay inicial do retry ViaCep (ms)" },
            new() { Key = "Http.ViaCepCircuitBreakerThreshold", Value = "3", Type = SystemParameterType.Int32, Group = "Http", Description = "Limite do circuit breaker ViaCep" },
            new() { Key = "Http.ViaCepCircuitBreakerSeconds", Value = "20", Type = SystemParameterType.Int32, Group = "Http", Description = "Duração do circuit breaker ViaCep (s)" },

            // ── Upload ────────────────────────────────────────────────
            new() { Key = "Upload.AllowedExtensions", Value = ".jpg,.jpeg,.png,.webp", Type = SystemParameterType.Json, Group = "Upload", Description = "Extensões de imagem permitidas" },
            new() { Key = "Upload.MaxFileSizeBytes", Value = "5242880", Type = SystemParameterType.Int32, Group = "Upload", Description = "Tamanho máximo de upload (5 MB)" },
            new() { Key = "Upload.UrlPathPattern", Value = "/uploads/{0}/{1}", Type = SystemParameterType.String, Group = "Upload", Description = "Padrão de URL de upload" },

            // ── Payment ───────────────────────────────────────────────
            new() { Key = "Payment.Currency", Value = "BRL", Type = SystemParameterType.String, Group = "Payment", Description = "Moeda padrão" },
            new() { Key = "Payment.MercadoPagoAutoReturn", Value = "approved", Type = SystemParameterType.String, Group = "Payment", Description = "Auto-return do MP" },
            new() { Key = "Payment.FallbackCustomerEmail", Value = "cliente@urbeat.local", Type = SystemParameterType.String, Group = "Payment", Description = "E-mail fallback do cliente" },

            // ── Cookie ────────────────────────────────────────────────
            new() { Key = "Cookie.RefreshTokenName", Value = "urbeat.refresh_token", Type = SystemParameterType.String, Group = "Cookie", Description = "Nome do cookie de refresh token" },
            new() { Key = "Cookie.Secure", Value = "true", Type = SystemParameterType.Boolean, Group = "Cookie", Description = "Cookie com flag Secure" },
            new() { Key = "Cookie.SameSiteMode", Value = "Strict", Type = SystemParameterType.String, Group = "Cookie", Description = "Modo SameSite do cookie" },
            new() { Key = "Cookie.Path", Value = "/", Type = SystemParameterType.String, Group = "Cookie", Description = "Path do cookie" },

            // ── Hangfire ──────────────────────────────────────────────
            new() { Key = "Hangfire.WorkerCount", Value = "1", Type = SystemParameterType.Int32, Group = "Hangfire", Description = "Workers do Hangfire" },
            new() { Key = "Hangfire.HeartbeatCron", Value = "0 * * * *", Type = SystemParameterType.String, Group = "Hangfire", Description = "Cron do heartbeat" },
            new() { Key = "Hangfire.SubscriptionNotificationCron", Value = "0 0 * * *", Type = SystemParameterType.String, Group = "Hangfire", Description = "Cron da notificação de assinatura" },

            // ── CORS ──────────────────────────────────────────────────
            new() { Key = "Cors.AllowAnyOrigin", Value = "true", Type = SystemParameterType.Boolean, Group = "Cors", Description = "Permite qualquer origem no CORS" },
        };

        await _dbContext.SystemParameters.AddRangeAsync(parameters, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
