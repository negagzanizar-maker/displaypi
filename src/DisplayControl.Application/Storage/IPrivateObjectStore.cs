namespace DisplayControl.Application.Storage;

public interface IPrivateObjectStore
{
    public Task PutAsync(
        PrivateObjectKey key,
        Stream content,
        long expectedLength,
        CancellationToken cancellationToken = default);

    public Task<Stream> OpenReadAsync(PrivateObjectKey key, CancellationToken cancellationToken = default);

    public Task<bool> ExistsAsync(PrivateObjectKey key, CancellationToken cancellationToken = default);

    public Task DeleteAsync(PrivateObjectKey key, CancellationToken cancellationToken = default);
}
