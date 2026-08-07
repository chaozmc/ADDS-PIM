using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ADDS.PIM.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for generating reviewed migrations. Runtime connection
/// strings are supplied by protected application configuration.
/// </summary>
public sealed class PimDbContextFactory : IDesignTimeDbContextFactory<PimDbContext>
{
    public PimDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PimDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\MSSQLLocalDB;Database=ADDS_PIM_DesignTime;Integrated Security=True;TrustServerCertificate=True")
            .Options;

        return new PimDbContext(options);
    }
}
