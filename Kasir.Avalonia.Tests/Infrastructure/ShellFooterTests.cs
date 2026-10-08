using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.LogicalTree;
using Kasir.Avalonia.Infrastructure;
using NUnit.Framework;

namespace Kasir.Avalonia.Tests.Infrastructure;

// F10: the footer showed a hardcoded "Online · Sync 0d lalu" badge driven by a
// SyncStatusModel nothing ever updated (the LAN SMB SyncEngine is never started).
// The operators trust the footer, so every badge must come from real state.
[TestFixture]
public class ShellFooterTests
{
    private static string[] FooterTexts(ShellWindow window) =>
        window.GetLogicalDescendants()
              .OfType<TextBlock>()
              .Select(t => t.Text ?? string.Empty)
              .ToArray();

    [AvaloniaTest]
    public void Footer_HasNoHardcodedLanSyncBadge()
    {
        var window = new ShellWindow();
        window.Show();

        var texts = FooterTexts(window);

        Assert.That(texts, Has.None.Contains("Sync 0d lalu"));
        Assert.That(texts, Has.None.StartsWith("Online"));
        Assert.That(window.FindControl<Border>("SyncBadge"), Is.Null,
            "LAN sync badge has no data source and must not be shown");
    }

    [AvaloniaTest]
    public void DeadLanSyncStatusModel_IsRemoved()
    {
        var type = typeof(ShellWindow).Assembly.GetType("Kasir.Avalonia.Infrastructure.SyncStatusModel");
        Assert.That(type, Is.Null, "SyncStatusModel had no producer; it only fed the fake badge");
    }

    [AvaloniaTest]
    public void CloudBadge_ShowsRealCloudSyncState()
    {
        var window = new ShellWindow();
        window.Show();

        var cloudText = window.FindControl<TextBlock>("CloudText");
        Assert.That(cloudText, Is.Not.Null);
        Assert.That(cloudText!.Text, Is.EqualTo(CloudSyncStatusModel.Current.DisplayText));
    }
}
