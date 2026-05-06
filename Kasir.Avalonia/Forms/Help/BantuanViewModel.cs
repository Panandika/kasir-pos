using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Kasir.Models;

namespace Kasir.Avalonia.Forms.Help;

/// <summary>
/// View-model for BantuanGlassStrip. Tracks state machine, mode, current
/// query/body, retrieval result, and ticket number. Simple INPC matching
/// project convention (no MVVM library, no ReactiveUI).
/// </summary>
public sealed class BantuanViewModel : INotifyPropertyChanged
{
    private BantuanState _state = BantuanState.Idle;
    private BantuanMode _mode = BantuanMode.Tanya;
    private string _input = string.Empty;
    private string _answerTitle = string.Empty;
    private string _answerBody = string.Empty;
    private string _category = "hardware";
    private string _ticketNo = string.Empty;
    private string _statusMessage = string.Empty;
    private string _selectedSuggestion = string.Empty;

    public ObservableCollection<HelpFaqHit> Candidates { get; } = new();
    public ObservableCollection<string> Suggestions { get; } = new();

    public BantuanViewModel()
    {
        Suggestions.Add("Diskon");
        Suggestions.Add("Void");
        Suggestions.Add("Reprint struk");
        Suggestions.Add("Member");
    }

    public BantuanState State
    {
        get => _state;
        set => Set(ref _state, value);
    }

    public BantuanMode Mode
    {
        get => _mode;
        set
        {
            if (Set(ref _mode, value))
            {
                OnPropertyChanged(nameof(IsTanya));
                OnPropertyChanged(nameof(IsLapor));
                OnPropertyChanged(nameof(ModeLabel));
                OnPropertyChanged(nameof(Placeholder));
            }
        }
    }

    public bool IsTanya => Mode == BantuanMode.Tanya;
    public bool IsLapor => Mode == BantuanMode.Lapor;
    public string ModeLabel => Mode == BantuanMode.Lapor ? "LAPOR" : "TANYA";
    public string Placeholder => Mode == BantuanMode.Lapor
        ? "Jelaskan masalah singkat..."
        : "Tanya, atau Shift+Tab untuk lapor";

    public string Input
    {
        get => _input;
        set => Set(ref _input, value ?? string.Empty);
    }

    public string AnswerTitle
    {
        get => _answerTitle;
        set => Set(ref _answerTitle, value ?? string.Empty);
    }

    public string AnswerBody
    {
        get => _answerBody;
        set => Set(ref _answerBody, value ?? string.Empty);
    }

    public string Category
    {
        get => _category;
        set => Set(ref _category, value ?? "hardware");
    }

    public string TicketNo
    {
        get => _ticketNo;
        set => Set(ref _ticketNo, value ?? string.Empty);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => Set(ref _statusMessage, value ?? string.Empty);
    }

    public string SelectedSuggestion
    {
        get => _selectedSuggestion;
        set => Set(ref _selectedSuggestion, value ?? string.Empty);
    }

    public void ToggleMode()
    {
        Mode = Mode == BantuanMode.Tanya ? BantuanMode.Lapor : BantuanMode.Tanya;
        if (Mode == BantuanMode.Lapor && State != BantuanState.Sent)
        {
            State = string.IsNullOrEmpty(Input) ? BantuanState.Idle : BantuanState.Composing;
        }
        else if (Mode == BantuanMode.Tanya && State != BantuanState.Sent)
        {
            State = string.IsNullOrEmpty(Input) ? BantuanState.Idle : BantuanState.Typing;
        }
    }

    public void Reset()
    {
        Input = string.Empty;
        AnswerTitle = string.Empty;
        AnswerBody = string.Empty;
        TicketNo = string.Empty;
        StatusMessage = string.Empty;
        Candidates.Clear();
        State = BantuanState.Idle;
        Mode = BantuanMode.Tanya;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
}
