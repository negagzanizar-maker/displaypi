using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;

namespace DisplayControl.Api.Security;

public sealed class AuthenticationAccountRateLimitFilter(
    IAuthenticationAccountRateLimiter accountRateLimiter) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var rateLimitAttribute = context.ActionDescriptor.EndpointMetadata
            .OfType<EnableRateLimitingAttribute>()
            .FirstOrDefault();
        if (!string.Equals(rateLimitAttribute?.PolicyName, "authentication", StringComparison.Ordinal))
        {
            await next();
            return;
        }

        var key = FindAccountKey(context);
        if (key is null || accountRateLimiter.TryAcquire(key))
        {
            await next();
            return;
        }

        context.Result = new ObjectResult(new ProblemDetails
        {
            Type = "https://docs.example.invalid/problems/rate-limit",
            Title = "Too many authentication attempts. Try again later.",
            Status = StatusCodes.Status429TooManyRequests,
            Extensions = { ["code"] = "authentication_rate_limited" }
        })
        {
            StatusCode = StatusCodes.Status429TooManyRequests
        };
    }

    private static string? FindAccountKey(ActionExecutingContext context)
    {
        foreach (var argument in context.ActionArguments.Values.Where(value => value is not null))
        {
            var email = argument!.GetType().GetProperty("Email")?.GetValue(argument) as string;
            if (!string.IsNullOrWhiteSpace(email))
            {
                return DigestAccountKey("email", email.Trim().ToUpperInvariant());
            }

            var token = argument.GetType().GetProperty("Token")?.GetValue(argument) as string;
            if (!string.IsNullOrWhiteSpace(token))
            {
                return DigestAccountKey("token", token);
            }
        }

        return context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } userId
            ? DigestAccountKey("user", userId)
            : null;
    }

    private static string DigestAccountKey(string category, string value) =>
        category + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public interface IAuthenticationAccountRateLimiter
{
    public bool TryAcquire(string key);
}

public sealed record AuthenticationAccountRateLimitOptions(int PermitLimit, TimeSpan Window)
{
    public static AuthenticationAccountRateLimitOptions Default { get; } = new(10, TimeSpan.FromMinutes(5));

    public bool IsValid() => PermitLimit > 0 && Window > TimeSpan.Zero;
}

public sealed class AuthenticationAccountRateLimiter : IAuthenticationAccountRateLimiter
{
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly AuthenticationAccountRateLimitOptions _options;
    private readonly object _gate = new();
    private readonly MemoryCacheEntryOptions _entryOptions = new MemoryCacheEntryOptions()
        .SetSize(1)
        .SetSlidingExpiration(TimeSpan.FromMinutes(30));

    public AuthenticationAccountRateLimiter(
        IMemoryCache cache,
        TimeProvider timeProvider,
        AuthenticationAccountRateLimitOptions options)
    {
        if (!options.IsValid())
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Authentication rate-limit values must be positive.");
        }

        _cache = cache;
        _timeProvider = timeProvider;
        _options = options;
    }

    public bool TryAcquire(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_gate)
        {
            var attempts = _cache.GetOrCreate(key, entry =>
            {
                entry.SetOptions(_entryOptions);
                return new Queue<long>();
            }) ?? throw new InvalidOperationException("Authentication account limiter could not be created.");
            var now = _timeProvider.GetTimestamp();
            while (attempts.TryPeek(out var timestamp) &&
                   _timeProvider.GetElapsedTime(timestamp, now) >= _options.Window)
            {
                attempts.Dequeue();
            }

            if (attempts.Count >= _options.PermitLimit)
            {
                return false;
            }

            attempts.Enqueue(now);
            return true;
        }
    }
}
