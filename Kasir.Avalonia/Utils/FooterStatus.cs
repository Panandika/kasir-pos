using System;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Kasir.Avalonia.Utils;

/// <summary>
/// Per-TextBlock footer status helper. Forms register a default keyboard hint
/// once on Loaded; transient messages are shown via <see cref="Show"/> with a
/// 3-second auto-revert to the registered default.
/// </summary>
/// <remarks>
/// Keeps state out of each form's code-behind by attaching a <see cref="FooterState"/>
/// to the target TextBlock through a <see cref="ConditionalWeakTable{TKey,TValue}"/>,
/// so labels released by view navigation are GC'd cleanly.
/// </remarks>
public static class FooterStatus
{
    private sealed class FooterState
    {
        public string DefaultHint = "";
        public DispatcherTimer? Timer;
        public IBrush? OriginalForeground;
        public bool DefaultRegistered;
    }

    private static readonly ConditionalWeakTable<TextBlock, FooterState> _states = new();

    /// <summary>
    /// Register the default keyboard hint for <paramref name="label"/>. Sets the
    /// label's text to <paramref name="defaultHint"/> immediately and remembers
    /// it as the auto-revert target for future <see cref="Show"/> calls.
    /// </summary>
    public static void RegisterDefault(TextBlock label, string defaultHint)
    {
        if (label == null) return;
        var state = GetOrCreate(label);
        state.DefaultHint = defaultHint ?? "";
        state.DefaultRegistered = true;
        if (state.OriginalForeground == null)
            state.OriginalForeground = label.Foreground;
        label.Text = state.DefaultHint;
    }

    /// <summary>
    /// Show a transient <paramref name="message"/> on <paramref name="label"/>
    /// for <paramref name="seconds"/> (default 3). Cancels any prior timer for
    /// the same label. Auto-reverts to the registered default; if no default has
    /// been registered, the message stays visible (no-op revert).
    /// </summary>
    public static void Show(TextBlock label, string message, int seconds = 3)
    {
        if (label == null) return;
        var state = GetOrCreate(label);

        // Cancel any in-flight revert so back-to-back Show calls don't snap back
        // to the default mid-message.
        state.Timer?.Stop();
        state.Timer = null;

        label.Text = message ?? "";

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, seconds)) };
        timer.Tick += (s, _) =>
        {
            if (s is DispatcherTimer t) t.Stop();
            // Only revert if this timer is still the active one (not superseded
            // by a later Show).
            if (!ReferenceEquals(state.Timer, s)) return;
            state.Timer = null;
            if (state.DefaultRegistered)
                label.Text = state.DefaultHint;
        };
        state.Timer = timer;
        timer.Start();
    }

    /// <summary>
    /// Manually revert <paramref name="label"/> to its registered default and
    /// cancel any pending auto-revert timer. No-op if no default registered.
    /// </summary>
    public static void Reset(TextBlock label)
    {
        if (label == null) return;
        if (!_states.TryGetValue(label, out var state)) return;
        state.Timer?.Stop();
        state.Timer = null;
        if (state.DefaultRegistered)
            label.Text = state.DefaultHint;
    }

    private static FooterState GetOrCreate(TextBlock label)
    {
        if (_states.TryGetValue(label, out var existing)) return existing;
        var fresh = new FooterState { OriginalForeground = label.Foreground };
        _states.Add(label, fresh);
        return fresh;
    }
}
