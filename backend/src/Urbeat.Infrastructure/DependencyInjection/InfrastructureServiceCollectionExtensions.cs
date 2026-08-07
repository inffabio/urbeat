using System.Text;
using Hangfire;
using Hangfire.InMemory;
using Hangfire.PostgreSql;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using Urbeat.Domain.Repositories;
using Urbeat.Infrastructure.Data;
using Urbeat.Infrastructure.Identity;
using Urbeat.Infrastructure.Jobs;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.ReadRepositories;
using Urbeat.Infrastructure.Persistence.Repositories;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Urbeat.Infrastructure.Security;
using Urbeat.Infrastructure.Services.Email;
using Urbeat.Infrastructure.Services.Payments;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Polly;
using Polly.Extensions.Http;
using System.Net;
using System.Net.Http;

namespace Urbeat.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddScoped<IOrderReportService, OrderReportService>();
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<AdminSeedOptions>(configuration.GetSection(AdminSeedOptions.SectionName));
        services.Configure<MercadoPagoOptions>(configuration.GetSection(MercadoPagoOptions.SectionName));
        services.Configure<AsaasWebhookOptions>(configuration.GetSection(AsaasWebhookOptions.SectionName));
        services.Configure<AsaasSubscriptionOptions>(configuration.GetSection(AsaasSubscriptionOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<EmailConfirmationOptions>(configuration.GetSection(EmailConfirmationOptions.SectionName));
        services.Configure<CustomerVerificationOptions>(configuration.GetSection(CustomerVerificationOptions.SectionName));

        services.AddDbContextPool<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"));
        });

        services
            .AddIdentityCore<IdentityUser<Guid>>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 8;
                options.User.RequireUniqueEmail = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.SignIn.RequireConfirmedEmail = false;
                options.SignIn.RequireConfirmedAccount = false;
                options.Tokens.EmailConfirmationTokenProvider = TokenOptions.DefaultProvider;
            })
            .AddErrorDescriber<PortugueseIdentityErrorDescriber>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret));

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidAudience = jwtOptions.Audience,
                    IssuerSigningKey = signingKey,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthorizationPolicies.AdminOnly, policy => policy.RequireRole("Admin"));
            options.AddPolicy(AuthorizationPolicies.SellerOnly, policy => policy.RequireRole("Seller"));
            options.AddPolicy(AuthorizationPolicies.CustomerOnly, policy => policy.RequireRole("Customer"));
        });
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICuisineTypeService, CuisineTypeService>();
        services.AddScoped<IPlanService, PlanService>();
        services.AddScoped<IStoreService, StoreService>();
        services.AddScoped<IStoreReadRepository, StoreReadRepository>();
        services.AddScoped<IStoreAddressService, StoreAddressService>();
        services.AddScoped<IStoreBusinessHoursService, StoreBusinessHoursService>();
        services.AddScoped<Urbeat.Application.Interfaces.Publish.IStorePublishService, Urbeat.Application.Services.Publish.StorePublishService>();
        services.AddScoped<ICustomerAddressService, CustomerAddressService>();
        services.AddScoped<INotificationService>(provider =>
        {
            var dbContext = provider.GetRequiredService<ApplicationDbContext>();
            var sellerHubType = Type.GetType("Urbeat.WebApi.Hubs.SellerNotificationHub, Urbeat.WebApi");
            var customerHubType = Type.GetType("Urbeat.WebApi.Hubs.CustomerNotificationHub, Urbeat.WebApi");
            object? sellerHub = null;
            object? customerHub = null;
            if (sellerHubType != null)
            {
                var sellerHubContextType = typeof(Microsoft.AspNetCore.SignalR.IHubContext<>).MakeGenericType(sellerHubType);
                sellerHub = provider.GetService(sellerHubContextType);
            }
            if (customerHubType != null)
            {
                var customerHubContextType = typeof(Microsoft.AspNetCore.SignalR.IHubContext<>).MakeGenericType(customerHubType);
                customerHub = provider.GetService(customerHubContextType);
            }
            return (INotificationService)Activator.CreateInstance(typeof(NotificationService), dbContext, sellerHub, customerHub)!;
        });
        services.AddScoped<ISellerSubscriptionStatusService, SellerSubscriptionStatusService>();
        services.AddScoped<ISubscriptionNotificationService, SubscriptionNotificationService>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<ICustomerOtpService, CustomerOtpService>();
        services.AddScoped<FakeSmsVerificationMessageSender>();
        services.AddScoped<FakeWhatsAppVerificationMessageSender>();
        services.AddHttpClient<InfobipSmsVerificationMessageSender>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddScoped<ICustomerVerificationMessageSender>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<CustomerVerificationOptions>>().Value;
            if (options.Channel == CustomerVerificationChannel.WhatsApp)
            {
                return provider.GetRequiredService<FakeWhatsAppVerificationMessageSender>();
            }

            return options.SmsProvider.Equals("Infobip", StringComparison.OrdinalIgnoreCase)
                ? provider.GetRequiredService<InfobipSmsVerificationMessageSender>()
                : provider.GetRequiredService<FakeSmsVerificationMessageSender>();
        });
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentWebhookService, PaymentWebhookService>();
        services.AddScoped<ISubscriptionWebhookService, SubscriptionWebhookService>();
        services.AddScoped<IOrderPaymentStrategyFactory, OrderPaymentStrategyFactory>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<IOrderPaymentStrategy, MercadoPagoOrderPaymentStrategy>();
        services.AddHttpClient<IMercadoPagoCheckoutAdapter, MercadoPagoCheckoutAdapter>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        })
            .AddPolicyHandler(GetMercadoPagoRetryPolicy())
            .AddPolicyHandler(GetMercadoPagoCircuitBreakerPolicy());
        services.AddHttpClient<IViaCepService, ViaCepService>(client =>
        {
            client.BaseAddress = new Uri("https://viacep.com.br");
            client.Timeout = TimeSpan.FromSeconds(5);
        })
            .AddPolicyHandler(GetViaCepRetryPolicy())
            .AddPolicyHandler(GetViaCepCircuitBreakerPolicy());
        services.AddHttpClient<IOsmService, OsmService>(client =>
        {
            client.BaseAddress = new Uri("https://viacep.com.br");
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Urbeat/1.0 contato@urbeat.com.br");
        });

        services.AddHttpClient("Nominatim", client =>
        {
            client.BaseAddress = new Uri("https://nominatim.openstreetmap.org");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Urbeat/1.0 contato@urbeat.com.br");
        });
        services.AddHttpClient<IAsaasSubscriptionAdapter, AsaasSubscriptionAdapter>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddScoped<IEmailService, SmtpEmailService>();
        services.AddScoped<IEmailConfirmationService, EmailConfirmationService>();
        services.AddSingleton<ISystemParameterService, SystemParameterService>();
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.AddSingleton<IShortIdCache, RedisShortIdCache>();
        services.AddSingleton<IEmailTokenCache, RedisEmailTokenCache>();
        services.AddScoped<IShortIdService, ShortIdService>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IEfUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IDapperUnitOfWork, DapperUnitOfWork>();
        services.AddScoped<IProductCategoryService, ProductCategoryService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IStoreAdditionalService, StoreAdditionalService>();
        services.AddScoped<IProductReadRepository, ProductReadRepository>();
        services.AddScoped<ILandingPageContentService, LandingPageContentService>();
        services.AddScoped<IPrinterConfigService, PrinterConfigService>();
        
        services.Configure<CloudinaryOptions>(configuration.GetSection(CloudinaryOptions.SectionName));
        services.AddScoped<IImageUploadService, CloudinaryImageUploadService>();
        services.AddScoped<IPricingService, PricingService>();
        
        services.AddScoped<IStorePaymentGatewayConfigService, StorePaymentGatewayConfigService>();
        services.AddScoped<IEncryptionService, EncryptionService>();
        services.Configure<EncryptionOptions>(configuration.GetSection(EncryptionOptions.SectionName));
        services.AddScoped<AdminUserSeeder>();
        services.AddScoped<CuisineTypeSeeder>();
        services.AddScoped<DeliveryTimeSeeder>();
        services.AddScoped<DemoDataSeeder>();
        services.AddScoped<SystemParameterSeeder>();
        services.AddScoped<LandingPageSeeder>();

        return services;
    }

    public static IServiceCollection AddInfrastructureJobs(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddScoped<OperationalHeartbeatJob>();
        services.AddScoped<SellerSubscriptionNotificationJob>();
        services.AddScoped<SendEmailConfirmationJob>();
        services.AddScoped<ImportAllCitiesNeighborhoodsJob>();
        services.AddScoped<ImportStoreNeighborhoodsJob>();
        services.AddScoped<ImportUfNeighborhoodsJob>();
        services.AddScoped<GooglePlacesTextSearchImporter>();

        services.AddHangfire(config =>
        {
            if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
            {
                config.UseInMemoryStorage();
                return;
            }

            var defaultConnection = configuration.GetConnectionString("DefaultConnection");
            config.UsePostgreSqlStorage(options =>
            {
                options.UseNpgsqlConnection(defaultConnection);
            });
        });

        services.AddHangfireServer(options =>
        {
            options.WorkerCount = 2;
        });

        return services;
    }

    private static IAsyncPolicy<HttpResponseMessage> GetMercadoPagoRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(message => message.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromMilliseconds(200 * retryAttempt));
    }

    private static IAsyncPolicy<HttpResponseMessage> GetMercadoPagoCircuitBreakerPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(message => message.StatusCode == HttpStatusCode.TooManyRequests)
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30));
    }

    private static IAsyncPolicy<HttpResponseMessage> GetViaCepRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(message => message.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(2, retryAttempt => TimeSpan.FromMilliseconds(150 * retryAttempt));
    }

    private static IAsyncPolicy<HttpResponseMessage> GetViaCepCircuitBreakerPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(message => message.StatusCode == HttpStatusCode.TooManyRequests)
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 3,
                durationOfBreak: TimeSpan.FromSeconds(20));
    }
}
