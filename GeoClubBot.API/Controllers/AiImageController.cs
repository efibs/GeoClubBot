using Infrastructure.OutputAdapters.AI.ImageRelay;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UseCases.OutputPorts.AI;

namespace GeoClubBot.Controllers;

/// <summary>
/// Serves guide images that were copied during indexing because their own host refuses unattended
/// clients.
///
/// Anonymous by necessity: the fetchers are the AI provider and Discord's embed renderer, neither of
/// which can carry a credential. Two properties keep that safe. It serves only bytes already on disk —
/// there is no path that fetches a URL on request, which would turn this into an open proxy into the
/// bot's network. And it is addressed purely by content hash, so there is nothing to enumerate and no
/// caller-supplied text ever reaches a file path.
/// </summary>
[ApiController]
[Route(FileSystemImageRelay.RoutePrefix)]
[AllowAnonymous]
public class AiImageController(IImageRelay imageRelay) : ControllerBase
{
    /// <summary>Content is immutable — the name is its hash — so it can be cached indefinitely.</summary>
    private const int CacheSeconds = 31_536_000;

    [HttpGet("{name}")]
    [EnableRateLimiting(RateLimitPolicies.AiImageRelay)]
    public async Task<IActionResult> GetAsync(string name, CancellationToken cancellationToken)
    {
        var image = await imageRelay.ReadAsync(name, cancellationToken).ConfigureAwait(false);
        if (image is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = $"public, max-age={CacheSeconds}, immutable";

        // The declared type comes from the allowlist the bytes were stored under, never from the
        // request, so a caller cannot influence how a browser interprets the response.
        return File(image.Content, image.ContentType);
    }
}
