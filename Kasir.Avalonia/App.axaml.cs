using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Kasir.Utils;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Infrastructure;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Hardware;

namespace Kasir.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        RegisterGlobalErrorHandlers();

        // The shared DB connection belongs to the UI thread; background callers get an
        // exception from GetConnection() instead of silently sharing it.
        DbConnection.BindToCurrentThread();

        // Apply persisted theme variant before opening MainWindow to avoid unstyled flash.
        ThemeService.Current.LoadAndApplyAtStartup();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new ShellWindow();
        }

        // Footer status models — best-effort, must not block UI thread.
        PrinterStatusModel.Current.Start(BuildPrinter);
        UpdateStatusModel.Current.Start();
        CloudSyncStatusModel.Current.Start();

        base.OnFrameworkInitializationCompleted();
    }

    private static bool _handlersRegistered;
    private static bool _showingError;

    // Without this, any exception escaping an event handler (key press, button
    // click, async void) closed the app with no message and no trace. Avalonia's own
    // Dispatcher.UnhandledException (Avalonia.Threading, not WPF) — raised for exceptions
    // escaping Dispatcher-run delegates, which includes async void continuations. The
    // process-wide AppDomain / TaskScheduler handlers are registered in Program.Main.
    private static void RegisterGlobalErrorHandlers()
    {
        if (_handlersRegistered) return;
        _handlersRegistered = true;

        Dispatcher.UIThread.UnhandledException += OnUiThreadUnhandledException;
    }

    // UI-thread exceptions: log, keep the app running, tell the user where the detail is.
    private static void OnUiThreadUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLog.Write("UI thread", e.Exception);
        e.Handled = true;

        if (_showingError) return; // an error inside the error dialog must not loop
        _showingError = true;
        try
        {
            var owner = (Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var shown = MsgBox.Show(owner,
                "Terjadi kesalahan: " + e.Exception.Message +
                "\n\nAplikasi tetap berjalan. Jika ada yang tidak beres, tutup lalu buka lagi." +
                "\nDetail tersimpan di " + CrashLog.LogPath,
                "Kesalahan");
            shown.ContinueWith(_ => _showingError = false);
        }
        catch
        {
            _showingError = false;
        }
    }

    private static IReceiptPrinter? BuildPrinter()
    {
        try
        {
            // No DB before first-run registration; opening one would create an empty kasir.db.
            if (!DbConnection.IsInitialized) return null;
            // Background timer thread — must not call GetConnection() (UI-thread-only).
            // Runs every 30 s: the connection is disposed once config is read.
            return ReceiptPrinter.FromConfig(DbConnection.CreateConnection);
        }
        catch
        {
            return null;
        }
    }
}