using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kasir.Avalonia.Diagnostics;
using NUnit.Framework;

namespace Kasir.Avalonia.Tests.Diagnostics;

// F08/F28: SaleView's scan handler was `async void` with no try/catch, so one
// SqliteException on the barcode path escaped and took the app down mid-sale.
// UiGuard is the wrapper every async void sale handler now runs through.
[TestFixture]
public class UiGuardTests
{
    private readonly List<(string Source, Exception Ex)> _logged = new();

    private void Log(string source, Exception ex) => _logged.Add((source, ex));

    [SetUp]
    public void SetUp() => _logged.Clear(); // NUnit reuses the fixture instance across tests

    [Test]
    public async Task RunAsync_SynchronousThrow_IsLoggedAndReported_NotRethrown()
    {
        Exception? reported = null;
        var boom = new InvalidOperationException("database is locked");

        await UiGuard.RunAsync("Sale.Scan", () => throw boom, ex => { reported = ex; return Task.CompletedTask; }, Log);

        Assert.That(reported, Is.SameAs(boom));
        Assert.That(_logged, Has.Count.EqualTo(1));
        Assert.That(_logged[0].Source, Is.EqualTo("Sale.Scan"));
        Assert.That(_logged[0].Ex, Is.SameAs(boom));
    }

    [Test]
    public async Task RunAsync_ThrowAfterAwait_IsLoggedAndReported_NotRethrown()
    {
        Exception? reported = null;

        await UiGuard.RunAsync("Sale.Scan", async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("after await");
        }, ex => { reported = ex; return Task.CompletedTask; }, Log);

        Assert.That(reported, Is.InstanceOf<InvalidOperationException>());
        Assert.That(_logged, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task RunAsync_Success_DoesNotReportOrLog()
    {
        bool ran = false, reported = false;

        await UiGuard.RunAsync("Sale.Scan", () => { ran = true; return Task.CompletedTask; },
            _ => { reported = true; return Task.CompletedTask; }, Log);

        Assert.That(ran, Is.True);
        Assert.That(reported, Is.False);
        Assert.That(_logged, Is.Empty);
    }

    [Test]
    public async Task RunAsync_ErrorHandlerThrows_IsSwallowedAndLogged()
    {
        await UiGuard.RunAsync("Sale.Scan", () => throw new InvalidOperationException("first"),
            _ => throw new InvalidOperationException("handler"), Log);

        Assert.That(_logged, Has.Count.EqualTo(2));
        Assert.That(_logged[1].Source, Is.EqualTo("Sale.Scan (error handler)"));
    }

    [Test]
    public void RunAsync_RunsActionSynchronouslyUntilFirstAwait()
    {
        // Key handlers set e.Handled inside the action; that must happen before
        // RunAsync returns to the caller, or the key also reaches other controls.
        bool handled = false;

        var pending = UiGuard.RunAsync("Sale.Key", async () =>
        {
            handled = true;
            await Task.Delay(50);
        }, _ => Task.CompletedTask, Log);

        Assert.That(handled, Is.True);
        pending.GetAwaiter().GetResult();
    }
}
