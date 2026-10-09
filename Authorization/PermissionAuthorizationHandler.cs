using System.Security.Claims;
using HRManagement.Services.Tenants;
using Microsoft.AspNetCore.Authorization;

namespace HRManagement.Authorization
{
    public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly IPermissionCache _permissionCache;

        public PermissionAuthorizationHandler(IPermissionCache permissionCache)
        {
            _permissionCache = permissionCache;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            // The SuperAdmin holds no company permissions, but has full access to the configuration
            // endpoints opened to it with [SuperAdminCompanyScope]
            if (context.User.IsInRole(PlatformRoles.SuperAdmin)
                && context.Resource is HttpContext http
                && http.GetEndpoint()?.Metadata.GetMetadata<SuperAdminCompanyScopeAttribute>() != null)
            {
                context.Succeed(requirement);
                return;
            }

            var roleNames = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
            if (roleNames.Count == 0)
                return;

            if (!int.TryParse(context.User.FindFirst(TenantContext.ClaimType)?.Value, out var companyId))
                return;

            var granted = await _permissionCache.GetPermissionKeysAsync(companyId, roleNames);

            if (requirement.Keys.Any(granted.Contains))
                context.Succeed(requirement);
        }
    }
}
