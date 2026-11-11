using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EyeBreak;

/// <summary>Music presets and quotes for infinite mode.</summary>
public static class Infinite
{
    public record Preset(string Key, string Name, string[] VideoIds);

    /// <summary>Each preset lists backups: some videos can't be embedded, and the player then tries the next.</summary>
    public static readonly Preset[] Presets =
    {
        new("interstellar", "Interstellar", new[] { "UDVtMYqUAyw", "4VXErA63_eg", "5gO0xpY_Y3E", "EGcXF0iG-2s" }),
        new("space", "Deep space", new[] { "02sBiefk0nY", "qaFrEIcTokM", "EQu5PUC1v-M", "nnKjCrQcHN8" }),
        new("rain", "Rain & piano", new[] { "o8GrqUSdzi0", "9dKGUodhCvI", "jX6kn9_U8qk" }),
        new("none", "Silence", Array.Empty<string>()),
    };

    /// <summary>
    /// The stations the music bar steps through (presets, then your link if you have one), and which one to start on.
    /// </summary>
    public static (object[] stations, int start) StationsFor(Settings s)
    {
        var list = Presets.Where(p => p.Key != "none")
            .Select(p => (key: p.Key, st: (object)new { name = p.Name, music = p.VideoIds.Select(id => (object)new { id }).ToArray() }))
            .ToList();
        if (ParseYouTube(s.CustomMusic) is { } mine)
            list.Add(("custom", new { name = "Your link", music = new[] { mine } }));
        list.Add(("none", new { name = "Silence", music = Array.Empty<object>() }));
        int start = Math.Max(0, list.FindIndex(x => x.key == s.Music));
        return (list.Select(x => x.st).ToArray(), start);
    }

    /// <summary>What the page's music player should try, in order: [{id}] or [{list}] for a playlist.</summary>
    public static object[] MusicFor(Settings s)
    {
        if (s.Music == "custom")
        {
            var item = ParseYouTube(s.CustomMusic);
            return item == null ? Array.Empty<object>() : new[] { item };
        }
        var preset = Presets.FirstOrDefault(p => p.Key == s.Music) ?? Presets[0];
        return preset.VideoIds.Select(id => (object)new { id }).ToArray();
    }

    /// <summary>Accepts a YouTube link (watch, youtu.be, shorts, embed, music, playlist) or a bare video id.</summary>
    public static object? ParseYouTube(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        var list = Regex.Match(text, @"[?&]list=([A-Za-z0-9_-]+)");
        var id = Regex.Match(text, @"(?:v=|youtu\.be/|shorts/|embed/|live/)([A-Za-z0-9_-]{11})");
        if (id.Success) return new { id = id.Groups[1].Value };
        if (list.Success) return new { list = list.Groups[1].Value };
        if (Regex.IsMatch(text, @"^[A-Za-z0-9_-]{11}$")) return new { id = text };
        return null;
    }

    public record Quote(string q, string a);

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>
    /// The quotes shown in infinite mode: our own hand-picked calm list, or (if chosen) a fresh batch from
    /// ZenQuotes, keeping the calmer ones. Falls back to our list if ZenQuotes can't be reached.
    /// </summary>
    public static async Task<(Quote[] quotes, string credit)> GetQuotesAsync(string source)
    {
        if (source != "zen") return (Fallback, "");
        try
        {
            var json = await Http.GetStringAsync("https://zenquotes.io/api/quotes");
            var all = JsonSerializer.Deserialize<Quote[]>(json)?
                .Where(x => !string.IsNullOrWhiteSpace(x.q) && x.q.Length <= 180 && !x.q.Contains("Too many requests"))
                .ToArray() ?? Array.Empty<Quote>();
            var calm = all.Where(x => Calming.IsMatch(x.q)).ToArray();
            var pick = calm.Length >= 8 ? calm : all;
            if (pick.Length > 0)
                return (pick, "Inspirational quotes provided by ZenQuotes API");   // ZenQuotes asks for this credit
        }
        catch (Exception ex) { GammaDimmer.Log("zenquotes failed: " + ex.Message); }
        return (Fallback, "");
    }

    static readonly Regex Calming = new(
        @"\b(peace\w*|calm\w*|breath\w*|still\w*|rest|quiet\w*|silen\w*|present|moment|nature|gentle|slow\w*|patien\w*|" +
        @"soul|heart|joy\w*|grati\w*|serene|serenity|sky|skies|star\w*|universe|ocean|sea|river|light|tranquil\w*|ease|relax\w*|" +
        @"mind|happiness|kind\w*|wonder|simpl\w*|let go|inner|within)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Quote[] Fallback =
    {
        new("Nature does not hurry, yet everything is accomplished.", "Lao Tzu"),
        new("Quiet the mind, and the soul will speak.", "Ma Jaya Sati Bhagavati"),
        new("Almost everything will work again if you unplug it for a few minutes, including you.", "Anne Lamott"),
        new("The quieter you become, the more you can hear.", "Ram Dass"),
        new("Look deep into nature, and then you will understand everything better.", "Albert Einstein"),
        new("Rest is not idleness.", "John Lubbock"),
        new("Within you there is a stillness and a sanctuary to which you can retreat at any time.", "Hermann Hesse"),
        new("Breathe. Let go. And remind yourself that this very moment is the only one you know you have for sure.", "Oprah Winfrey"),
        new("We are made of star-stuff.", "Carl Sagan"),
        new("The present moment is filled with joy and happiness. If you are attentive, you will see it.", "Thich Nhat Hanh"),
        new("Peace comes from within. Do not seek it without.", "Buddha"),
        new("Smile, breathe, and go slowly.", "Thich Nhat Hanh"),
        new("In the midst of movement and chaos, keep stillness inside of you.", "Deepak Chopra"),
        new("You are the universe expressing itself as a human for a little while.", "Eckhart Tolle"),
        new("Adopt the pace of nature: her secret is patience.", "Ralph Waldo Emerson"),
        new("Feelings come and go like clouds in a windy sky. Conscious breathing is my anchor.", "Thich Nhat Hanh"),
        new("Silence is a source of great strength.", "Lao Tzu"),
        new("Be still, and the earth will speak to you.", "Navajo proverb"),
        new("The sky is everywhere, it begins at your feet.", "Jandy Nelson"),
        new("Look at the stars. See their beauty. And in that beauty, see yourself.", "Draya Mooney"),
        new("Tension is who you think you should be. Relaxation is who you are.", "Chinese proverb"),
        new("Slow down and everything you are chasing will come around and catch you.", "John De Paola"),
        new("Your calm mind is the ultimate weapon against your challenges.", "Bryant McGill"),
        new("There is more to life than increasing its speed.", "Mahatma Gandhi"),
        new("Wherever you are, be all there.", "Jim Elliot"),
        new("The cosmos is within us. We are a way for the universe to know itself.", "Carl Sagan"),
        new("Muddy water is best cleared by leaving it alone.", "Alan Watts"),
        new("Let go of the thoughts that don't make you strong.", "Karen Salmansohn"),
        new("Just breathe.", ""),
        new("Close your eyes. Let the world wait a moment.", ""),
    };
}
