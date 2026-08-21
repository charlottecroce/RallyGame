using System.Collections.Generic;
using UnityEngine;
using RallyGame.Core;

namespace RallyGame.UI
{
    /// Anything that takes over the screen and should close on Escape.
    public interface IUiModal
    {
        bool IsModalOpen { get; }
        void CloseModal();
    }

    /// One place that knows which panels are on screen and in what order.
    ///
    /// Without this, every panel polls InputReader.MenuPressed in its own Update and
    /// they all react in the same frame: the garage closes AND the pause menu opens,
    /// in an order that depends on component ordering. Here, exactly one thing reads
    /// Escape (PauseMenuView) and asks the stack to close the topmost panel.
    ///
    /// Also the single source of truth for "is a menu holding input", so the cursor
    /// and Var_InputLocked stay correct no matter what order panels close in.
    public static class UiModalStack
    {
        private static readonly List<IUiModal> open = new List<IUiModal>();

        public static int Depth { get { Prune(); return open.Count; } }
        public static bool AnyOpen { get { Prune(); return open.Count > 0; } }

        // Statics survive "Enter Play Mode without domain reload", so clear explicitly.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => open.Clear();

        public static void Push(IUiModal modal)
        {
            if (modal == null) return;
            open.Remove(modal);          // re-opening moves it to the top
            open.Add(modal);
            GameLog.Verbose(LogCat.UI, $"Modal opened: {Describe(modal)} (depth {open.Count})", modal as MonoBehaviour);
        }

        public static void Pop(IUiModal modal)
        {
            if (modal == null) return;
            if (open.Remove(modal))
                GameLog.Verbose(LogCat.UI, $"Modal closed: {Describe(modal)} (depth {open.Count})", modal as MonoBehaviour);
        }

        /// Returns true if something was closed. False means nothing was open and the
        /// caller should do whatever Escape normally does.
        public static bool CloseTopmost()
        {
            Prune();
            if (open.Count == 0) return false;

            var top = open[open.Count - 1];
            GameLog.Action(LogCat.UI, "Escape closed topmost panel", Describe(top), top as MonoBehaviour);
            top.CloseModal();
            open.Remove(top);            // no-op if CloseModal already popped itself
            return true;
        }

        public static void CloseAll()
        {
            Prune();
            for (int i = open.Count - 1; i >= 0; i--) open[i].CloseModal();
            open.Clear();
        }

        /// Call after any Push/Pop. Uses the whole stack rather than the caller's own
        /// state, so closing the garage while the pause menu is still up does not hand
        /// control back to the player.
        public static void ApplyInputState(BoolVariable inputLocked, bool freeCursor = true)
        {
            bool any = AnyOpen;
            if (inputLocked) inputLocked.Value = any;
            if (!freeCursor) return;

            Cursor.lockState = any ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = any;
        }

        private static void Prune()
        {
            for (int i = open.Count - 1; i >= 0; i--)
            {
                var m = open[i];
                bool destroyed = m is MonoBehaviour mb && mb == null;
                if (m == null || destroyed || !m.IsModalOpen) open.RemoveAt(i);
            }
        }

        private static string Describe(IUiModal m)
            => m is MonoBehaviour mb && mb ? $"{mb.gameObject.name}/{mb.GetType().Name}" : m.GetType().Name;
    }
}