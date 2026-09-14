using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DisplayControl.Api.Security;

public sealed class ApiAntiforgeryFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (SafeMethods.Contains(context.HttpContext.Request.Method) ||
            context.ActionDescriptor.EndpointMetadata.OfType<IgnoreAntiforgeryTokenAttribute>().Any())
        {
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Type = "https://docs.example.invalid/problems/antiforgery-validation",
                Title = "The request could not be accepted.",
                Status = StatusCodes.Status400BadRequest,
                Extensions = { ["code"] = "antiforgery_validation_failed" }
            })
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
        }
    }
}
