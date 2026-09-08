using System.Security.Claims;
using VitrinYa.Models;

namespace VitrinYa.Services;

// Scoped identity supplied by authentication, never by posted form fields.
public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    private ClaimsPrincipal Principal => accessor.HttpContext?.User ?? new ClaimsPrincipal();

    public string? Id => Principal.FindFirstValue(ClaimTypes.NameIdentifier);

    public string RequiredId => Id ?? throw new InvalidOperationException("Authentication is required.");

    public bool IsInRole(string role) => Principal.IsInRole(role);

    public bool IsReviewer => IsInRole(Roles.Admin) || IsInRole(Roles.Editor);
}
