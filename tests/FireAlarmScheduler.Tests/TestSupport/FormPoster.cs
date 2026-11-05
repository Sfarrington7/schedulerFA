// Purpose: Submits forms the way a browser would: loads the page to get its antiforgery token,
// then posts fields with that token (optionally asking for JSON, as the checklist script does).
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace FireAlarmScheduler.Tests.TestSupport;

public static partial class FormPoster
{
    public static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string pageUrl)
    {
        var html = await client.GetStringAsync(pageUrl);
        return TokenPattern().Match(html) is { Success: true } match
            ? match.Groups[1].Value
            : throw new InvalidOperationException($"No antiforgery token found on {pageUrl}.");
    }

    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string pageUrl, string postUrl,
        params (string Name, string Value)[] fields)
    {
        var token = await GetAntiforgeryTokenAsync(client, pageUrl);
        var content = new FormUrlEncodedContent(
            fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Value))
                  .Append(new("__RequestVerificationToken", token)));
        return await client.PostAsync(postUrl, content);
    }

    /// <summary>Posts form fields the way the building checklist script does, asking for a JSON answer.</summary>
    public static async Task<HttpResponseMessage> PostFormForJsonAsync(HttpClient client, string pageUrl, string postUrl,
        params (string Name, string Value)[] fields)
    {
        var token = await GetAntiforgeryTokenAsync(client, pageUrl);
        using var request = new HttpRequestMessage(HttpMethod.Post, postUrl)
        {
            Content = new FormUrlEncodedContent(
                fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Value))
                      .Append(new("__RequestVerificationToken", token)))
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await client.SendAsync(request);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}
