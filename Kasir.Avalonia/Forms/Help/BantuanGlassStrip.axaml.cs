using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Lucide.Avalonia;
using Kasir.Help;
using Kasir.Help.KnowledgeBase;
using Kasir.Models;

namespace Kasir.Avalonia.Forms.Help;

/// <summary>
/// Bottom-center floating glass strip implementing the 15 wireframe states
/// from claude.ai/design. Code-behind drives all state transitions; no MVVM
/// library, matching project convention.
///
/// Wired by BantuanOverlayHost which owns the HelpService instance and
/// closes the overlay on Esc / click-outside / Sent timeout.
/// </summary>
public partial class BantuanGlassStrip : UserControl
{
    private readonly BantuanViewModel _vm = new();
    private HelpService? _service;
    private string _registerId = "01";
    private string _cashierId = "?";
    private string _appVersion = "0.0.0";
    private string _lastInvoice = "";
    private string _lastError = "";
    private CancellationTokenSource? _searchCts;

    public event EventHandler? Closed;

    public BantuanGlassStrip()
    {
        InitializeComponent();
        DataContext = _vm;
        _vm.PropertyChanged += OnVmPropertyChanged;
        Render();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public void Configure(
        HelpService service,
        string registerId, string cashierId,
        string appVersion, string lastInvoice, string lastError)
    {
        _service = service;
        _registerId = registerId;
        _cashierId = cashierId;
        _appVersion = appVersion;
        _lastInvoice = lastInvoice ?? "";
        _lastError = lastError ?? "";
    }

    public void FocusInput()
    {
        var box = this.FindControl<TextBox>("InputBox");
        box?.Focus();
    }

    private void OnVmPropertyChanged(object? s, PropertyChangedEventArgs e) =>
        Dispatcher.UIThread.Post(Render);

    // ------------------------------------------------------------------ keys

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (KeyboardRouter.IsEscape(e))
        {
            Close();
            e.Handled = true;
            return;
        }
        if (KeyboardRouter.IsShiftTab(e))
        {
            _vm.ToggleMode();
            e.Handled = true;
            return;
        }
        // Disambiguate digit picks: 1 / 2 / 3
        if (_vm.State == BantuanState.Disambiguate &&
            (e.Key == Key.D1 || e.Key == Key.D2 || e.Key == Key.D3))
        {
            int idx = e.Key - Key.D1;
            if (idx < _vm.Candidates.Count)
            {
                ShowAnswer(_vm.Candidates[idx]);
                e.Handled = true;
            }
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (KeyboardRouter.IsEnter(e))
        {
            _ = Submit();
            e.Handled = true;
            return;
        }
    }

    private void OnInputChanged(object? sender, TextChangedEventArgs e)
    {
        var box = sender as TextBox;
        _vm.Input = box?.Text ?? string.Empty;
        if (string.IsNullOrEmpty(_vm.Input))
        {
            _vm.State = BantuanState.Idle;
            return;
        }
        _vm.State = _vm.Mode == BantuanMode.Tanya
            ? BantuanState.Typing
            : BantuanState.Composing;
    }

    private void OnSendClicked(object? s, RoutedEventArgs e) => _ = Submit();

    private void OnScrimPressed(object? s, PointerPressedEventArgs e) => Close();

    // ----------------------------------------------------------------- flows

    private async Task Submit()
    {
        if (_service == null) return;
        if (string.IsNullOrWhiteSpace(_vm.Input)) return;

        if (_vm.Mode == BantuanMode.Tanya)
        {
            await SubmitTanya();
        }
        else
        {
            SubmitConfirmReport();
        }
    }

    private async Task SubmitTanya()
    {
        if (_service == null) return;
        _vm.State = BantuanState.Thinking;
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        RetrievalResult result;
        try
        {
            result = await _service.AskAsync(_vm.Input, _registerId, _searchCts.Token);
        }
        catch
        {
            _vm.State = BantuanState.AiDown;
            return;
        }

        _vm.Candidates.Clear();
        foreach (var h in result.Hits) _vm.Candidates.Add(h);

        switch (result.Confidence)
        {
            case RetrievalConfidence.High:
                if (result.Hits.Count > 0) ShowAnswer(result.Hits[0]);
                else _vm.State = BantuanState.NoAnswer;
                break;
            case RetrievalConfidence.Ambiguous:
                _vm.State = BantuanState.Disambiguate;
                break;
            default:
                _vm.State = BantuanState.NoAnswer;
                break;
        }
    }

    private void ShowAnswer(HelpFaqHit hit)
    {
        _vm.AnswerTitle = hit.Title ?? string.Empty;
        _vm.AnswerBody = hit.Content ?? string.Empty;
        _vm.State = BantuanState.Answer;
    }

    private void SubmitConfirmReport()
    {
        // First press of Enter in Lapor mode → Confirm screen
        if (_vm.State != BantuanState.Confirm)
        {
            _vm.State = BantuanState.Confirm;
            return;
        }
        // Second press → actually send
        if (_service == null) return;
        try
        {
            string ticketNo = _service.Report(
                _vm.Category, _vm.Input, _registerId, _cashierId,
                _appVersion, _lastInvoice, _lastError);
            _vm.TicketNo = ticketNo;
            _vm.State = BantuanState.Sent;

            // Auto-close after 3.5s
            _ = Task.Run(async () =>
            {
                await Task.Delay(3500);
                await Dispatcher.UIThread.InvokeAsync(Close);
            });
        }
        catch (Exception ex)
        {
            _vm.StatusMessage = ex.Message;
            _vm.State = BantuanState.AiDown;
        }
    }

    private void Close()
    {
        Closed?.Invoke(this, EventArgs.Empty);
    }

    // -------------------------------------------------------------- rendering

    private void Render()
    {
        var aboveCard = this.FindControl<Border>("AboveCard");
        var aboveContent = this.FindControl<StackPanel>("AboveContent");
        var modePill = this.FindControl<Border>("ModePill");
        var modeIcon = this.FindControl<LucideIcon>("ModeIcon");
        var modeLabel = this.FindControl<TextBlock>("ModeLabel");
        var inputBox = this.FindControl<TextBox>("InputBox");
        var enterHint = this.FindControl<StackPanel>("EnterHint");
        var sendIcon = this.FindControl<LucideIcon>("SendIcon");
        var offlineBadge = this.FindControl<Border>("OfflineBadge");
        var sentToast = this.FindControl<Border>("SentToast");
        var toastTitle = this.FindControl<TextBlock>("ToastTitle");

        if (aboveCard == null || aboveContent == null || modePill == null ||
            modeLabel == null || inputBox == null || enterHint == null ||
            sendIcon == null || offlineBadge == null || sentToast == null) return;

        // Mode pill colour + icon
        modePill.Classes.Set("modePillTanya", _vm.Mode == BantuanMode.Tanya);
        modePill.Classes.Set("modePillLapor", _vm.Mode == BantuanMode.Lapor);
        if (modeIcon != null) modeIcon.Kind = _vm.Mode == BantuanMode.Lapor
            ? LucideIconKind.Flag : LucideIconKind.Sparkles;
        modeLabel.Text = _vm.ModeLabel;

        // Placeholder + send icon
        inputBox.SetValue(TextBox.PlaceholderTextProperty, _vm.Placeholder);
        sendIcon.Kind = _vm.Mode == BantuanMode.Lapor
            ? LucideIconKind.Send : LucideIconKind.ArrowRight;

        // Offline / sent / above-card visibility per state
        offlineBadge.IsVisible = _vm.State == BantuanState.Offline;
        sentToast.IsVisible = _vm.State == BantuanState.Sent;
        if (_vm.State == BantuanState.Sent && toastTitle != null)
        {
            toastTitle.Text = "Laporan terkirim · #" + _vm.TicketNo;
        }

        // enter hint visible while typing/composing
        enterHint.IsVisible = _vm.State is BantuanState.Typing or BantuanState.Composing
            or BantuanState.Confirm or BantuanState.Followup;

        // Above card: render content per state
        aboveContent.Children.Clear();
        aboveCard.IsVisible = true;
        switch (_vm.State)
        {
            case BantuanState.Idle:
                RenderIdle(aboveContent);
                break;
            case BantuanState.Thinking:
                RenderThinking(aboveContent);
                break;
            case BantuanState.Disambiguate:
                RenderDisambiguate(aboveContent);
                break;
            case BantuanState.Answer:
                RenderAnswer(aboveContent);
                break;
            case BantuanState.Followup:
                RenderFollowup(aboveContent);
                break;
            case BantuanState.Guided:
                RenderGuided(aboveContent);
                break;
            case BantuanState.NoAnswer:
                RenderNoAnswer(aboveContent);
                break;
            case BantuanState.Composing:
                RenderComposing(aboveContent);
                break;
            case BantuanState.Voice:
                RenderVoice(aboveContent);
                break;
            case BantuanState.Confirm:
                RenderConfirm(aboveContent);
                break;
            case BantuanState.Offline:
                RenderOffline(aboveContent);
                break;
            case BantuanState.AiDown:
                RenderAiDown(aboveContent);
                break;
            case BantuanState.Typing:
            case BantuanState.Sent:
            default:
                aboveCard.IsVisible = false;
                break;
        }
    }

    // --- per-state above-card builders ----------------------------------

    private void RenderIdle(StackPanel host)
    {
        var label = MakeLabelLine(
            _vm.Mode == BantuanMode.Lapor ? "Kategori:" : "Saran:");
        host.Children.Add(label);
        var chips = new WrapPanel { ItemSpacing = 6 };
        if (_vm.Mode == BantuanMode.Lapor)
        {
            foreach (var (icon, text, value) in new[] {
                ("Cpu", "Hardware", "hardware"),
                ("RotateCcw", "Transaksi", "transaksi"),
                ("AppWindow", "Aplikasi", "aplikasi"),
                ("Lightbulb", "Saran", "saran") })
            {
                chips.Children.Add(MakeCategoryChip(icon, text, value));
            }
        }
        else
        {
            foreach (var s in _vm.Suggestions) chips.Children.Add(MakeSuggestionChip(s));
        }
        host.Children.Add(chips);
    }

    private void RenderThinking(StackPanel host)
    {
        var row = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(new LucideIcon { Kind = LucideIconKind.Loader, Size = 14, Foreground = BrandBrush() });
        row.Children.Add(new TextBlock
        {
            Text = "Mencari jawaban...",
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            FontSize = 12, Foreground = SecondaryBrush()
        });
        row.Children.Add(MakeShimmer(120, 6));
        row.Children.Add(MakeShimmer(70, 6));
        host.Children.Add(row);
    }

    private void RenderAnswer(StackPanel host)
    {
        host.Children.Add(MakeHeader(LucideIconKind.Sparkles, "JAWABAN", BrandBrush()));
        host.Children.Add(new TextBlock
        {
            Text = _vm.AnswerTitle, FontWeight = FontWeight.SemiBold, FontSize = 12
        });
        host.Children.Add(new TextBlock
        {
            Text = _vm.AnswerBody, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            LineHeight = 18, Foreground = PrimaryBrush()
        });
    }

    private void RenderFollowup(StackPanel host)
    {
        var prior = new TextBlock
        {
            Text = "↳ " + _vm.AnswerTitle,
            FontSize = 10, Foreground = DimBrush(),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        host.Children.Add(prior);
        host.Children.Add(new Border
        {
            Height = 1, Background = SubtleBrush(), Margin = new Thickness(0, 4)
        });
        host.Children.Add(MakeHeader(LucideIconKind.Sparkles, "LANJUT", BrandBrush()));
    }

    private void RenderGuided(StackPanel host)
    {
        host.Children.Add(MakeHeader(LucideIconKind.Sparkles, "COBA LANGKAH INI", BrandBrush()));
        host.Children.Add(new TextBlock
        {
            Text = "Pilih baris item yang mau didiskon, lalu tekan F8 sekarang.",
            FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 18
        });
        var listening = new Border
        {
            Background = SoftBrandBrush(),
            BorderBrush = BrandBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6),
        };
        var row = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new LucideIcon { Kind = LucideIconKind.Radio, Size = 11, Foreground = BrandBrush() });
        row.Children.Add(new TextBlock { Text = "Mendengarkan tombol... lanjut otomatis saat F8 ditekan.",
            FontSize = 10, Foreground = SecondaryBrush() });
        listening.Child = row;
        host.Children.Add(listening);
    }

