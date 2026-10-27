using System;
using System.Collections.Generic;
using System.Linq;
using Requestarr.Core.Requests;

namespace Requestarr.Core.Anime
{
    public enum AnimePriority
    {
        // A dub request switches a sub series to dub; a sub request for a dub series follows it.
        Dub,

        // The reverse.
        Sub,

        // Whatever the series was first requested as stays; later requests follow.
        First
    }

    // What Requestarr creates in Sonarr for anime, and the release terms it starts from. Every name
    // starts with "requestarr" so it is clearly Requestarr's and never mistaken for the owner's.
    public static class AnimeDefaults
    {
        public const string DubTag = "requestarr-dub";
        public const string SubTag = "requestarr-sub";
        public const string DubProfile = "Requestarr: anime dub";
        public const string SubProfile = "Requestarr: anime sub";

        // Dub releases say so in the name: English-dubbed ("Dubbed", "English Dub", "[Dub]") or carrying
        // both tracks ("Dual Audio" / "Dual-Audio", "Multi-Audio", which include English). A dub series
        // must match one of these, so a Japanese-only release is never picked for it.
        public static readonly IReadOnlyList<string> DubRequired = new[]
        {
            "Dual Audio", "Dual-Audio", "DualAudio", "Multi-Audio", "Multi Audio", "Dubbed", "English Dub", "Eng Dub", "Eng-Dub", "EN Dub", "[Dub]", "(Dub)"
        };

        public static readonly IReadOnlyList<string> DubIgnored = Array.Empty<string>();

        public static readonly IReadOnlyList<string> SubRequired = Array.Empty<string>();

        // Sub releases rarely label themselves (most fansub and simulcast groups never write "sub"), so
        // the sub profile works the other way round: it refuses English-dub-only releases and accepts
        // everything else, including dual-audio, which still has the original audio and subtitles.
        public static readonly IReadOnlyList<string> SubIgnored = new[]
        {
            "Dubbed", "English Dub", "Eng Dub", "Eng-Dub", "EN Dub", "[Dub]", "(Dub)"
        };

        public static AnimePriority ParsePriority(string value) => value?.Trim().ToLowerInvariant() switch
        {
            "sub" => AnimePriority.Sub,
            "first" => AnimePriority.First,
            _ => AnimePriority.Dub
        };

        public static string PriorityKey(AnimePriority priority) => priority.ToString().ToLowerInvariant();

        // Which language a series ends up in when it is one language and a request asks for the other.
        public static AnimeLanguage Winner(AnimePriority priority, AnimeLanguage existing) => priority switch
        {
            AnimePriority.Dub => AnimeLanguage.Dub,
            AnimePriority.Sub => AnimeLanguage.Sub,
            _ => existing
        };

        // The language a request gets when nothing asked (buttons from before sub/dub existed).
        public static AnimeLanguage Default(AnimePriority priority, AnimeLanguage? existing) =>
            existing ?? (priority == AnimePriority.Sub ? AnimeLanguage.Sub : AnimeLanguage.Dub);

        public static string Key(AnimeLanguage language) => language == AnimeLanguage.Dub ? "d" : "s";

        public static AnimeLanguage? ParseKey(string key) => key switch
        {
            "d" => AnimeLanguage.Dub,
            "s" => AnimeLanguage.Sub,
            _ => null
        };

        public static List<string> Clean(IEnumerable<string> terms, IReadOnlyList<string> fallback) =>
            (terms ?? fallback).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
