using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Kasir.Avalonia.Navigation;

public interface INavigationAware
{
    void OnNavigatedTo();
}

public static class NavigationService
{
    private static ContentControl? _host;
    private static readonly Stack<UserControl> _stack = new();

    public static ShellWindow Instance { get; private set; } = null!;
    public static Window Owner => Instance;

    /// <summary>
    /// Factory that rebuilds the post-login home view. Set by LoginView on
    /// successful auth so any view (SaleView, ShiftView, etc.) can return
    /// to the main menu via GoHome() without juggling AuthService refs.
    /// </summary>
    public static Func<UserControl>? HomeFactory { get; set; }

    public static void Initialize(ShellWindow shell, ContentControl host)
    {
        Instance = shell;
        _host = host;
    }

    public static void GoHome()
    {
        if (HomeFactory == null) return;
        ReplaceRoot(HomeFactory());
    }

    public static void Navigate(UserControl view)
    {
        _stack.Push(view);
        _host!.Content = view;
        FocusAfterSwap(view);
    }

    public static void GoBack()
    {
        _stack.TryPop(out _);
        if (_stack.TryPeek(out var prev))
        {
            (prev as INavigationAware)?.OnNavigatedTo();
            _host!.Content = prev;
            FocusAfterSwap(prev);
        }
    }

    /// <summary>
    /// Ensures the newly-activated view owns keyboard focus so its OnKeyDown
    /// override (including Esc) fires without requiring a click first. Deferred
    /// to Background priority so the visual tree is settled before Focus() runs.
    /// Views that want focus on a specific descendant should implement
    /// INavigationAware and call Focus() there instead.
    /// </summary>
    private static void FocusAfterSwap(UserControl view)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!view.IsKeyboardFocusWithin) view.Focus();
        }, DispatcherPriority.Background);
    }

    public static void ReplaceRoot(UserControl view)
    {
        _stack.Clear();
        Navigate(view);
    }
}
