using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Tests.Tenancy;

public sealed class TenantLifecycleTests
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TenantNormalizesSlugAndSupportsSuspendReactivateArchiveLifecycle()
    {
        var tenant = new Tenant(Guid.NewGuid(), "Customer", "Customer-One", "Africa/Casablanca", CreatedAtUtc);

        Assert.Equal("customer-one", tenant.Slug);
        tenant.ChangeState(TenantState.Suspended, CreatedAtUtc.AddMinutes(1));
        tenant.ChangeState(TenantState.Active, CreatedAtUtc.AddMinutes(2));
        tenant.UpdateDetails("Customer renamed", "UTC", CreatedAtUtc.AddMinutes(3));
        tenant.ChangeState(TenantState.Archived, CreatedAtUtc.AddMinutes(4));

        Assert.Equal(TenantState.Archived, tenant.State);
        Assert.Equal("Customer renamed", tenant.Name);
        Assert.Equal("UTC", tenant.TimeZone);
        Assert.Throws<InvalidOperationException>(() =>
            tenant.ChangeState(TenantState.Active, CreatedAtUtc.AddMinutes(5)));
        Assert.Throws<InvalidOperationException>(() =>
            tenant.UpdateDetails("Forbidden", "UTC", CreatedAtUtc.AddMinutes(5)));
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("-customer")]
    [InlineData("customer-")]
    [InlineData("customer_name")]
    [InlineData("client é")]
    public void TenantRejectsUnsafeSlug(string slug)
    {
        Assert.Throws<ArgumentException>(() =>
            new Tenant(Guid.NewGuid(), "Customer", slug, "UTC", CreatedAtUtc));
    }

    [Fact]
    public void TenantRejectsUnknownTimeZone()
    {
        Assert.Throws<ArgumentException>(() =>
            new Tenant(Guid.NewGuid(), "Customer", "customer", "Not/A-Time-Zone", CreatedAtUtc));
    }
}
