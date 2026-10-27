// Purpose: Submits forms the way a browser would: loads the page to get its antiforgery token,
// then posts fields (or files) with that token.
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

    public static async Task<HttpResponseMessage> PostFilesAsync(HttpClient client, string pageUrl, string postUrl,
        params (string FileName, byte[] Bytes)[] files)
    {
        var token = await GetAntiforgeryTokenAsync(client, pageUrl);
        using var content = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        foreach (var (fileName, bytes) in files)
        {
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(part, "files", fileName);
        }

        return await client.PostAsync(postUrl, content);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}
