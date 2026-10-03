using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Kasir.Avalonia.Forms.Help;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Data;
using NUnit.Framework;

namespace Kasir.Avalonia.Tests.Forms.Help;

// Bantuan must open (or explain why it can't) on every screen, including the
// first-run / cloud-import screens shown before any database exists. Opening it
// used to call DbConnection.GetConnection() there, which threw (no data\ folder)
// out of the key handler and killed the app.
[TestFixture]
public class BantuanOverlayHostTests
{
    private static string DbFile => Path.Combine(AppContext.BaseDirectory, "data", "kasir.db");

    [AvaloniaTest]
    public void Open_BeforeDatabaseIsInitialized_ShowsMessage_DoesNotThrow()
    {
        Assert.That(DbConnection.IsInitialized, Is.False, "test assumes first-run state");
        var window = new ShellWindow();
        window.Show();

        Assert.DoesNotThrow(() => BantuanOverlayHost.Current.Open(window));

        var host = window.FindControl<ContentControl>("OverlayHost")!;
        Assert.That(host.IsVisible, Is.True);
        Assert.That(host.Content, Is.InstanceOf<MsgBoxOverlay>());
    }

    [AvaloniaTest]
    public void Open_BeforeDatabaseIsInitialized_DoesNotCreateDatabaseFile()
    {
        bool existedBefore = File.Exists(DbFile);
        var window = new ShellWindow();
        window.Show();

        BantuanOverlayHost.Current.Open(window);

        // An empty kasir.db would make the next start skip first-run and fail validation.
        Assert.That(File.Exists(DbFile), Is.EqualTo(existedBefore));
    }

    [AvaloniaTest]
    public void CtrlSlash_BeforeDatabaseIsInitialized_DoesNotCrash()
    {
        var window = new ShellWindow();
        window.Show();

        Assert.DoesNotThrow(() => window.KeyPress(Key.Oem2, RawInputModifiers.Control, PhysicalKey.Slash, "/"));
        Assert.That(window.FindControl<ContentControl>("OverlayHost")!.IsVisible, Is.True);
    }

    [AvaloniaTest]
    public void BantuanHintBadge_IsClickable()
    {
        var window = new ShellWindow();
        var badge = window.FindControl<Border>("BantuanHintBadge")!;

        Assert.That(badge.Cursor, Is.Not.Null, "badge should show a hand cursor and open Bantuan on click");
    }
}
