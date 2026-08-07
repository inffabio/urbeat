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
            // Hero Section
            new() { Section = "Hero", Key = "Title", Value = "Seu delivery, com cara de restaurante.", DisplayOrder = 1, IsActive = true, Description = "Título principal da seção Hero" },
            new() { Section = "Hero", Key = "Subtitle", Value = "Cardápio digital, pedidos online e painel de gestão num só sistema — feito pra quem ainda atende cada cliente pelo nome.", DisplayOrder = 2, IsActive = true, Description = "Subtítulo da seção Hero" },
            
            // Stats Section
            new() { Section = "Stats", Key = "StoreCount", Value = "1.2k+", DisplayOrder = 1, IsActive = true, Description = "Estatística: Número de lojas ativas" },
            new() { Section = "Stats", Key = "OrderIncrease", Value = "38%", DisplayOrder = 2, IsActive = true, Description = "Estatística: Aumento médio em pedidos" },
            new() { Section = "Stats", Key = "SetupTime", Value = "5min", DisplayOrder = 3, IsActive = true, Description = "Estatística: Tempo para colocar o menu no ar" },
            new() { Section = "Stats", Key = "Fee", Value = "0%", DisplayOrder = 4, IsActive = true, Description = "Estatística: Taxa por pedido recebido" },

            // Features Section (Descriptions only, titles are hardcoded in UI for now, but can be made dynamic later)
            new() { Section = "Features", Key = "RealTimeOrders_Desc", Value = "Alertas instantâneos com som personalizado. Nenhum pedido passa batido.", DisplayOrder = 1, IsActive = true, Description = "Descrição do card de Pedidos em tempo real" },
            new() { Section = "Features", Key = "OrderStatus_Desc", Value = "Recebido, em preparo, saiu pra entrega, entregue. Cliente acompanha em tempo real.", DisplayOrder = 2, IsActive = true, Description = "Descrição do card de Status do pedido" },
            new() { Section = "Features", Key = "StressFree_Desc", Value = "Cardápio, preços, horários e taxas num painel simples. Edita e publica em segundos.", DisplayOrder = 3, IsActive = true, Description = "Descrição do card de Gestão sem stress" },
            new() { Section = "Features", Key = "WhatsApp_Desc", Value = "Conversa com cliente direto do painel. Notificação automática a cada novo pedido.", DisplayOrder = 4, IsActive = true, Description = "Descrição do card de WhatsApp integrado" }
        };

        await _dbContext.LandingPageContents.AddRangeAsync(defaultContents, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
