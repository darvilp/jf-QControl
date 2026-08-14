using System;
using System.IO;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Api;

public sealed class AdministratorPageContractTests
{
    [Fact]
    public void PageLoadsThinControllerAndExposesAccessibleSections()
    {
        var html = ReadResource("Jellyfin.Plugin.QControl.Configuration.configPage.html");

        Assert.Contains("data-controller=\"__plugin/QControl.js\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"qControlConnection\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"qControlProtection\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"qControlOperationalStatus\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"qControlRecovery\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", html, StringComparison.Ordinal);
        Assert.Contains("<dialog", html, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 52rem)", html, StringComparison.Ordinal);
    }

    [Fact]
    public void PageUsesNativeKeyboardControlsAndNeverEmbedsCredentialContent()
    {
        var html = ReadResource("Jellyfin.Plugin.QControl.Configuration.configPage.html");

        Assert.DoesNotContain("value=\"qbt_", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("type=\"password\"", html, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"new-password\"", html, StringComparison.Ordinal);
        Assert.Contains("data-action=\"test-connection\"", html, StringComparison.Ordinal);
        Assert.Contains("data-action=\"save-configuration\"", html, StringComparison.Ordinal);
        Assert.Contains("data-action=\"resume-marked\"", html, StringComparison.Ordinal);
        Assert.Contains("data-action=\"restore-speed\"", html, StringComparison.Ordinal);
        Assert.Contains("data-action=\"mark-resolved\"", html, StringComparison.Ordinal);
    }

    private static string ReadResource(string name)
    {
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(name);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
