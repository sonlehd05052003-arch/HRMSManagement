using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace HRManagement.Authorization
{
    /// <summary>
    /// Per-IP limits on the anonymous auth endpoints, against password guessing, OTP guessing and email spam.
    /// Apply with [EnableRateLimiting(RateLimitPolicies.Login)] and friends.
    /// </summary>
    public static class RateLimitPolicies
    {
        public const string Login = "login";
        public const string PasswordReset = "password-reset";

        public static IServiceCollection AddAuthRateLimits(this IServiceCollection services)
        {
            return services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = async (context, cancellationToken) =>
                {
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                        context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();

                    await context.HttpContext.Response.WriteAsJsonAsync(
                        new { message = "Bạn thao tác quá nhiều lần. Vui lòng thử lại sau ít phút." }, cancellationToken);
                };

                options.AddPolicy(Login, http => PerClientIp(http, permitLimit: 10, TimeSpan.FromMinutes(1)));
                // Covers both requesting an OTP (sends an email) and submitting one
                options.AddPolicy(PasswordReset, http => PerClientIp(http, permitLimit: 5, TimeSpan.FromMinutes(15)));
            });
        }

        // RemoteIpAddress is the proxy's address when the API sits behind one, unless forwarded headers are configured
        private static RateLimitPartition<string> PerClientIp(HttpContext http, int permitLimit, TimeSpan window) =>
            RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = window,
                    QueueLimit = 0
                });
    }
}
