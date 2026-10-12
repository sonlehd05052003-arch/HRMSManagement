using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace HRManagement.Authorization
{
    /// <summary>
    /// Issues the access JWT and renews it while the user is active (sliding session): once a token is past
    /// half its lifetime, the next request gets a fresh one, but never beyond <see cref="MaxSession"/> after
    /// the original sign-in, so an idle tab that keeps pinging cannot stay signed in forever.
    /// Tokens are still checked against the database on every request, so renewing them does not weaken revocation.
    /// </summary>
    public class JwtTokenIssuer
    {
        /// <summary>Unix time of the original sign-in, carried over by every renewed token.</summary>
        public const string SessionStartClaim = "session_start";

        // Registered claims that the handler sets itself; they must not be copied into a renewed token
        private static readonly HashSet<string> RegisteredClaims = new(StringComparer.Ordinal)
        {
            JwtRegisteredClaimNames.Exp, JwtRegisteredClaimNames.Nbf, JwtRegisteredClaimNames.Iat,
            JwtRegisteredClaimNames.Iss, JwtRegisteredClaimNames.Aud, JwtRegisteredClaimNames.Jti,
            SessionStartClaim
        };

        private readonly IConfiguration _configuration;

        public JwtTokenIssuer(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>How long one token is valid without activity.</summary>
        public TimeSpan Lifetime => TimeSpan.FromMinutes(_configuration.GetValue("Jwt:LifetimeMinutes", 30));

        /// <summary>Hard limit after sign-in; past it the user must sign in again however active they are.</summary>
        public TimeSpan MaxSession => TimeSpan.FromHours(_configuration.GetValue("Jwt:MaxSessionHours", 8));

        public string Issue(IEnumerable<Claim> claims, DateTime sessionStartUtc)
        {
            var now = DateTime.UtcNow;
            var sessionEnd = sessionStartUtc + MaxSession;
            var expires = now + Lifetime < sessionEnd ? now + Lifetime : sessionEnd;

            var subject = new ClaimsIdentity(claims.Where(c => !RegisteredClaims.Contains(c.Type)));
            subject.AddClaim(new Claim(SessionStartClaim,
                new DateTimeOffset(sessionStartUtc).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64));

            var descriptor = new SecurityTokenDescriptor
            {
                Subject = subject,
                NotBefore = now,
                Expires = expires,
                Issuer = _configuration["Jwt:Issuer"],
                Audience = _configuration["Jwt:Audience"],
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!)),
                    SecurityAlgorithms.HmacSha256Signature)
            };
            var handler = new JwtSecurityTokenHandler();
            return handler.WriteToken(handler.CreateToken(descriptor));
        }

        /// <summary>
        /// A renewed token when <paramref name="current"/> is past half its lifetime and the session has time left;
        /// null when no renewal is due.
        /// </summary>
        public (string Token, DateTime ExpiresUtc)? TryRenew(ClaimsPrincipal principal, SecurityToken current)
        {
            var now = DateTime.UtcNow;
            var lifetime = current.ValidTo - current.ValidFrom;
            if (current.ValidTo - now > lifetime / 2)
                return null;

            // Tokens issued before sliding sessions have no start claim: their issue time is the sign-in
            var sessionStart = long.TryParse(principal.FindFirst(SessionStartClaim)?.Value, out var unix)
                ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime
                : current.ValidFrom;

            // Out of session time: let the current token run out instead of issuing an ever-shorter one
            if (sessionStart + MaxSession <= current.ValidTo)
                return null;

            var token = Issue(principal.Claims, sessionStart);
            var expires = new JwtSecurityTokenHandler().ReadJwtToken(token).ValidTo;
            return (token, expires);
        }
    }
}
