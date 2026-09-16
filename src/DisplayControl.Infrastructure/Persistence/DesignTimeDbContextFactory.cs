using DisplayControl.Application.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DisplayControl.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DisplayControlDbContext>
{
    public DisplayControlDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DISPLAYCONTROL_MIGRATION_CONNECTION")
            ?? "Server=127.0.0.1,14333;Database=display_control_design;User Id=sa;Password=NotUsedForModelGeneration123!;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<DisplayControlDbContext>()
            .UseSqlServer(connectionString, sqlServer =>
                sqlServer.MigrationsAssembly("DisplayControl.SqlServerMigrations"))
            .Options;

        return new DisplayControlDbContext(options, NullTenantContext.Instance);
    }

    private sealed class NullTenantContext : ICurrentTenant
    {
        public static NullTenantContext Instance { get; } = new();

        public Guid? TenantId => null;
    }
}
