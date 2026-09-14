# Private Object Storage

## Boundary

Media bytes are never stored in SQL Server and never served from a public web root. Application code addresses an object through `PrivateObjectKey`, which contains only normalized tenant and object UUIDs. Database `storage_key` values are server-generated references; a browser filename, URL, absolute path, or device-supplied value must never become an object key.

## Filesystem adapter

`LocalPrivateObjectStore` is the implemented baseline for development, tests, and an explicitly approved single-API-node deployment backed by a private encrypted volume. It requires a dedicated absolute root and a positive size ceiling. It:

- resolves GUID-only keys beneath the configured root;
- rejects symbolic links/reparse points in managed paths;
- rejects unreadable streams and declared lengths outside the configured ceiling;
- aborts when actual and declared lengths differ;
- writes a temporary object in the destination directory and atomically publishes it;
- refuses to overwrite an immutable object; and
- exposes no physical path through the application contract.

The root must not be synchronized into the repository, mounted as a static-file directory, or shared with unrelated workloads. It must be owner-only, encrypted at rest by the deployment volume, backed up coherently with SQL Server metadata, and mounted on exactly one API writer. Horizontal API replicas require the production adapter gate below.

## Production adapter gate

A multi-node or cloud production deployment requires a private S3-compatible adapter or an explicitly approved equivalent. Selection is deferred until the hosting environment is known. The adapter must prove:

- private buckets/containers with blocked anonymous access;
- TLS validation and narrowly scoped workload identity;
- tenant-prefixed opaque keys without trusting caller paths;
- server-side encryption and documented key ownership/rotation;
- bounded streaming uploads and downloads without buffering whole videos;
- conditional create/no-overwrite semantics for immutable versions;
- quarantine and approved-object separation;
- short-lived, audience-limited delivery authorization or API streaming;
- lifecycle/retention rules, versioning policy and deletion audit; and
- integration tests against the selected production-compatible service.

Adding a cloud/S3 SDK and its pinned version requires dependency review and vulnerability scanning before implementation.
