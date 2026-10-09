namespace HRManagement.Authorization
{
    /// <summary>
    /// The JWT lives in an HttpOnly cookie instead of browser storage, so scripts injected into the
    /// client (XSS) cannot read it. SameSite=Strict keeps other sites from sending it (CSRF); the client
    /// and API share a site (app./api.peoplecore.tech, or the Vite dev proxy), so it is still sent to the API.
    /// </summary>
    public static class AuthCookie
    {
        public const string Name = "hrms_access_token";

        public static void Append(HttpContext http, string token, DateTime expiresUtc)
        {
            http.Response.Cookies.Append(Name, token, BuildOptions(http, expiresUtc));
        }

        public static void Delete(HttpContext http)
        {
            http.Response.Cookies.Delete(Name, BuildOptions(http, null));
        }

        private static CookieOptions BuildOptions(HttpContext http, DateTime? expiresUtc)
        {
            var env = http.RequestServices.GetRequiredService<IWebHostEnvironment>();
            return new CookieOptions
            {
                HttpOnly = true,
                // Outside development the API is only served over HTTPS (possibly behind a TLS-terminating proxy)
                Secure = http.Request.IsHttps || !env.IsDevelopment(),
                SameSite = SameSiteMode.Strict,
                Path = "/",
                Expires = expiresUtc
            };
        }
    }
}
