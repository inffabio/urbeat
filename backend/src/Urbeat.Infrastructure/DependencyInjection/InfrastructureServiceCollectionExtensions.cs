using System.Text;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Application.Security;
using Urbeat.Application.WhatsApp;
using Urbeat.Domain.Repositories;
using Urbeat.Infrastructure.Data;
using Urbeat.Infrastructure.Hangfire;
using Urbeat.Infrastructure.Identity;
using Urbeat.Infrastructure.Jobs;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.ReadRepositories;
using Urbeat.Infrastructure.Persistence.Repositories;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Outbox;
using Urbeat.Infrastructure.Outbox.Handlers;
using Urbeat.Infrastructure.Services;
using Urbeat.Infrastructure.Security;
using Urbeat.Infrastructure.Services.Email;
using Urbeat.Infrastructure.Services.Payments;
using Urbeat.Infrastructure.Services.WhatsApp;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
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
        IHostEnvironment environment)
    {
        services.AddScoped<IOrderReportService, OrderReportService>();
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<AdminSeedOptions>(configuration.GetSection(AdminSeedOptions.SectionName));
        services.Configure<MercadoPagoOptions>(configuration.GetSection(MercadoPagoOptions.SectionName));
        services.AddOptions<MockPixOptions>()
            .Bind(configuration.GetSection(MockPixOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MockPixOptions>, MockPixOptionsValidator>();
        services.Configure<AsaasWebhookOptions>(configuration.GetSection(AsaasWebhookOptions.SectionName));
        services.Configure<AsaasSubscriptionOptions>(configuration.GetSection(AsaasSubscriptionOptions.SectionName));
        services.Configure<WhatsAppOptions>(configuration.GetSection(WhatsAppOptions.SectionName));
        services.AddOptions<WhatsAppOptions>().ValidateOnStart();
        services.AddSingleton<IValidateOptions<WhatsAppOptions>, WhatsAppOptionsValidator>();
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<EmailConfirmationOptions>(configuration.GetSection(EmailConfirmationOptions.SectionName));
        services.Configure<DeliveryRouteOptions>(configuration.GetSection(DeliveryRouteOptions.SectionName));
        services.Configure<CustomerVerificationOptions>(configuration.GetSection(CustomerVerificationOptions.SectionName));
        services.Configure<EmailChangeChallengeOptions>(configuration.GetSection(EmailChangeChallengeOptions.SectionName));
        // Argon2id parameters are operational (non-secret). There is intentionally no pepper and no
        // Vault/secret entry: the random salt travels inside the hash. ValidateOnStart rejects a
        // cost that could exhaust memory/CPU on login or registration.
        services.AddOptions<Argon2idPasswordHasherOptions>()
            .Bind(configuration.GetSection(Argon2idPasswordHasherOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<Argon2idPasswordHasherOptions>, Argon2idPasswordHasherOptionsValidator>();
        // Role promotion has its own dedicated signing key. ValidateOnStart makes a deployment
        // without a valid (non-placeholder, high-entropy) value fail at startup instead of signing
        // links with a fallback secret.
        services.AddOptions<RolePromotionChallengeOptions>()
            .Bind(configuration.GetSection(RolePromotionChallengeOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RolePromotionChallengeOptions>, RolePromotionChallengeOptionsValidator>();

        // Resolve the connection string once with explicit Npgsql pool limits. EF Core and Hangfire
        // share the same normalized value, so neither can open an unbounded pool per process.
        var databaseConnectionString = NpgsqlPoolConfiguration.ResolveConnectionString(configuration);

        services.AddDbContextPool<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(databaseConnectionString);
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

        // User credentials use Argon2id while legacy ASP.NET Identity PBKDF2 hashes remain valid and
        // report SuccessRehashNeeded. Tokens and delivery PINs deliberately do not go through it.
        services.Replace(ServiceDescriptor.Scoped<IPasswordHasher<IdentityUser<Guid>>, Argon2idPasswordHasher>());
        services.AddSingleton<ITokenHasher, TokenHasher>();

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
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // Browser SignalR clients cannot set an Authorization header on the
                        // WebSocket handshake and send the token on the query string instead. The
                        // fallback is scoped to the hub path inside the resolver.
                        var signalRToken = SignalRAccessTokenResolver.Resolve(context.Request);
                        if (signalRToken is not null)
                        {
                            context.Token = signalRToken;
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthorizationPolicies.AdminOnly, policy => policy.RequireRole("Admin"));
            options.AddPolicy(AuthorizationPolicies.SellerOnly, policy => policy.RequireRole("Seller"));
            options.AddPolicy(AuthorizationPolicies.CustomerOnly, policy => policy.RequireRole("Customer"));
        });
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IEmailChangeChallengeService, EmailChangeChallengeService>();
        services.AddScoped<IRolePromotionChallengeService, RolePromotionChallengeService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICuisineTypeService, CuisineTypeService>();
        services.AddSingleton<PublicPlanOfferCache>();
        services.AddScoped<PlanService>();
        services.AddScoped<IPlanService>(provider => new CachedPlanService(
            provider.GetRequiredService<PlanService>(),
            provider.GetRequiredService<PublicPlanOfferCache>(),
            provider.GetRequiredService<IServiceScopeFactory>()));
        services.AddScoped<IStoreService, StoreService>();
        services.AddScoped<IStoreReadRepository, StoreReadRepository>();
        services.AddScoped<IStoreAddressService, StoreAddressService>();
        services.AddScoped<IStoreBusinessHoursService, StoreBusinessHoursService>();
        services.AddScoped<Urbeat.Application.Interfaces.Publish.IStorePublishService, Urbeat.Application.Services.Publish.StorePublishService>();
        services.AddScoped<ICustomerAddressService, CustomerAddressService>();
        services.AddScoped<INotificationService>(provider => new NotificationService(
            provider.GetRequiredService<ApplicationDbContext>(),
            provider.GetService<ILiveNotificationPublisher>()));
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
        services.AddScoped<IDeliveryDriverService, DeliveryDriverService>();
        services.AddScoped<IDeliveryRouteService, DeliveryRouteService>();
        services.AddScoped<IIndividualDeliveryLinkService, IndividualDeliveryLinkService>();
        services.AddScoped<IOrderPrintingService, OrderPrintingService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentWebhookService, PaymentWebhookService>();
        services.AddScoped<ISubscriptionWebhookService, SubscriptionWebhookService>();
        services.AddScoped<IWhatsAppWebhookService, WhatsAppWebhookService>();
        services.AddScoped<IWhatsAppStoreMessageComposer, WhatsAppStoreMessageComposer>();
        services.AddScoped<IWhatsAppBusinessProfileService, WhatsAppBusinessProfileService>();
        services.AddHttpClient<IWhatsAppMessageSender, WhatsAppCloudApiMessageSender>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<WhatsAppOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        })
            .AddPolicyHandler(GetWhatsAppRetryPolicy());
        services.AddHttpClient(WhatsAppHttpClients.Graph, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<WhatsAppOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        })
            // The Resumable Upload session is opened with the access token in the documented
            // `access_token` query parameter, so request logging must not echo the URL.
            .RemoveAllLoggers();
        services.AddHttpClient(WhatsAppHttpClients.ProfileImage, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.MaxResponseContentBufferSize = 6 * 1024 * 1024;
        })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            // The configured logo URL may contain a query string; do not let HttpClient log it.
            .RemoveAllLoggers();
        services.AddScoped<IOrderPaymentStrategyFactory, OrderPaymentStrategyFactory>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddSingleton<IMockPixClock, SystemClock>();
        services.AddSingleton<IMockPixRandom, SystemRandom>();
        services.AddScoped<MockPixPaymentProcessor>();
        services.AddScoped<IOrderPaymentStrategy, MockPixPaymentStrategy>();
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
        var redisConnectionString = configuration[$"{RedisOptions.SectionName}:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            // One multiplexer is shared by application caches, SignalR and health probes. Creating
            // a connection per request/probe exhausts sockets and hides the real Redis status.
            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(redisConnectionString));
        }
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
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddSingleton<IOutboxWorkerLiveness, OutboxWorkerLiveness>();
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.AddScoped<IOutboxDeliveryTracker, OutboxDeliveryTracker>();
        services.AddSingleton<IOutboxHandlerClassifier, OutboxHandlerClassifier>();
        services.AddScoped<IOutboxDispatcher, OutboxDispatcher>();
        services.AddScoped<IOutboxEventHandler, OrderSignalRHandler>();
        services.AddScoped<IOutboxEventHandler, OrderAcceptedForPrintingSignalRHandler>();
        services.AddScoped<IOutboxEventHandler, NotificationHandler>();
        services.AddScoped<IOutboxEventHandler, EmailHandler>();
        services.AddScoped<IOutboxEventHandler, PrintHandler>();

        var outboxOptions = configuration.GetSection(OutboxOptions.SectionName).Get<OutboxOptions>() ?? new OutboxOptions();
        var hangfireRuntime = HangfireRuntime.Resolve(configuration);
        if (outboxOptions.WorkerEnabled && hangfireRuntime.RunsOutbox)
        {
            services.AddHostedService<OutboxWorker>();
        }

        var mockPixOptions = configuration.GetSection(MockPixOptions.SectionName).Get<MockPixOptions>() ?? new MockPixOptions();
        if (mockPixOptions.IsMockEnabled && mockPixOptions.WorkerEnabled)
        {
            services.AddHostedService<MockPixPaymentWorker>();
        }
        
        services.Configure<CloudinaryOptions>(configuration.GetSection(CloudinaryOptions.SectionName));
        services.Configure<CloudinaryMaintenanceOptions>(configuration.GetSection(CloudinaryMaintenanceOptions.SectionName));
        services.AddScoped<IImageUploadService, CloudinaryImageUploadService>();
        services.AddScoped<ICloudinaryImageCatalog, CloudinaryImageCatalog>();
        services.AddScoped<IStoreImageMaintenanceService, StoreImageMaintenanceService>();
        services.AddScoped<IPricingService, PricingService>();
        
        services.AddScoped<IStorePaymentGatewayConfigService, StorePaymentGatewayConfigService>();
        services.AddScoped<IEncryptionService, EncryptionService>();
        services.Configure<EncryptionOptions>(configuration.GetSection(EncryptionOptions.SectionName));
        services.AddScoped<AdminUserSeeder>();
        services.AddScoped<UserRoleCredentialBackfillService>();
        services.AddScoped<CuisineTypeSeeder>();
        services.AddScoped<DeliveryTimeSeeder>();
        services.AddScoped<DemoDataSeeder>();
        services.AddScoped<SystemParameterSeeder>();
        services.AddScoped<LandingPageSeeder>();
        services.AddScoped<BillingPlanSeeder>();
        services.AddScoped<DemoSubscriptionChargeSeeder>();

        return services;
    }

    public static IServiceCollection AddInfrastructureJobs(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddScoped<OperationalHeartbeatJob>();
        services.AddScoped<SellerSubscriptionNotificationJob>();
        services.AddScoped<SendEmailConfirmationJob>();
        services.AddScoped<SendRolePromotionEmailJob>();
        services.AddScoped<RolePromotionEmailRecoveryJob>();
        services.AddScoped<ImportAllCitiesNeighborhoodsJob>();
        services.AddScoped<ImportStoreNeighborhoodsJob>();
        services.AddScoped<ImportUfNeighborhoodsJob>();
        services.AddScoped<GooglePlacesTextSearchImporter>();

        var hangfireRuntime = HangfireRuntime.Resolve(configuration);
        services.AddSingleton(hangfireRuntime);
        services.AddUrbeatHangfireStorage(configuration, environment);
        services.AddUrbeatHangfireServer(hangfireRuntime);

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

    private static IAsyncPolicy<HttpResponseMessage> GetWhatsAppRetryPolicy()
    {
        // Cloud API message sends are not idempotent: only a throttled request (429) is retried
        // because Meta did not accept it. Server/transport errors are surfaced to the caller.
        return Policy<HttpResponseMessage>
            .HandleResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(2, retryAttempt => TimeSpan.FromMilliseconds(250 * retryAttempt));
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
