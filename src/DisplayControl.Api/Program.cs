using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DisplayControl.Api.Devices;
using DisplayControl.Api.Identity;
using DisplayControl.Api.Notifications;
using DisplayControl.Api.Operations;
using DisplayControl.Api.Realtime;
using DisplayControl.Api.Scheduling;
using DisplayControl.Api.Security;
using DisplayControl.Application.Content;
using DisplayControl.Application.Security;
using DisplayControl.Application.Storage;
using DisplayControl.Application.Tenancy;
using DisplayControl.Domain.Identity;
using DisplayControl.Infrastructure.Content;
using DisplayControl.Infrastructure.Identity;
using DisplayControl.Infrastructure.Persistence;
using DisplayControl.Infrastructure.Security;
using DisplayControl.Infrastructure.Storage;
using DisplayControl.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.EntityFrameworkCore;

var packagedWebRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Directory.Exists(packagedWebRoot) ? packagedWebRoot : null
});

var deploymentInstanceCount = builder.Configuration.GetValue<int?>("Deployment:InstanceCount") ?? 1;
if (deploymentInstanceCount != 1)
{
    throw new InvalidOperationException(
        "Deployment:InstanceCount must be 1 while authentication account rate limiting uses local memory.");
}

var contentMaximumObjectBytes = builder.Configuration.GetValue<long?>("ContentStorage:MaximumObjectBytes")
    ?? 268_435_456;
if (contentMaximumObjectBytes is < 1_024 or > 2_147_483_648)
{
    throw new InvalidOperationException("ContentStorage:MaximumObjectBytes must be between 1 KiB and 2 GiB.");
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = checked(contentMaximumObjectBytes + 1_048_576);
    options.ConfigureHttpsDefaults(https =>
    {
        https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
        // TLS transports the presented certificate; DeviceCertificateAuthenticationMiddleware then
        // performs the authoritative custom-root, identity-extension, database-state, and tenant checks.
        https.ClientCertificateValidation = static (_, _, _) => true;
    });
});

var isTesting = builder.Environment.IsEnvironment("Testing");
var humanAuthenticationOptions = new HumanAuthenticationOptions(
    builder.Configuration.GetValue<bool?>("Security:HumanAuthentication:RequireMfa") ?? true);
if (!humanAuthenticationOptions.RequireMfa && !builder.Environment.IsDevelopment() && !isTesting)
{
    throw new InvalidOperationException(
        "Password-only human authentication is permitted only in Development or Testing environments.");
}

var databaseConnectionString = builder.Configuration.GetConnectionString("Database");
if (string.IsNullOrWhiteSpace(databaseConnectionString))
{
    if (!isTesting)
    {
        throw new InvalidOperationException(
            "ConnectionStrings:Database is required. Supply it through environment variables or a secret provider.");
    }

    databaseConnectionString = "Server=127.0.0.1,1433;Database=display_control_testing_unconnected;User Id=unused;Password=unused;TrustServerCertificate=True";
}

