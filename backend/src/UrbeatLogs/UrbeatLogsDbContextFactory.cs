using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UrbeatLogs;

public class UrbeatLogsDbContextFactory : IDesignTimeDbContextFactory<UrbeatLogsDbContext>
{
    public UrbeatLogsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<UrbeatLogsDbContext>();
        optionsBuilder.UseNpgsql("Host=192.168.1.15;Database=UrbeatLogs;Username=postgres;Password=postgres");
        return new UrbeatLogsDbContext(optionsBuilder.Options);
    }
}