    private void RenderDisambiguate(StackPanel host)
    {
        host.Children.Add(MakeHeader(LucideIconKind.MessageCircleQuestionMark, "MANA YANG DIMAKSUD?", BrandBrush()));
        for (int i = 0; i < Math.Min(3, _vm.Candidates.Count); i++)
        {
            var cand = _vm.Candidates[i];
            int captured = i;
            var btn = new Button
            {
                Content = "[" + (i + 1) + "]  " + cand.Title,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 6),
                FontSize = 11
            };
            btn.Click += (_, _) => ShowAnswer(_vm.Candidates[captured]);
            host.Children.Add(btn);
        }
    }

    private void RenderNoAnswer(StackPanel host)
    {
        host.Children.Add(MakeHeader(LucideIconKind.SearchX, "TIDAK DITEMUKAN", WarningBrush()));
        host.Children.Add(new TextBlock
        {
            Text = "Belum ada panduan untuk pertanyaan ini. Lapor ke IT supaya dijawab langsung?",
            FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 18
        });
        var row = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        var laporBtn = new Button
        {
            Content = "Lapor ke IT (Shift+Tab)",
            Background = WarningBrush(), Foreground = OnBrandBrush(),
            Padding = new Thickness(10, 4)
        };
        laporBtn.Click += (_, _) => _vm.ToggleMode();
        row.Children.Add(laporBtn);
        host.Children.Add(row);
    }

    private void RenderComposing(StackPanel host)
    {
        host.Children.Add(MakeHeader(LucideIconKind.Flag, "LAPOR MASALAH", WarningBrush()));
        var chips = new WrapPanel { ItemSpacing = 4 };
        foreach (var (icon, text, value) in new[] {
            ("Cpu", "Hardware", "hardware"),
            ("RotateCcw", "Transaksi", "transaksi"),
            ("AppWindow", "Aplikasi", "aplikasi"),
            ("Lightbulb", "Saran", "saran") })
        {
            chips.Children.Add(MakeCategoryChip(icon, text, value));
        }
        host.Children.Add(chips);

        var ctxLine = new TextBlock
        {
            Text = "Terlampir: " + _registerId + " · v" + _appVersion + " · " + _lastInvoice,
            FontSize = 9, Foreground = DimBrush(),
            FontFamily = "ui-monospace, Consolas, monospace"
        };
        host.Children.Add(ctxLine);
    }

    private void RenderVoice(StackPanel host)
    {
        host.Children.Add(MakeHeader(LucideIconKind.Mic, "DIKTE (NONAKTIF V1)", DangerBrush()));
        host.Children.Add(new TextBlock
        {
            Text = "Voice dictation belum tersedia di v1. Gunakan input teks.",
            FontSize = 11, Foreground = SecondaryBrush(), TextWrapping = TextWrapping.Wrap
        });
    }

    private void RenderConfirm(StackPanel host)
    {
        host.Children.Add(MakeHeader(LucideIconKind.Send, "KIRIM LAPORAN?", WarningBrush()));
        var preview = new Border
        {
            Background = Bg2Brush(),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6),
            Child = new TextBlock { Text = _vm.Input, FontSize = 11, TextWrapping = TextWrapping.Wrap }
        };
        host.Children.Add(preview);
        host.Children.Add(new TextBlock
        {
            Text = _vm.Category + " · " + _registerId + " · v" + _appVersion + " · " + _lastInvoice,
            FontSize = 9, Foreground = DimBrush(),
            FontFamily = "ui-monospace, Consolas, monospace"
        });
        host.Children.Add(new TextBlock
        {
            Text = "Tekan Enter sekali lagi untuk kirim · Esc batal",
            FontSize = 9, Foreground = DimBrush()
        });
    }

    private void RenderOffline(StackPanel host)
    {
        var row = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new LucideIcon { Kind = LucideIconKind.CloudOff, Size = 13, Foreground = WarningBrush() });
        row.Children.Add(new TextBlock
        {
            Text = "Sync tertunda · jawaban dari cache lokal. Laporan terkirim saat online.",
            FontSize = 11, TextWrapping = TextWrapping.Wrap
        });
        host.Children.Add(row);
    }

    private void RenderAiDown(StackPanel host)
    {
        host.Children.Add(MakeHeader(LucideIconKind.CircleAlert, "AI TIDAK TERSEDIA", DangerBrush()));
        host.Children.Add(new TextBlock
        {
            Text = "Pakai panduan tersimpan, atau langsung lapor ke IT.",
            FontSize = 11, Foreground = SecondaryBrush()
        });
        var chips = new WrapPanel { ItemSpacing = 4 };
        foreach (var s in _vm.Suggestions) chips.Children.Add(MakeSuggestionChip(s));
        host.Children.Add(chips);
    }

    // --- helpers --------------------------------------------------------

    private Border MakeSuggestionChip(string text)
    {
        var b = new Border { Classes = { "chip" } };
        b.Child = new TextBlock { Text = text, FontSize = 10, FontWeight = FontWeight.Medium };
        b.PointerPressed += (_, _) =>
        {
            _vm.Input = text;
            var inputBox = this.FindControl<TextBox>("InputBox");
            if (inputBox != null) inputBox.Text = text;
            _ = Submit();
        };
        return b;
    }

    private Border MakeCategoryChip(string lucide, string text, string value)
    {
        bool active = _vm.Category == value;
        var b = new Border
        {
            Classes = { "chip" },
            BorderBrush = active ? WarningBrush() : SubtleBrush(),
            Background = active ? Bg2Brush() : Bg1Brush()
        };
        var row = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 4 };
        if (Enum.TryParse<LucideIconKind>(lucide, out var kind))
        {
            row.Children.Add(new LucideIcon { Kind = kind, Size = 10,
                Foreground = active ? WarningBrush() : SecondaryBrush() });
        }
        row.Children.Add(new TextBlock { Text = text, FontSize = 10,
            FontWeight = active ? FontWeight.SemiBold : FontWeight.Medium,
            Foreground = active ? WarningBrush() : SecondaryBrush() });
        b.Child = row;
        b.PointerPressed += (_, _) =>
        {
            _vm.Category = value;
            Render();
        };
        return b;
    }

    private TextBlock MakeLabelLine(string text) => new()
    {
        Text = text, FontSize = 9, Foreground = SecondaryBrush(),
        FontWeight = FontWeight.SemiBold,
    };

    private StackPanel MakeHeader(LucideIconKind icon, string text, IBrush colour)
    {
        var p = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        p.Children.Add(new LucideIcon { Kind = icon, Size = 12, Foreground = colour });
        p.Children.Add(new TextBlock
        {
            Text = text, FontSize = 9, FontWeight = FontWeight.SemiBold,
            Foreground = colour, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
        });
        return p;
    }

    private static Border MakeShimmer(double w, double h)
    {
        return new Border
        {
            Width = w, Height = h,
            CornerRadius = new CornerRadius(h / 2),
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0x2D, 0xBA, 0x8E))
        };
    }

    // ----- brush helpers ---------------------------------------------------

    private IBrush BrandBrush() => Resource("BrandBrush") ?? new SolidColorBrush(Colors.Teal);
    private IBrush SoftBrandBrush() => Resource("BrandSoftBrush") ?? Resource("BgSelectedBrush") ?? new SolidColorBrush(Colors.LightGreen);
    private IBrush WarningBrush() => Resource("WarningBrush") ?? new SolidColorBrush(Colors.Orange);
    private IBrush DangerBrush() => Resource("DangerBrush") ?? new SolidColorBrush(Colors.Red);
    private IBrush PrimaryBrush() => Resource("FgPrimaryBrush") ?? new SolidColorBrush(Colors.Black);
    private IBrush SecondaryBrush() => Resource("FgSecondaryBrush") ?? new SolidColorBrush(Colors.Gray);
    private IBrush DimBrush() => Resource("FgDimBrush") ?? new SolidColorBrush(Colors.DarkGray);
    private IBrush SubtleBrush() => Resource("BorderSubtleBrush") ?? new SolidColorBrush(Colors.LightGray);
    private IBrush Bg1Brush() => Resource("Bg1Brush") ?? new SolidColorBrush(Colors.White);
    private IBrush Bg2Brush() => Resource("Bg2Brush") ?? new SolidColorBrush(Colors.WhiteSmoke);
    private IBrush OnBrandBrush() => Resource("FgOnBrandBrush") ?? new SolidColorBrush(Colors.White);

    private IBrush? Resource(string key)
    {
        if (Application.Current?.Resources.TryGetResource(key, ActualThemeVariant, out var res) == true
            && res is IBrush b)
        {
            return b;
        }
        return null;
    }
}
