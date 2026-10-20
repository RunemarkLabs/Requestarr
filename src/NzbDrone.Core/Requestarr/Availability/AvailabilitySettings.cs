using System;
using System.Security.Cryptography;
using System.Text;

namespace Requestarr.Core.Availability
{
    // How Requestarr learns that requests are downloading and available, from the provisioning
    // file: the address Sonarr and Radarr use to reach Requestarr (no address: polling only) and
    // how often the backup poll runs.
    public interface IAvailabilitySettingsStore
    {
        string WebhookUrl { get; }
        int PollMinutes { get; }
        void Set(string webhookUrl, int? pollMinutes);
    }

    public class AvailabilitySettingsStore : IAvailabilitySettingsStore
    {
        public const int DefaultPollMinutes = 15;
        public const int MinPollMinutes = 5;

        public string WebhookUrl { get; private set; }

        public int PollMinutes { get; private set; } = DefaultPollMinutes;

        public void Set(string webhookUrl, int? pollMinutes)
        {
            WebhookUrl = string.IsNullOrWhiteSpace(webhookUrl) ? null : webhookUrl.Trim().TrimEnd('/');
            PollMinutes = Math.Max(MinPollMinutes, pollMinutes ?? DefaultPollMinutes);
        }
    }

    // Each instance's webhook carries a token only that instance knows: derived from Requestarr's
    // API key and the instance, so nothing extra is stored and resetting the API key revokes it.
    public static class WebhookTokens
    {
        public const string Path = "api/v1/webhook";

        public static string For(string apiKey, int backendId)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiKey ?? string.Empty));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"requestarr-webhook:{backendId}"));

            return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
        }

        public static bool Matches(string apiKey, int backendId, string token) =>
            !string.IsNullOrEmpty(token) &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(For(apiKey, backendId)), Encoding.UTF8.GetBytes(token));

        public static string Url(string baseUrl, string apiKey, int backendId) => $"{baseUrl.TrimEnd('/')}/{Path}/{backendId}?token={For(apiKey, backendId)}";
    }
}
