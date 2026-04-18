using Avalonia.Controls;
using Avalonia.Input;

namespace Kasir.Avalonia.Forms.Shared;

public partial class MsgBoxWindow : Window
{
    public bool Result { get; private set; }

    public MsgBoxWindow(string title, string message, bool showCancel)
    {
        InitializeComponent();
        Title = title;
        MsgText.Text = message;

        if (showCancel)
            BtnNo.IsVisible = true;

        BtnYes.Click += (_, _) => { Result = true; Close(); };
        BtnNo.Click += (_, _) => { Result = false; Close(); };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsEscape(e)) { Result = false; Close(); }
        if (KeyboardRouter.IsEnter(e)) { Result = true; Close(); }
    }
}
