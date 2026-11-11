using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EyeBreak;

/// <summary>
/// Searches YouTube for music in infinite mode. Reads the normal results page (no API key needed) and returns
/// plain text results only: title, channel and length. No thumbnails.
/// </summary>
public static class YouTubeSearch
{
    public record Result(string id, string title, string channel, string duration);

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        h.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        return h;
    }

    public static async Task<Result[]> SearchAsync(string query, int max = 8)
    {
        try
        {
            // sp=EgIQAQ%3D%3D limits results to videos (no channels, playlists or shorts shelves)
            string url = "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query) + "&sp=EgIQAQ%253D%253D";
            string html = await Http.GetStringAsync(url);
            var m = Regex.Match(html, @"var ytInitialData = (\{.*?\});</script>", RegexOptions.Singleline);
            if (!m.Success) return Array.Empty<Result>();

            using var doc = JsonDocument.Parse(m.Groups[1].Value);
            var results = new List<Result>();
            Walk(doc.RootElement, results, max);
            return results.ToArray();
        }
        catch (Exception ex)
        {
            GammaDimmer.Log("search failed: " + ex.Message);
            return Array.Empty<Result>();
        }
    }

    // Finds every "videoRenderer" object anywhere in YouTube's page data, in order.
    static void Walk(JsonElement e, List<Result> into, int max)
    {
        if (into.Count >= max) return;
        if (e.ValueKind == JsonValueKind.Object)
        {
            if (e.TryGetProperty("videoRenderer", out var v) && v.TryGetProperty("videoId", out var id))
            {
                string title = Runs(v, "title");
                string channel = Runs(v, "ownerText");
                string dur = v.TryGetProperty("lengthText", out var lt) && lt.TryGetProperty("simpleText", out var st) ? st.GetString() ?? "" : "Live";
                if (!string.IsNullOrEmpty(title)) into.Add(new(id.GetString()!, title, channel, dur));
                return;
            }
            foreach (var p in e.EnumerateObject()) Walk(p.Value, into, max);
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in e.EnumerateArray()) Walk(x, into, max);
        }
    }

    static string Runs(JsonElement v, string name) =>
        v.TryGetProperty(name, out var t) && t.TryGetProperty("runs", out var runs)
            ? string.Concat(runs.EnumerateArray().Select(r => r.TryGetProperty("text", out var x) ? x.GetString() : ""))
            : "";
}
