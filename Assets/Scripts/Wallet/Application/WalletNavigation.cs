using System;
using System.Collections.Generic;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Keeps track of the current UI state and the navigation history.
    /// </summary>
    public sealed class WalletNavigation
    {
        private readonly Stack<GUIState> _history = new Stack<GUIState>();

        public GUIState CurrentState { get; private set; } = GUIState.Loading;

        public event Action<GUIState, GUIState> StateChanged;

        public void Reset(GUIState state)
        {
            _history.Clear();
            ChangeState(state);
        }

        public GUIState MoveTo(GUIState state, bool rememberCurrent)
        {
            if (rememberCurrent && CurrentState != GUIState.Loading)
            {
                _history.Push(CurrentState);
            }

            return ChangeState(state);
        }

        public GUIState Replace(GUIState state)
        {
            return ChangeState(state);
        }

        public GUIState PopOr(GUIState fallbackState)
        {
            var nextState = fallbackState;

            if (_history.Count > 0)
            {
                nextState = _history.Pop();
            }

            return ChangeState(nextState);
        }

        public void ClearHistory()
        {
            _history.Clear();
        }

        private GUIState ChangeState(GUIState nextState)
        {
            var previous = CurrentState;
            CurrentState = nextState;
            StateChanged?.Invoke(previous, nextState);
            return previous;
        }
    }
}
