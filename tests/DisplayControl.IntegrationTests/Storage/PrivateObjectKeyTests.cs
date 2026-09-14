using DisplayControl.Application.Storage;

namespace DisplayControl.IntegrationTests.Storage;

public sealed class PrivateObjectKeyTests
{
    [Fact]
    public void KeyUsesOnlyOpaqueNormalizedIdentifiers()
    {
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var objectId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var key = new PrivateObjectKey(tenantId, objectId);

        Assert.Equal(
            "tenants/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/objects/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            key.ToString());
    }

    [Fact]
    public void EmptyIdentifierIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new PrivateObjectKey(Guid.Empty, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new PrivateObjectKey(Guid.NewGuid(), Guid.Empty));
    }
}
