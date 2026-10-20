using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Requestarr.Core.Availability;

namespace Requestarr.Http.Webhooks
{
    // Sonarr's and Radarr's webhook connection that Requestarr creates on each instance. Sonarr and
    // Radarr can't send Requestarr's API key, so each instance's URL carries its own token instead.
    // The body only says which title changed; Requestarr then asks the instance itself.
    public class WebhookController : Controller
    {
        private const int MaxBodyLength = 512 * 1024;

        private readonly IAvailabilityService _availability;

        public WebhookController(IAvailabilityService availability)
        {
            _availability = availability;
        }

        [AllowAnonymous]
        [HttpPost("/" + WebhookTokens.Path + "/{backendId:int}")]
        public async Task<IActionResult> Receive(int backendId, [FromQuery] string token)
        {
            JObject payload;

            try
            {
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();
                payload = body.Length is > 0 and <= MaxBodyLength ? JObject.Parse(body) : new JObject();
            }
            catch (JsonException)
            {
                return BadRequest();
            }

            var outcome = await _availability.HandleWebhookAsync(
                backendId,
                token,
                (string)payload["eventType"],
                (int?)payload["series"]?["tvdbId"],
                (int?)payload["movie"]?["tmdbId"] ?? (int?)payload["remoteMovie"]?["tmdbId"]);

            return outcome switch
            {
                WebhookOutcome.Unauthorized => StatusCode(StatusCodes.Status401Unauthorized),
                WebhookOutcome.UnknownInstance => NotFound(),
                _ => Ok()
            };
        }
    }
}
