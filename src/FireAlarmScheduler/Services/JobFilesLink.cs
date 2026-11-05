// Purpose: Checks the "job files" link a team lead pastes onto a job. Any website is allowed (Google Drive, SharePoint,
// Dropbox, an intranet share...), but it must be an http or https web address so it is safe to render as a link.
using System.Text.RegularExpressions;

namespace FireAlarmScheduler.Services;

public static partial class JobFilesLink
{
    public const int MaxLength = 2000;

    public const string InvalidMessage = "Enter a web link starting with https:// (or http://), such as a Google Drive share link.";

    /// <summary>
    /// Cleans up a pasted link: trims it, treats blank as "no link" (null), and puts "https://" in front of a link
    /// pasted without a scheme ("drive.google.com/..."). Returns false with a message for anything that isn't an
    /// http(s) address, which also keeps out javascript: and data: links that would run in the browser when clicked.
    /// </summary>
    public static bool TryNormalize(string? value, out string? link, out string? error)
    {
        link = null;
        error = null;
        var text = value?.Trim() ?? "";
        if (text.Length == 0)
        {
            return true;
        }

        var hasScheme = SchemePrefix().IsMatch(text);
        if (!hasScheme)
        {
            text = "https://" + text;
        }

        if (text.Length > MaxLength)
        {
            error = $"The job files link must be {MaxLength} characters or fewer.";
            return false;
        }

        var valid = Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && uri.Host.Length > 0
            // Without a scheme, plain words like "TBD" would otherwise become https://TBD.
            && (hasScheme || uri.Host.Contains('.'));
        if (!valid)
        {
            error = InvalidMessage;
            return false;
        }

        link = text;
        return true;
    }

    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9+.-]*:")]
    private static partial Regex SchemePrefix();
}
