using DisplayControl.Application.Storage;
using DisplayControl.Infrastructure.Storage;

namespace DisplayControl.IntegrationTests.Storage;

public sealed class LocalPrivateObjectStoreTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        "display-control-object-store-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PutPublishesExactContentAndCanReadItBack()
    {
        var store = new LocalPrivateObjectStore(_testRoot, maximumObjectBytes: 1024);
        var key = new PrivateObjectKey(Guid.NewGuid(), Guid.NewGuid());
        var bytes = "private-content"u8.ToArray();

        await store.PutAsync(key, new MemoryStream(bytes), bytes.Length);

        Assert.True(await store.ExistsAsync(key));
        await using var content = await store.OpenReadAsync(key);
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
    }

    [Fact]
    public async Task LengthMismatchDoesNotPublishPartialObject()
    {
        var store = new LocalPrivateObjectStore(_testRoot, maximumObjectBytes: 1024);
        var key = new PrivateObjectKey(Guid.NewGuid(), Guid.NewGuid());

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.PutAsync(key, new MemoryStream([1, 2, 3]), expectedLength: 4));

        Assert.False(await store.ExistsAsync(key));
    }

    [Fact]
    public async Task ExistingObjectCannotBeOverwritten()
    {
        var store = new LocalPrivateObjectStore(_testRoot, maximumObjectBytes: 1024);
        var key = new PrivateObjectKey(Guid.NewGuid(), Guid.NewGuid());
        await store.PutAsync(key, new MemoryStream([1]), expectedLength: 1);

        await Assert.ThrowsAsync<IOException>(() =>
            store.PutAsync(key, new MemoryStream([2]), expectedLength: 1));

        await using var content = await store.OpenReadAsync(key);
        Assert.Equal(1, content.ReadByte());
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }
}
