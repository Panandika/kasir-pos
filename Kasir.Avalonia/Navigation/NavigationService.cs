using System.Collections.Generic;
using Avalonia.Controls;

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

    public static void Initialize(ShellWindow shell, ContentControl host)
    {
        Instance = shell;
        _host = host;
    }

    public static void Navigate(UserControl view)
    {
        _stack.Push(view);
        _host!.Content = view;
    }

    public static void GoBack()
    {
        _stack.TryPop(out _);
        if (_stack.TryPeek(out var prev))
        {
            (prev as INavigationAware)?.OnNavigatedTo();
            _host!.Content = prev;
        }
    }

    public static void ReplaceRoot(UserControl view)
    {
        _stack.Clear();
        Navigate(view);
    }
}
