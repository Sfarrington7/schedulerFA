// Purpose: Tests the job files link rules: any http(s) website is accepted (Google Drive is only the common case),
// links pasted without a scheme get https://, and anything a browser could run or that isn't a web address is refused.
using FireAlarmScheduler.Services;

namespace FireAlarmScheduler.Tests.Services;

public class JobFilesLinkTests
{
    [Theory]
    [InlineData("https://drive.google.com/drive/folders/1AbC_dEf?usp=sharing")]
    [InlineData("https://docs.google.com/spreadsheets/d/1xyz/edit#gid=0")]
    [InlineData("https://contoso.sharepoint.com/:f:/s/FireAlarm/EabC123")]
    [InlineData("https://www.dropbox.com/scl/fo/abc/xyz?rlkey=1&dl=0")]
    [InlineData("http://fileserver/jobs/central-plaza")]
    [InlineData("HTTPS://Drive.Google.com/drive/folders/abc")]
    public void TryNormalize_AnyWebLink_AcceptedUnchanged(string link)
    {
        Assert.True(JobFilesLink.TryNormalize(link, out var normalized, out var error));
        Assert.Equal(link, normalized);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryNormalize_Blank_MeansNoLink(string? link)
    {
        Assert.True(JobFilesLink.TryNormalize(link, out var normalized, out _));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalize_SurroundingSpaces_Trimmed()
    {
        Assert.True(JobFilesLink.TryNormalize("  https://drive.google.com/x  ", out var normalized, out _));
        Assert.Equal("https://drive.google.com/x", normalized);
    }

    [Fact]
    public void TryNormalize_NoScheme_AddsHttps()
    {
        Assert.True(JobFilesLink.TryNormalize("drive.google.com/drive/folders/abc", out var normalized, out _));
        Assert.Equal("https://drive.google.com/drive/folders/abc", normalized);
    }

    [Theory]
    [InlineData("javascript:alert(document.cookie)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file:///C:/Users/lead/jobs")]
    [InlineData("ftp://files.example.com/job")]
    [InlineData("mailto:lead@example.test")]
    [InlineData("TBD")]
    [InlineData("see the shared drive")]
    [InlineData("https://")]
    public void TryNormalize_NotAWebLink_Refused(string link)
    {
        Assert.False(JobFilesLink.TryNormalize(link, out var normalized, out var error));
        Assert.Null(normalized);
        Assert.Equal(JobFilesLink.InvalidMessage, error);
    }

    [Fact]
    public void TryNormalize_TooLong_Refused()
    {
        var link = "https://drive.google.com/" + new string('a', JobFilesLink.MaxLength);

        Assert.False(JobFilesLink.TryNormalize(link, out _, out var error));
        Assert.Contains("2000 characters", error);
    }
}