if (builder.Environment.IsProduction())
{
    ProductionConfigurationValidator.Validate(builder.Configuration, databaseConnectionString);
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache(options => options.SizeLimit = 10_000);
builder.Services.AddScoped<ScopedTenantContext>();
builder.Services.AddScoped<ICurrentTenant>(services => services.GetRequiredService<ScopedTenantContext>());
var databaseProvider = builder.Configuration["Database:Provider"] ?? "SqlServer";
if (!string.Equals(databaseProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("Database:Provider must be SqlServer.");
}
builder.Services.AddDbContext<DisplayControlDbContext>(options =>
{
    options.UseSqlServer(
        databaseConnectionString,
        sqlServer => sqlServer.MigrationsAssembly("DisplayControl.SqlServerMigrations"));
});

var dataProtection = builder.Services.AddDataProtection().SetApplicationName("DisplayControl");
if (isTesting)
{
    dataProtection.UseEphemeralDataProtectionProvider();
}
else
{
    var keyDirectory = builder.Configuration["Security:DataProtectionKeyDirectory"];
    if (string.IsNullOrWhiteSpace(keyDirectory) || !Path.IsPathFullyQualified(keyDirectory))
    {
        throw new InvalidOperationException(
            "Security:DataProtectionKeyDirectory must be a dedicated absolute path supplied by deployment configuration.");
    }

    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
    if (builder.Environment.IsProduction())
    {
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            builder.Configuration["Security:DataProtectionCertificate:PfxPath"]!,
            builder.Configuration["Security:DataProtectionCertificate:PfxPassword"],
            X509KeyStorageFlags.EphemeralKeySet);
        using var dataProtectionPrivateKey = certificate.GetRSAPrivateKey();
        if (dataProtectionPrivateKey is null ||
            DateTimeOffset.UtcNow < certificate.NotBefore.ToUniversalTime() ||
            DateTimeOffset.UtcNow >= certificate.NotAfter.ToUniversalTime())
        {
            certificate.Dispose();
            throw new InvalidOperationException(
                "The production Data Protection certificate must contain a currently valid private key.");
        }

        builder.Services.AddSingleton(certificate);
        dataProtection.ProtectKeysWithCertificate(certificate);
    }
}

var pepperValue = builder.Configuration["Security:TokenDigestPepperBase64"];
byte[] tokenDigestPepper;
if (isTesting && string.IsNullOrWhiteSpace(pepperValue))
{
    tokenDigestPepper = Enumerable.Repeat((byte)0xA5, 32).ToArray();
}
else
{
    try
    {
        tokenDigestPepper = Convert.FromBase64String(pepperValue ?? string.Empty);
    }
    catch (FormatException exception)
    {
        throw new InvalidOperationException("Security:TokenDigestPepperBase64 must be valid Base64.", exception);
    }

    if (tokenDigestPepper.Length < 32)
    {
        throw new InvalidOperationException("Security:TokenDigestPepperBase64 must decode to at least 32 random bytes.");
    }
}

var secureTokenService = new HmacSecureTokenService(tokenDigestPepper);
CryptographicOperations.ZeroMemory(tokenDigestPepper);
builder.Services.AddSingleton<ISecureTokenService>(secureTokenService);
builder.Services.AddSingleton<ITenantCapabilityTokenService, TenantCapabilityTokenService>();
builder.Services.AddSingleton<ITotpService, Rfc6238TotpService>();
builder.Services.AddSingleton<IMfaSecretProtector, DataProtectionMfaSecretProtector>();
builder.Services.AddSingleton(humanAuthenticationOptions);
builder.Services.AddSingleton<ISensitivePayloadProtector, IdentityNotificationPayloadProtector>();
builder.Services.AddSingleton(new PlatformBootstrapCredential(
    builder.Configuration["Security:PlatformBootstrapTokenSha256Base64"]));
builder.Services.AddSingleton(TimeProvider.System);
if (builder.Configuration.GetValue<bool>("Notifications:DeliveryEnabled"))
{
    builder.Services.AddSingleton(NotificationDeliveryOptions.FromConfiguration(builder.Configuration));
    builder.Services.AddHostedService<IdentityNotificationDeliveryWorker>();
}

if (builder.Configuration.GetValue<bool>("Operations:Retention:Enabled"))
{
    builder.Services.AddSingleton(OperationalDataRetentionOptions.FromConfiguration(builder.Configuration));
    builder.Services.AddHostedService<OperationalDataRetentionWorker>();
}

var privateStorageRoot = isTesting
    ? Path.Combine(Path.GetTempPath(), "display-control-api-tests", Guid.NewGuid().ToString("N"))
    : builder.Configuration["ContentStorage:RootDirectory"];
if (string.IsNullOrWhiteSpace(privateStorageRoot) || !Path.IsPathFullyQualified(privateStorageRoot))
{
    throw new InvalidOperationException("ContentStorage:RootDirectory must be a dedicated absolute path.");
}

builder.Services.AddSingleton(new ContentStorageOptions(contentMaximumObjectBytes));
builder.Services.AddSingleton<IPrivateObjectStore>(
    new LocalPrivateObjectStore(privateStorageRoot, contentMaximumObjectBytes));
if (isTesting)
{
    builder.Services.AddSingleton<IContentMalwareScanner, AllowAllContentMalwareScanner>();
}
else
{
    var scannerHost = builder.Configuration["ContentScanning:ClamAv:Host"] ?? "127.0.0.1";
    var scannerPort = builder.Configuration.GetValue<int?>("ContentScanning:ClamAv:Port") ?? 3310;
    var scannerTimeoutSeconds = builder.Configuration.GetValue<int?>("ContentScanning:ClamAv:TimeoutSeconds") ?? 120;
    builder.Services.AddSingleton<IContentMalwareScanner>(
        new ClamAvContentMalwareScanner(scannerHost, scannerPort, TimeSpan.FromSeconds(scannerTimeoutSeconds)));
}

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = checked(contentMaximumObjectBytes + 1_048_576);
    options.ValueLengthLimit = 16 * 1024;
    options.MultipartHeadersLengthLimit = 16 * 1024;
});

