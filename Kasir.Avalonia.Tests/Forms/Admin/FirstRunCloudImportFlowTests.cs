using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kasir.Avalonia.Forms;
using Kasir.Avalonia.Forms.Admin;
using Kasir.Data;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Kasir.Avalonia.Tests.Forms.Admin;

// Owner report (v2.10.1, #13): after "Daftarkan dari Cloud" → download → "Masuk ke
// aplikasi", the first-run choice menu came back (clickable) while the downloaded
// database was being put in place, then the login appeared. It must show a busy
// "Menyiapkan data toko…" screen instead, never the choices.
[TestFixture]
[NonParallelizable]
public class FirstRunCloudImportFlowTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private string _staging = "";

    [SetUp]
    public void SetUp()
    {
        ResetDbConnection();
        // A real, valid database standing in for the downloaded cloud snapshot.
        DbConnection.FirstRunHandler = () => new FirstRunResult { Choice = "seed" };
        DbConnection.InitializeDatabase();
        ResetDbConnection(deleteData: false);
        _staging = Path.Combine(Path.GetTempPath(), "kasir-staging-" + Guid.NewGuid().ToString("N") + ".db");
        File.Copy(Path.Combine(DataDir, "kasir.db"), _staging);
        ResetDbConnection();
    }

    [TearDown]
    public void TearDown()
    {
        ResetDbConnection();
        try { File.Delete(_staging); } catch { }
    }

    private static void ResetDbConnection(bool deleteData = true)
    {
        DbConnection.CloseConnection();
        SqliteConnection.ClearAllPools();
        typeof(DbConnection).GetProperty(nameof(DbConnection.IsInitialized))!.SetValue(null, false);
        typeof(DbConnection).GetField("_uiThreadId", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, 0);
        DbConnection.FirstRunHandler = null;
        if (deleteData && Directory.Exists(DataDir)) Directory.Delete(DataDir, true);
    }

    private static string AllText(Control root) =>
        string.Join(" ", root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

    [AvaloniaTest]
    public void CloudImportDone_ShowsBusyScreen_NeverTheChoiceMenu_ThenLogin()
    {
        var window = new ShellWindow();
        var content = window.FindControl<ContentControl>("ContentArea")!;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var firstRun = content.Content as FirstRunView;
        Assert.That(firstRun, Is.Not.Null, "precondition: fresh install shows the first-run choices");

        firstRun!.FindControl<Button>("BtnCloud")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var cloud = content.Content as CloudImportView;
        Assert.That(cloud, Is.Not.Null, "precondition: cloud pairing screen is shown");

        var shown = new List<object?>();
        string? busyText = null;
        content.PropertyChanged += (_, e) =>
        {
            if (e.Property != ContentControl.ContentProperty) return;
            shown.Add(e.NewValue);
            if (e.NewValue is Control c && c is not LoginView)
            {
                Dispatcher.UIThread.RunJobs();
                busyText ??= AllText(c);
            }
        };

        // Download finished and the operator pressed "Masuk ke aplikasi".
        var tcs = (TaskCompletionSource<FirstRunResult?>)typeof(CloudImportView)
            .GetField("_tcs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cloud)!;
        tcs.TrySetResult(new FirstRunResult { Choice = "import", ImportPath = _staging });

        var sw = Stopwatch.StartNew();
        while (content.Content is not LoginView && sw.Elapsed < TimeSpan.FromSeconds(20))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }

        Assert.That(content.Content, Is.InstanceOf<LoginView>(), "login appears once the data is ready");
        Assert.That(shown.OfType<FirstRunView>(), Is.Empty,
            "the first-run choice menu must not come back after the import finished");
        Assert.That(shown.Count, Is.GreaterThanOrEqualTo(2), "a busy screen is shown before the login");
        Assert.That(shown[0], Is.Not.InstanceOf<LoginView>());
        Assert.That(((Control)shown[0]!).GetVisualDescendants().OfType<Button>(), Is.Empty,
            "the busy screen offers no buttons to click");
        Assert.That(busyText, Does.Contain("Menyiapkan data toko"));
    }
}
