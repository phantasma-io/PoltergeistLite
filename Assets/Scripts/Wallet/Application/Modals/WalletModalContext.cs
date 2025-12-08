using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Holds modal dialog state so it can be reused by different UI layers.
    /// </summary>
    public sealed class WalletModalContext
    {
        public string[] Options { get; set; } = Array.Empty<string>();
        public ModalOptionsKind OptionsKind { get; set; } = ModalOptionsKind.Default;
        public int ConfirmDelay { get; set; }
        public bool Redirected { get; set; }
        public float Time { get; set; }
        public ModalState State { get; set; }
        public Action<PromptResult, string> Callback { get; set; }
        public string Input { get; set; } = string.Empty;
        public string InputKey { get; set; }
        public int MinInputLength { get; set; }
        public int MaxInputLength { get; set; }
        public string Caption { get; set; } = string.Empty;
        public Vector2 CaptionScroll { get; set; }
        public string Title { get; set; } = string.Empty;
        public int MaxLines { get; set; } = 1;
        public string HintsLabel { get; set; } = "...";
        public Dictionary<string, string> Hints { get; set; }
        public PromptResult Result { get; set; } = PromptResult.Waiting;
        public int LineCount { get; set; }
        public Texture2D PromptPicture { get; set; }
        public Action OnCopy { get; set; }
        public bool CloseOnCopy { get; set; }
        public bool EscapeActivatesSecondary { get; set; }
        public bool EnterActivatesPrimary { get; set; }
        public bool EnterOrEscapeActivatesSingle { get; set; }
        public bool SecondaryIsCopy { get; set; }
        public PromptResult PrimaryResult { get; set; } = PromptResult.Success;
        public PromptResult SecondaryResult { get; set; } = PromptResult.Failure;

        public void Reset()
        {
            Options = Array.Empty<string>();
            OptionsKind = ModalOptionsKind.Default;
            ConfirmDelay = 0;
            Redirected = false;
            Time = 0;
            State = ModalState.None;
            Callback = null;
            Input = string.Empty;
            InputKey = null;
            MinInputLength = 0;
            MaxInputLength = 0;
            Caption = string.Empty;
            CaptionScroll = Vector2.zero;
            Title = string.Empty;
            MaxLines = 1;
            HintsLabel = "...";
            Hints = null;
            Result = PromptResult.Waiting;
            LineCount = 0;
            PromptPicture = null;
            OnCopy = null;
            CloseOnCopy = false;
            EscapeActivatesSecondary = false;
            EnterActivatesPrimary = false;
            EnterOrEscapeActivatesSingle = false;
            SecondaryIsCopy = false;
            PrimaryResult = PromptResult.Success;
            SecondaryResult = PromptResult.Failure;
        }
    }
}
