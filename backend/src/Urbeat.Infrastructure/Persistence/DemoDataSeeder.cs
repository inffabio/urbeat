using System.Globalization;
using System.Text;
using Urbeat.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Urbeat.Infrastructure.Persistence;

/// <summary>
/// Seeder de dados de demonstração: 3 lojas (Hamburgueria, Pizzaria, Japonês),
/// com 10 produtos cada distribuídos em categorias variadas.
/// Roda apenas se a tabela Stores estiver vazia.
/// </summary>
public sealed class DemoDataSeeder
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<IdentityUser<Guid>> _userManager;
    private readonly ILogger<DemoDataSeeder> _logger;

    public DemoDataSeeder(
        ApplicationDbContext dbContext,
        UserManager<IdentityUser<Guid>> userManager,
        ILogger<DemoDataSeeder> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        if (await _dbContext.Stores.AnyAsync())
        {
            _logger.LogInformation("DemoDataSeeder: dados de demo já existem — pulando.");
            return;
        }

        _logger.LogInformation("DemoDataSeeder: populando 3 lojas + 30 produtos…");

        var plans = await SeedPlansAsync();
        var stores = GetStoreData();

        for (var i = 0; i < stores.Count; i++)
        {
            var data = stores[i];
            var sellerUser = await CreateUserAsync(data.SellerEmail, "Teste1234", "Seller");
            if (sellerUser is null)
            {
                _logger.LogWarning("DemoDataSeeder: não foi possível criar o seller {Email}", data.SellerEmail);
                continue;
            }

            var plan = plans[i % plans.Count];
            SeedStore(data, sellerUser, plan);
        }

        await SeedCustomersAsync();
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("DemoDataSeeder: concluído.");
    }

    // ────────────────────────────── PLANS ──────────────────────────────

    private async Task<List<Plan>> SeedPlansAsync()
    {
        var plans = new List<Plan>();
        var planDefs = new[]
        {
            ("Plano Básico", 49.90m, "Ideal para pequenos negócios. Taxa por pedido: 8%."),
            ("Plano Premium", 99.90m, "Taxa zero por pedido. Prioridade no atendimento."),
        };

        foreach (var (name, amount, desc) in planDefs)
        {
            var existing = await _dbContext.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Name == name);
            if (existing is not null)
            {
                plans.Add(existing);
                continue;
            }

            var plan = new Plan
            {
                Name = name,
                Amount = amount,
                Description = desc,
                IsActive = true,
            };
            _dbContext.Plans.Add(plan);
            plans.Add(plan);
        }

        return plans;
    }

    // ────────────────────────────── USERS ──────────────────────────────

    private async Task SeedCustomersAsync()
    {
        var customerCreds = new[]
        {
            "joao@cliente.com",
            "maria@cliente.com",
            "carlos@cliente.com",
        };

        foreach (var email in customerCreds)
        {
            await CreateUserAsync(email, "Teste1234", "Customer");
        }
    }

    private async Task<IdentityUser<Guid>?> CreateUserAsync(string email, string password, string role)
    {
        var existing = await _userManager.FindByEmailAsync(email);
        if (existing is not null) return existing;

        var user = new IdentityUser<Guid>
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            LockoutEnabled = false,
        };

        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            _logger.LogWarning("DemoDataSeeder: falha criando usuário {Email}: {Errors}",
                email, string.Join(", ", result.Errors.Select(e => e.Description)));
            return null;
        }

        await _userManager.AddToRoleAsync(user, role);
        return user;
    }

    // ────────────────────────────── STORES ─────────────────────────────

    private void SeedStore(StoreSeedData data, IdentityUser<Guid> sellerUser, Plan plan)
    {
        var path = Slugify(data.Name);

        var store = new Store
        {
            OwnerUserId = sellerUser.Id,
            Name = data.Name,
            Slug = path,
            PhoneNumber = data.Phone,
            Description = data.Description,
            CuisineType = _dbContext.CuisineTypes.Single(x => x.Name == data.CuisineType),
            BannerUrl = data.BannerUrl,
            LogoUrl = data.LogoUrl,
            IsOpen = true,
            IsSubscriptionBlocked = false,
            DeliveryFee = data.DeliveryFee,
            MinimumOrderValue = data.MinOrder,
        };
        _dbContext.Stores.Add(store);

        _dbContext.StoreAddresses.Add(new StoreAddress
        {
            StoreId = store.Id,
            Street = data.Street,
            Number = data.Number,
            Neighborhood = data.Neighborhood,
            City = data.City,
            State = data.State,
            ZipCode = data.ZipCode,
        });

        foreach (var hour in data.Hours)
        {
            _dbContext.StoreBusinessHours.Add(new StoreBusinessHour
            {
                StoreId = store.Id,
                DayOfWeek = hour.DayOfWeek,
                IsOpen = true,
                Shifts =
                {
                    new StoreBusinessHourShift
                    {
                        StartTime = TimeOnly.Parse(hour.OpensAt, CultureInfo.InvariantCulture),
                        EndTime = TimeOnly.Parse(hour.ClosesAt, CultureInfo.InvariantCulture)
                    }
                }
            });
        }

        foreach (var cat in data.Categories)
        {
            var category = new ProductCategory
            {
                StoreId = store.Id,
                Name = cat.Name,
                DisplayOrder = cat.Order,
                IsActive = true,
            };
            _dbContext.ProductCategories.Add(category);

            foreach (var p in cat.Products)
            {
                _dbContext.Products.Add(new Product
                {
                    StoreId = store.Id,
                    CategoryId = category.Id,
                    Name = p.Name,
                    Description = p.Description,
                    Price = p.Price,
                    ImageUrl = p.ImageUrl,
                    IsAvailable = true,
                    DisplayOrder = p.DisplayOrder,
                });
            }
        }

        _dbContext.SellerSubscriptions.Add(new SellerSubscription
        {
            StoreId = store.Id,
            SellerUserId = sellerUser.Id,
            PlanId = plan.Id,
            PlanName = plan.Name,
            PlanAmount = plan.Amount,
            Status = SellerSubscriptionBillingStatus.Active,
            StartDateUtc = DateTime.UtcNow,
            NextBillingDateUtc = DateTime.UtcNow.AddDays(30),
            GatewayCustomerId = $"demo_{sellerUser.Id:N}",
            GatewaySubscriptionId = $"demo_sub_{sellerUser.Id:N}",
        });
    }

    // ────────────────────────────── DATA ───────────────────────────────

    private static List<StoreSeedData> GetStoreData() =>
    [
        // ───── 1) HAMBURGUERIA ─────
        new StoreSeedData
        {
            Name = "Burguer do Rafa",
            SellerEmail = "rafa@burguer.com",
            Phone = "(11) 99999-0001",
            CuisineType = "Lanches",
            Description = "Hambúrgueres artesanais com ingredientes selecionados. O verdadeiro sabor do churrasco em formato de hambúrguer!",
            DeliveryFee = 5.90m,
            MinOrder = 15.00m,
            Street = "Rua Augusta",
            Number = "1500",
            Neighborhood = "Consolação",
            City = "São Paulo",
            State = "SP",
            ZipCode = "01304-001",
            BannerUrl = "https://images.unsplash.com/photo-1568901346375-23c9450c58cd?w=1200&q=80",
            LogoUrl = "https://images.unsplash.com/photo-1572802419224-296b0aeee0d9?w=400&q=80",
            Hours = AllDays("11:00", "23:00"),
            Categories =
            [
                new CategorySeed
                {
                    Name = "Hambúrgueres",
                    Order = 1,
                    Products =
                    [
                        P("Smash Burguer", "Pão brioche, smash de 120g, queijo cheddar, alface americana e molho especial.", 28.90m, 1, "https://images.unsplash.com/photo-1568901346375-23c9450c58cd?w=600&q=80"),
                        P("Costela Burguer", "Pão australiano, 150g de costela desfiada, queijo prato, cebola caramelizada e barbecue.", 34.90m, 2, "https://images.unsplash.com/photo-1550317138-10000687a72b?w=600&q=80"),
                        P("Bacon Triple", "Pão preto, 180g de blend bovino, bacon crocante, cheddar duplo e onion rings.", 36.90m, 3, "https://images.unsplash.com/photo-1565299624946-b28f40a0ae38?w=600&q=80"),
                        P("Veggie Burguer", "Hambúrguer de grão-de-bico, pão integral, rúcula, tomate seco e molho de iogurte.", 29.90m, 4, "https://images.unsplash.com/photo-1525059696034-4fc31aaa3f7d?w=600&q=80"),
                    ],
                },
                new CategorySeed
                {
                    Name = "Porções",
                    Order = 2,
                    Products =
                    [
                        P("Batata Cheddar", "Batata frita crocante coberta com cheddar cremoso e bacon.", 22.90m, 1, "https://images.unsplash.com/photo-1573080496219-bb080dd4f877?w=600&q=80"),
                        P("Anéis de Cebola", "Anéis empanados crocantes servidos com molho especial.", 18.90m, 2, "https://images.unsplash.com/photo-1639024471283-03518883512d?w=600&q=80"),
                        P("Nuggets de Frango", "Nuggets artesanais de peito de frango com molho barbecue.", 21.90m, 3, "https://images.unsplash.com/photo-1562967914-608f82629710?w=600&q=80"),
                    ],
                },
                new CategorySeed
                {
                    Name = "Bebidas",
                    Order = 3,
                    Products =
                    [
                        P("Coca-Cola Lata", "Refrigerante de cola 350ml.", 5.90m, 1, "https://images.unsplash.com/photo-1622483767028-3f66f32aef97?w=600&q=80"),
                        P("Suco Natural de Laranja", "Suco de laranja natural 500ml.", 8.90m, 2, "https://images.unsplash.com/photo-1613478223719-2ab802602423?w=600&q=80"),
                        P("Cerveja Artesanal IPA", "Cerveja artesanal IPA 473ml.", 12.90m, 3, "https://images.unsplash.com/photo-1608270586620-248524c67de9?w=600&q=80"),
                    ],
                },
            ],
        },

        // ───── 2) PIZZARIA ─────
        new StoreSeedData
        {
            Name = "Pizza do Rafa",
            SellerEmail = "rafa@pizza.com",
            Phone = "(11) 99999-0002",
            CuisineType = "Pizza",
            Description = "Pizzas tradicionais italianas com massa fina e crocante. Forno a lenha e ingredientes selecionados.",
            DeliveryFee = 6.90m,
            MinOrder = 22.00m,
            Street = "Rua Oscar Freire",
            Number = "800",
            Neighborhood = "Jardins",
            City = "São Paulo",
            State = "SP",
            ZipCode = "01426-001",
            BannerUrl = "https://images.unsplash.com/photo-1513104890138-7c749659a591?w=1200&q=80",
            LogoUrl = "https://images.unsplash.com/photo-1565299585323-38d6b0865b47?w=400&q=80",
            Hours = AllDays("17:00", "23:30"),
            Categories =
            [
                new CategorySeed
                {
                    Name = "Pizzas Salgadas",
                    Order = 1,
                    Products =
                    [
                        P("Margherita", "Molho de tomate San Marzano, mussarela de búfala, manjericão fresco e azeite extravirgem.", 45.90m, 1, "https://images.unsplash.com/photo-1604068549290-dea0e4a305ca?w=600&q=80"),
                        P("Pepperoni", "Pepperoni importado, mussarela, molho de tomate e orégano.", 49.90m, 2, "https://images.unsplash.com/photo-1628840042765-356cda07504e?w=600&q=80"),
                        P("Quattro Formaggi", "Mussarela, gorgonzola, parmesão e provolone. Massa fina e crocante.", 54.90m, 3, "https://images.unsplash.com/photo-1593504049359-74330189a345?w=600&q=80"),
                        P("Calabresa", "Calabresa fatiada, cebola roxa, mussarela e azeitonas.", 44.90m, 4, "https://images.unsplash.com/photo-1601924582970-9238bcb495d9?w=600&q=80"),
                        P("Portuguesa", "Presunto, mussarela, ovos, cebola, pimentão e azeitonas.", 47.90m, 5, "https://images.unsplash.com/photo-1574071318508-1cdbab80d002?w=600&q=80"),
                        P("Frango com Catupiry", "Frango desfiado, catupiry cremoso, milho e azeitona verde.", 46.90m, 6, "https://images.unsplash.com/photo-1565299543923-37dd37887442?w=600&q=80"),
                    ],
                },
                new CategorySeed
                {
                    Name = "Pizzas Doces",
                    Order = 2,
                    Products =
                    [
                        P("Nutella com Morango", "Nutella cremosa, morangos frescos e granulado de chocolate.", 39.90m, 1, "https://images.unsplash.com/photo-1612392061787-2d078b3e573e?w=600&q=80"),
                        P("Banana com Canela", "Banana caramelizada, canela e açúcar mascavo.", 34.90m, 2, "https://images.unsplash.com/photo-1571066811602-716837d681de?w=600&q=80"),
                    ],
                },
                new CategorySeed
                {
                    Name = "Bebidas",
                    Order = 3,
                    Products =
                    [
                        P("Coca-Cola 2L", "Refrigerante de cola 2 litros.", 12.90m, 1, "https://images.unsplash.com/photo-1554866585-cd94860890b7?w=600&q=80"),
                        P("Suco Natural de Uva", "Suco de uva integral 500ml.", 9.90m, 2, "https://images.unsplash.com/photo-1546173159-315724a31696?w=600&q=80"),
                    ],
                },
            ],
        },

        // ───── 3) JAPONÊS ─────
        new StoreSeedData
        {
            Name = "Sushi Rafa",
            SellerEmail = "rafa@sushi.com",
            Phone = "(11) 99999-0003",
            CuisineType = "Japonesa",
            Description = "Comida japonesa tradicional com toque contemporâneo. Peixes frescos e arroz temperado na medida certa.",
            DeliveryFee = 7.90m,
            MinOrder = 25.00m,
            Street = "Rua Liberdade",
            Number = "650",
            Neighborhood = "Liberdade",
            City = "São Paulo",
            State = "SP",
            ZipCode = "01505-010",
            BannerUrl = "https://images.unsplash.com/photo-1579871494447-9811cf80d66c?w=1200&q=80",
            LogoUrl = "https://images.unsplash.com/photo-1611143669185-af224c5e3252?w=400&q=80",
            Hours = AllDays("11:30", "22:30"),
            Categories =
            [
                new CategorySeed
                {
                    Name = "Combinados",
                    Order = 1,
                    Products =
                    [
                        P("Combinado Sakura (22 peças)", "10 sushis (salmão, atum, kani) + 8 uramakis filadélfia + 4 hots.", 68.90m, 1, "https://images.unsplash.com/photo-1579871494447-9811cf80d66c?w=600&q=80"),
                        P("Combinado Premium (32 peças)", "12 sushis variados + 10 uramakis especiais + 6 hots + 4 temakis.", 89.90m, 2, "https://images.unsplash.com/photo-1617196034796-73dfa7b1fd56?w=600&q=80"),
                        P("Combinado Light (14 peças)", "8 sushis de salmão + 6 uramakis vegetais + salada edamame.", 54.90m, 3, "https://images.unsplash.com/photo-1607301405390-d831c242f59b?w=600&q=80"),
                    ],
                },
                new CategorySeed
                {
                    Name = "Sushis e Sashimis",
                    Order = 2,
                    Products =
                    [
                        P("Sushi de Salmão (8 un)", "Salmão fresco sobre arroz temperado.", 24.90m, 1, "https://images.unsplash.com/photo-1611143669185-af224c5e3252?w=600&q=80"),
                        P("Sashimi de Salmão (10 fatias)", "Fatias de salmão fresco premium.", 38.90m, 2, "https://images.unsplash.com/photo-1648456961583-9c8e95cbb1c3?w=600&q=80"),
                        P("Temaki Salmão Filadélfia", "Cone de alga com salmão, cream cheese e arroz.", 18.90m, 3, "https://images.unsplash.com/photo-1583032015879-e5022cb87c3b?w=600&q=80"),
                        P("Hot Filadélfia (6 un)", "Uramaki empanado com salmão e cream cheese.", 22.90m, 4, "https://images.unsplash.com/photo-1564489563601-c53cfc451e93?w=600&q=80"),
                    ],
                },
                new CategorySeed
                {
                    Name = "Bebidas",
                    Order = 3,
                    Products =
                    [
                        P("Sakê Quente", "Sakê tradicional servido quente (150ml).", 14.90m, 1, "https://images.unsplash.com/photo-1614624532983-4ce03382d63d?w=600&q=80"),
                        P("Chá Gelado de Hibisco", "Chá de hibisco natural 500ml.", 7.90m, 2, "https://images.unsplash.com/photo-1556679343-c7306c1976bc?w=600&q=80"),
                        P("Água Mineral", "Água sem gás 500ml.", 3.90m, 3, "https://images.unsplash.com/photo-1548839140-29a749e1cf4d?w=600&q=80"),
                    ],
                },
            ],
        },
    ];

    private static ProductSeed P(string name, string desc, decimal price, int order, string imageUrl) =>
        new()
        {
            Name = name,
            Description = desc,
            Price = price,
            DisplayOrder = order,
            ImageUrl = imageUrl,
        };

    private static List<BusinessHourSeed> AllDays(string open, string close) =>
        Enumerable.Range(0, 7).Select(d => new BusinessHourSeed
        {
            DayOfWeek = (DayOfWeek)d,
            OpensAt = open,
            ClosesAt = close,
        }).ToList();

    private static string Slugify(string source)
    {
        var normalized = source.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (c == ' ' || c == '-' || c == '_') sb.Append('_');
        }
        var slug = sb.ToString().Trim('_');
        while (slug.Contains("__")) slug = slug.Replace("__", "_");
        return slug;
    }

    // ───────────────────── SEED RECORDS ─────────────────────────

    private sealed record StoreSeedData
    {
        public required string Name { get; init; }
        public required string SellerEmail { get; init; }
        public required string Phone { get; init; }
        public required string CuisineType { get; init; }
        public required string Description { get; init; }
        public required decimal DeliveryFee { get; init; }
        public required decimal MinOrder { get; init; }
        public required string Street { get; init; }
        public required string Number { get; init; }
        public required string Neighborhood { get; init; }
        public required string City { get; init; }
        public required string State { get; init; }
        public required string ZipCode { get; init; }
        public string? BannerUrl { get; init; }
        public string? LogoUrl { get; init; }
        public required List<BusinessHourSeed> Hours { get; init; }
        public required List<CategorySeed> Categories { get; init; }
    }

    private sealed record BusinessHourSeed
    {
        public required DayOfWeek DayOfWeek { get; init; }
        public required string OpensAt { get; init; }
        public required string ClosesAt { get; init; }
    }

    private sealed record CategorySeed
    {
        public required string Name { get; init; }
        public required int Order { get; init; }
        public required List<ProductSeed> Products { get; init; }
    }

    private sealed record ProductSeed
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required decimal Price { get; init; }
        public required int DisplayOrder { get; init; }
        public string? ImageUrl { get; init; }
    }
}
