param(
    [Parameter(Mandatory = $true)]
    [uri] $ApiBaseUri,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[^@\s]+@[^@\s]+\.[^@\s]+$')]
    [string] $Email,

    [Parameter(Mandatory = $true)]
    [ValidateLength(1, 160)]
    [string] $DisplayName,

    [securestring] $Password,

    [securestring] $BootstrapToken
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Net.Http

if ($null -eq $Password) {
    $Password = Read-Host 'Initial platform administrator password' -AsSecureString
}

if ($null -eq $BootstrapToken) {
    $BootstrapToken = Read-Host 'One-time platform bootstrap token' -AsSecureString
}

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.CookieContainer = [System.Net.CookieContainer]::new()
$client = [System.Net.Http.HttpClient]::new($handler)
try {
    $baseUri = $ApiBaseUri.AbsoluteUri.TrimEnd('/')
    $sessionResponse = $client.GetAsync("$baseUri/api/v1/session").GetAwaiter().GetResult()
    try {
        $sessionResponse.EnsureSuccessStatusCode() | Out-Null
        $session = $sessionResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    }
    finally {
        $sessionResponse.Dispose()
    }

    $plainPassword = [System.Net.NetworkCredential]::new('', $Password).Password
    $plainBootstrapToken = [System.Net.NetworkCredential]::new('', $BootstrapToken).Password
    try {
        $payload = @{
            email = $Email
            displayName = $DisplayName
            password = $plainPassword
        } | ConvertTo-Json -Compress
        $request = [System.Net.Http.HttpRequestMessage]::new(
            [System.Net.Http.HttpMethod]::Post,
            "$baseUri/api/v1/bootstrap/platform-administrator")
        try {
            $request.Headers.Add('X-CSRF-TOKEN', [string] $session.csrfToken)
            $request.Headers.Add('X-Platform-Bootstrap-Token', $plainBootstrapToken)
            $request.Content = [System.Net.Http.StringContent]::new(
                $payload,
                [System.Text.Encoding]::UTF8,
                'application/json')
            $response = $client.SendAsync($request).GetAwaiter().GetResult()
            try {
                if (-not $response.IsSuccessStatusCode) {
                    $problem = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    throw "Platform bootstrap failed with HTTP $([int] $response.StatusCode): $problem"
                }
            }
            finally {
                $response.Dispose()
            }
        }
        finally {
            $request.Dispose()
        }
    }
    finally {
        $plainPassword = $null
        $plainBootstrapToken = $null
    }
}
finally {
    $client.Dispose()
    $handler.Dispose()
}

if ($session.mfaRequired) {
    Write-Host 'Platform administrator created. The account must enroll TOTP at first sign-in.'
}
else {
    Write-Host 'Platform administrator created. MFA is disabled for this Development field-test profile.'
}
