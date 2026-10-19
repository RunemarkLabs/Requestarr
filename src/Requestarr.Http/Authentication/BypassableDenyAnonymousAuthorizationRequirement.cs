using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Requestarr.Http.Authentication
{
    public class BypassableDenyAnonymousAuthorizationRequirement : DenyAnonymousAuthorizationRequirement
    {
    }
}
