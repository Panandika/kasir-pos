namespace Kasir.Avalonia.Forms.Help;

/// <summary>15 wireframe states from claude.ai/design handoff bundle.</summary>
public enum BantuanState
{
    Hidden,        // 00 — pre-trigger; overlay invisible (status bar hint only)
    Idle,          // 01 — pinned pill + suggestion chips
    Typing,        // 02 — strip expanded, user typing
    Thinking,      // 03 — shimmer skeleton
    Disambiguate,  // 04 — pick from 2-3 candidate matches
    Answer,        // 05 — single best chunk shown
    Followup,      // 06 — prior Q&A collapsed above new question
    Guided,        // 07 — AI listens for F-key press
    NoAnswer,      // 08 — no match, escalate to LAPOR
    Composing,     // 09 — LAPOR textarea expanded, category chips
    Voice,         // 10 — dictation UI (mic disabled v1)
    Confirm,       // 11 — review report before send
    Sent,          // 12 — toast top-right
    Offline,       // 13 — sync badge in strip
    AiDown,        // 14 — fallback to FAQ chips + Lapor
}

public enum BantuanMode { Tanya, Lapor }
