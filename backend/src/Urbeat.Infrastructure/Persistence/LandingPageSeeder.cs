using Urbeat.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Persistence;

public sealed class LandingPageSeeder
{
    private readonly ApplicationDbContext _dbContext;

    public LandingPageSeeder(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var hasData = await _dbContext.LandingPageContents.AnyAsync(cancellationToken);
        if (hasData)
        {
            return;
        }

        var defaultContents = new List<LandingPageContent>
        {
            // Hero Section (landpage UrBeat Green)
            new() { Section = "Hero", Key = "Title", Value = "Nunca vendeu por delivery? Comece hoje com seu app próprio.", DisplayOrder = 1, IsActive = true, Description = "Título principal da seção Hero" },
            new() { Section = "Hero", Key = "Subtitle", Value = "A UrBeat foi feita para quem está começando. Sem complicação, sem taxas por pedido, sem precisar saber de tecnologia. Você cadastra sua loja, divulga seu link e começa a receber pedidos organizados.", DisplayOrder = 2, IsActive = true, Description = "Subtítulo da seção Hero" },
            new() { Section = "Hero", Key = "Badge", Value = "Chega de dividir seu lucro no delivery. Teste por 15 dias.", DisplayOrder = 3, IsActive = true, Description = "Badge do topo do Hero" },

            // Features Section (6 cards da landpage UrBeat Green)
            new() { Section = "Features", Key = "AppBrand_Desc", Value = "Seus clientes usam seu app, com sua marca. Sem necessidade de você dividir seu lucro.", DisplayOrder = 1, IsActive = true, Description = "Card: App com sua marca" },
            new() { Section = "Features", Key = "WhatsappMenu_Desc", Value = "Link único que abre cardápio lindo e já manda pedido formatado no WhatsApp. Zero fricção.", DisplayOrder = 2, IsActive = true, Description = "Card: Cardápio inteligente pro WhatsApp" },
            new() { Section = "Features", Key = "OrdersPanel_Desc", Value = "Aceite, acompanhe e dispare entregas. Som de novo pedido, tudo organizado.", DisplayOrder = 3, IsActive = true, Description = "Card: Painel de pedidos em tempo real" },
            new() { Section = "Features", Key = "AutoPrint_Desc", Value = "Pediu, imprimiu. Integra com impressora térmica via Wi-Fi. Sem erro, sem atraso.", DisplayOrder = 4, IsActive = true, Description = "Card: Impressão automática na cozinha" },
            new() { Section = "Features", Key = "EasyMgmt_Desc", Value = "Cardápio, preços, horários e taxas num painel simples. Edita e publica em segundos.", DisplayOrder = 5, IsActive = true, Description = "Card: Gestão sem stress" },
            new() { Section = "Features", Key = "Reports_Desc", Value = "Veja quem compra, quanto gasta, qual horário vende mais. Exporte base e faça reengajamento.", DisplayOrder = 6, IsActive = true, Description = "Card: Relatórios e base de clientes" }
        };

        await _dbContext.LandingPageContents.AddRangeAsync(defaultContents, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