var deviceCertificateLifetimeDays = builder.Configuration.GetValue<int?>(
    "Security:DeviceCertificateAuthority:IssuedLifetimeDays") ?? 90;
if (deviceCertificateLifetimeDays is < 1 or > 90)
{
    throw new InvalidOperationException("Device certificate lifetime must be between 1 and 90 days.");
}

DeviceCertificateIssuer deviceCertificateIssuer;
if (isTesting)
{
    deviceCertificateIssuer = DeviceCertificateIssuerFactory.CreateEphemeralForTesting(
        DateTimeOffset.UtcNow,
        TimeSpan.FromDays(deviceCertificateLifetimeDays));
}
else
{
    deviceCertificateIssuer = DeviceCertificateIssuerFactory.LoadFileBacked(
        builder.Configuration["Security:DeviceCertificateAuthority:PfxPath"] ?? string.Empty,
        builder.Configuration["Security:DeviceCertificateAuthority:PfxPassword"] ?? string.Empty,
        TimeSpan.FromDays(deviceCertificateLifetimeDays));
}

builder.Services.AddSingleton<IDeviceCertificateIssuer>(deviceCertificateIssuer);

EcdsaLicenseLeaseSigner licenseLeaseSigner;
if (isTesting)
{
    licenseLeaseSigner = LicenseLeaseSignerFactory.CreateEphemeralForTesting();
}
else
{
    licenseLeaseSigner = LicenseLeaseSignerFactory.LoadFileBacked(
        builder.Configuration["Security:LicenseSigningKey:PrivateKeyPath"] ?? string.Empty,
        builder.Configuration["Security:LicenseSigningKey:Password"] ?? string.Empty,
        builder.Configuration.GetSection("Security:LicenseSigningKey:VerificationPublicKeyPaths").Get<string[]>());
}

builder.Services.AddSingleton<ILicenseLeaseSigner>(licenseLeaseSigner);
var offlineAllowanceHours = builder.Configuration.GetValue<int?>("DeviceProtocol:OfflineAllowanceHours") ?? 24;
if (offlineAllowanceHours is < 1 or > 24)
{
    throw new InvalidOperationException("DeviceProtocol:OfflineAllowanceHours must be between 1 and 24.");
}

builder.Services.AddSingleton(new DeviceProtocolOptions(TimeSpan.FromHours(offlineAllowanceHours)));
builder.Services.AddScoped<UserSessionService>();
builder.Services.AddScoped<UniformPasswordFailureService>();
builder.Services.AddScoped<TenantSecurityAuditService>();
builder.Services.AddScoped<InvitationWorkflow>();
builder.Services.AddScoped<AuthenticationAccountRateLimitFilter>();
builder.Services.AddSingleton(AuthenticationAccountRateLimitOptions.Default);
builder.Services.AddSingleton<IAuthenticationAccountRateLimiter, AuthenticationAccountRateLimiter>();
builder.Services.AddSingleton<IAuthorizationHandler, TenantRouteRequirement>();

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 15;
        options.Password.RequiredUniqueChars = 1;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.SignIn.RequireConfirmedEmail = true;
        options.User.RequireUniqueEmail = true;
        options.Tokens.EmailConfirmationTokenProvider = TokenOptions.DefaultProvider;
        options.Tokens.PasswordResetTokenProvider = TokenOptions.DefaultProvider;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<ApplicationClaimsPrincipalFactory>()
    .AddEntityFrameworkStores<DisplayControlDbContext>()
    .AddPasswordValidator<CommonPasswordValidator>()
    .AddTokenProvider<DataProtectorTokenProvider<ApplicationUser>>(TokenOptions.DefaultProvider);

builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
    options.TokenLifespan = TimeSpan.FromHours(2));
builder.Services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
        options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
    })
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.Cookie.Name = "__Host-dc.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = false;
        options.LoginPath = "/api/v1/session";
        options.AccessDeniedPath = "/api/v1/session";
        options.Events.OnRedirectToLogin = CookieRedirects.ReturnUnauthorized;
        options.Events.OnRedirectToAccessDenied = CookieRedirects.ReturnForbidden;
    })
    .AddCookie(IdentityConstants.TwoFactorUserIdScheme, options =>
    {
        options.Cookie.Name = "__Host-dc.mfa";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        options.SlidingExpiration = false;
        options.Events.OnRedirectToLogin = CookieRedirects.ReturnUnauthorized;
        options.Events.OnRedirectToAccessDenied = CookieRedirects.ReturnForbidden;
    });

