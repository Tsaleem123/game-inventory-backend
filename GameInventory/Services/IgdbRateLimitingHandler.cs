namespace GameInventory.Services
{
    /// <summary>
    /// <see cref="DelegatingHandler"/> that paces outbound calls to the IGDB API
    /// using the shared <see cref="IgdbRateLimiter"/>. Attached to the named
    /// "igdb" <see cref="HttpClient"/> so every IGDB request — from any controller
    /// — is throttled automatically.
    ///
    /// Only requests to api.igdb.com are throttled; the Twitch token endpoint
    /// (a different host) passes through untouched.
    /// </summary>
    public class IgdbRateLimitingHandler : DelegatingHandler
    {
        private readonly IgdbRateLimiter _limiter;

        public IgdbRateLimitingHandler(IgdbRateLimiter limiter)
        {
            _limiter = limiter;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host == "api.igdb.com")
                await _limiter.WaitTurnAsync(cancellationToken);

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
