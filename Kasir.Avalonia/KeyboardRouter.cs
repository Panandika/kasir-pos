using Avalonia.Input;

namespace Kasir.Avalonia;

/// <summary>
/// Maps Avalonia KeyEventArgs to named actions, replacing WinForms ProcessCmdKey across all 32 forms.
/// WinForms Keys.F1-F12 -> Key.F1-F12, Keys.Enter -> Key.Return, Keys.Escape -> Key.Escape,
/// Keys.Delete -> Key.Delete, Ctrl+S -> Key.S + KeyModifiers.Control.
/// </summary>
public static class KeyboardRouter
{
    public static bool IsF1(KeyEventArgs e) => e.Key == Key.F1 && e.KeyModifiers == KeyModifiers.None;
    public static bool IsF2(KeyEventArgs e) => e.Key == Key.F2 && e.KeyModifiers == KeyModifiers.None;
    public static bool IsF3(KeyEventArgs e) => e.Key == Key.F3 && e.KeyModifiers == KeyModifiers.None;
    public static bool IsF4(KeyEventArgs e) => e.Key == Key.F4 && e.KeyModifiers == KeyModifiers.None;
    public static bool IsF5(KeyEventArgs e) => e.Key == Key.F5 && e.KeyModifiers == KeyModifiers.None;
    public static bool IsF6(KeyEventArgs e) => e.Key == Key.F6 && e.KeyModifiers == KeyModifiers.None;
    public static bool IsF7(KeyEventArgs e) => e.Key == Key.F7 && e.KeyModifiers == KeyModifiers.None;
    public static bool IsF8(KeyEventArgs e) => e.Key == Key.F8 && e.KeyModifiers == KeyModifiers.None;
    public static bool IsF9(KeyEventArgs e) => e.Key == Key.F9 && e.KeyModifiers == KeyModifiers.None;
    public static bool IsF10(KeyEventArgs e) => e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.None;
    public static bool IsF11(KeyEventArgs e) => e.Key == Key.F11 && e.KeyModifiers == KeyModifiers.None;
    public static bool IsF12(KeyEventArgs e) => e.Key == Key.F12 && e.KeyModifiers == KeyModifiers.None;

    public static bool IsEnter(KeyEventArgs e) => e.Key == Key.Return && e.KeyModifiers == KeyModifiers.None;
    public static bool IsEscape(KeyEventArgs e) => e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None;
    public static bool IsDelete(KeyEventArgs e) => e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None;
    public static bool IsInsert(KeyEventArgs e) => e.Key == Key.Insert && e.KeyModifiers == KeyModifiers.None;
    public static bool IsTab(KeyEventArgs e) => e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None;
    public static bool IsShiftTab(KeyEventArgs e) => e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.Shift;

    public static bool IsCtrl(KeyEventArgs e, Key key) =>
        e.Key == key && e.KeyModifiers == KeyModifiers.Control;

    public static bool IsCtrlS(KeyEventArgs e) => IsCtrl(e, Key.S);
    public static bool IsCtrlP(KeyEventArgs e) => IsCtrl(e, Key.P);
    public static bool IsCtrlZ(KeyEventArgs e) => IsCtrl(e, Key.Z);
    public static bool IsCtrlA(KeyEventArgs e) => IsCtrl(e, Key.A);
    public static bool IsCtrlF(KeyEventArgs e) => IsCtrl(e, Key.F);
    public static bool IsCtrlD(KeyEventArgs e) => IsCtrl(e, Key.D);
    public static bool IsCtrlN(KeyEventArgs e) => IsCtrl(e, Key.N);
    public static bool IsCtrlE(KeyEventArgs e) => IsCtrl(e, Key.E);
    public static bool IsCtrlR(KeyEventArgs e) => IsCtrl(e, Key.R);
    public static bool IsCtrlL(KeyEventArgs e) => IsCtrl(e, Key.L);

    public static bool IsPageUp(KeyEventArgs e) => e.Key == Key.PageUp && e.KeyModifiers == KeyModifiers.None;
    public static bool IsPageDown(KeyEventArgs e) => e.Key == Key.PageDown && e.KeyModifiers == KeyModifiers.None;
    public static bool IsHome(KeyEventArgs e) => e.Key == Key.Home && e.KeyModifiers == KeyModifiers.None;
    public static bool IsEnd(KeyEventArgs e) => e.Key == Key.End && e.KeyModifiers == KeyModifiers.None;
    public static bool IsUp(KeyEventArgs e) => e.Key == Key.Up && e.KeyModifiers == KeyModifiers.None;
    public static bool IsDown(KeyEventArgs e) => e.Key == Key.Down && e.KeyModifiers == KeyModifiers.None;
}