builder.Services.AddAuthorization(options =>
{
    var fullSession = new AuthorizationPolicyBuilder(IdentityConstants.ApplicationScheme)
        .RequireAuthenticatedUser()
        .RequireClaim(SessionClaimTypes.AuthenticationStage, SessionClaimTypes.FullStage)
        .Build();
    options.FallbackPolicy = fullSession;
    options.AddPolicy(
        AuthorizationPolicies.AuthenticatedSession,
        policy => policy.RequireAuthenticatedUser());
    options.AddPolicy(AuthorizationPolicies.FullSession, fullSession);
    options.AddPolicy(
        AuthorizationPolicies.MfaPending,
        policy => policy.RequireAuthenticatedUser().RequireClaim(
            SessionClaimTypes.AuthenticationStage,
            SessionClaimTypes.MfaPendingStage));
    options.AddPolicy(
        AuthorizationPolicies.MfaEnrollment,
        policy => policy.RequireAuthenticatedUser().RequireClaim(
            SessionClaimTypes.AuthenticationStage,
            SessionClaimTypes.MfaEnrollmentStage));
    var mfaVerifiedSession = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim(SessionClaimTypes.AuthenticationStage, SessionClaimTypes.FullStage);
    var recentMfaSession = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim(SessionClaimTypes.AuthenticationStage, SessionClaimTypes.FullStage);
    if (humanAuthenticationOptions.RequireMfa)
    {
        mfaVerifiedSession.RequireClaim(SessionClaimTypes.AuthenticationMethod, "mfa");
        recentMfaSession
            .RequireClaim(SessionClaimTypes.AuthenticationMethod, "mfa")
            .AddRequirements(new RecentMfaRequirement(RecentMfaRequirement.DefaultMaximumAge));
    }
    options.AddPolicy(AuthorizationPolicies.MfaVerifiedSession, mfaVerifiedSession.Build());
    options.AddPolicy(AuthorizationPolicies.RecentMfaSession, recentMfaSession.Build());
    options.AddPolicy(
        AuthorizationPolicies.TenantViewer,
        policy => policy
            .RequireClaim(SessionClaimTypes.AuthenticationStage, SessionClaimTypes.FullStage)
            .RequireClaim(
                SessionClaimTypes.TenantRole,
                nameof(TenantRole.TenantAdmin),
                nameof(TenantRole.ContentManager),
                nameof(TenantRole.Viewer))
            .AddRequirements(new TenantRouteRequirement()));
    var tenantContentManager = new AuthorizationPolicyBuilder()
        .RequireClaim(SessionClaimTypes.AuthenticationStage, SessionClaimTypes.FullStage)
        .RequireClaim(
            SessionClaimTypes.TenantRole,
            nameof(TenantRole.TenantAdmin),
            nameof(TenantRole.ContentManager))
        .AddRequirements(new TenantRouteRequirement());
    var tenantAdministrator = new AuthorizationPolicyBuilder()
        .RequireClaim(SessionClaimTypes.AuthenticationStage, SessionClaimTypes.FullStage)
        .RequireClaim(SessionClaimTypes.TenantRole, nameof(TenantRole.TenantAdmin))
        .AddRequirements(new TenantRouteRequirement());
    var tenantAdministratorRecentMfa = new AuthorizationPolicyBuilder()
        .RequireClaim(SessionClaimTypes.AuthenticationStage, SessionClaimTypes.FullStage)
        .RequireClaim(SessionClaimTypes.TenantRole, nameof(TenantRole.TenantAdmin))
        .AddRequirements(new TenantRouteRequirement());
    var platformAdministrator = new AuthorizationPolicyBuilder()
        .RequireClaim(SessionClaimTypes.AuthenticationStage, SessionClaimTypes.FullStage)
        .RequireRole("PlatformAdministrator");
    var platformAdministratorRecentMfa = new AuthorizationPolicyBuilder()
        .RequireClaim(SessionClaimTypes.AuthenticationStage, SessionClaimTypes.FullStage)
        .RequireRole("PlatformAdministrator");
    if (humanAuthenticationOptions.RequireMfa)
    {
        tenantContentManager.RequireClaim(SessionClaimTypes.AuthenticationMethod, "mfa");
        tenantAdministrator.RequireClaim(SessionClaimTypes.AuthenticationMethod, "mfa");
        tenantAdministratorRecentMfa
            .RequireClaim(SessionClaimTypes.AuthenticationMethod, "mfa")
            .AddRequirements(new RecentMfaRequirement(RecentMfaRequirement.DefaultMaximumAge));
        platformAdministrator.RequireClaim(SessionClaimTypes.AuthenticationMethod, "mfa");
        platformAdministratorRecentMfa
            .RequireClaim(SessionClaimTypes.AuthenticationMethod, "mfa")
            .AddRequirements(new RecentMfaRequirement(RecentMfaRequirement.DefaultMaximumAge));
    }
    options.AddPolicy(AuthorizationPolicies.TenantContentManager, tenantContentManager.Build());
    options.AddPolicy(AuthorizationPolicies.TenantAdministrator, tenantAdministrator.Build());
    options.AddPolicy(AuthorizationPolicies.TenantAdministratorRecentMfa, tenantAdministratorRecentMfa.Build());
    options.AddPolicy(AuthorizationPolicies.PlatformAdministrator, platformAdministrator.Build());
    options.AddPolicy(AuthorizationPolicies.PlatformAdministratorRecentMfa, platformAdministratorRecentMfa.Build());
    options.AddPolicy(
        AuthorizationPolicies.DeviceAuthenticated,
        policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(
                DeviceClaimTypes.AuthenticationMethod,
                DeviceClaimTypes.MutualTlsAuthenticationMethod)
            .RequireClaim(DeviceClaimTypes.DeviceId)
            .RequireClaim(DeviceClaimTypes.TenantId));
});
builder.Services.AddSingleton<IAuthorizationHandler, RecentMfaAuthorizationHandler>();
builder.Services.AddScoped<DesiredStateCompilationService>();
builder.Services.AddScoped<DesiredStateResolver>();
builder.Services.AddScoped<DeviceHeartbeatWorkflow>();
builder.Services.AddSingleton<DeviceStateChangeBroker>();
builder.Services.AddScoped<DeviceStateChangeNotifications>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            "global",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 2_000,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("authentication", context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(5),
                SegmentsPerWindow = 5,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("device-enrollment", context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                SegmentsPerWindow = 5,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("platform-bootstrap", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-dc.csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
});

builder.Services.AddScoped<ApiAntiforgeryFilter>();
builder.Services
    .AddControllers(options =>
    {
        options.Filters.AddService<ApiAntiforgeryFilter>();
        options.Filters.Add<TenantTransactionCommitFilter>();
        options.Filters.AddService<AuthenticationAccountRateLimitFilter>();
    })
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi("v1");
builder.Services.AddSingleton(ReadinessProbeOptions.Default);
builder.Services.AddSingleton<IReadinessDependency, DatabaseReadinessDependency>();
builder.Services.AddSingleton<IReadinessDependency, PrivateStorageReadinessDependency>();
builder.Services.AddSingleton<IReadinessDependency, CryptographicReadinessDependency>();
builder.Services.AddSingleton<IReadinessDependency, MalwareScannerReadinessDependency>();
builder.Services.AddSingleton<IReadinessDependency, NotificationReadinessDependency>();
builder.Services.AddSingleton<CachedReadinessProbe>();
builder.Services.AddHealthChecks()
    .AddCheck<DependencyReadinessHealthCheck>("dependencies", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers["X-Correlation-ID"] = context.TraceIdentifier;
        headers["Referrer-Policy"] = "no-referrer";
        headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=(), usb=()");
        var isApiRequest = context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments("/device", StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments("/_health", StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase);
        headers.Append(
            "Content-Security-Policy",
            isApiRequest
                ? "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'"
                : "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'");
        headers.CacheControl = context.Request.Path.StartsWithSegments("/assets", StringComparison.OrdinalIgnoreCase) &&
            context.Response.StatusCode == StatusCodes.Status200OK
                ? "public,max-age=31536000,immutable"
                : "no-store";
        return Task.CompletedTask;
    });

    await next(context);
});

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<SessionValidationMiddleware>();
app.UseMiddleware<DeviceCertificateAuthenticationMiddleware>();
app.UseAuthorization();
app.UseMiddleware<TenantTransactionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
}

app.MapFallbackToFile("index.html").AllowAnonymous();

app.MapHealthChecks("/_health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = static async (context, _) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"status\":\"healthy\"}");
    }
}).AllowAnonymous();

app.MapHealthChecks("/_health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = static async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var status = report.Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy
            ? "healthy"
            : "unavailable";
        await context.Response.WriteAsync($"{{\"status\":\"{status}\"}}");
    }
}).AllowAnonymous();

app.MapControllers();

app.Run();

public partial class Program;
