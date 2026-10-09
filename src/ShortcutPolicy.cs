using System;

namespace ERQuit
{
    internal interface IGameTarget
    {
        bool TryTerminateForeground(uint inputTime);
    }

    // Pure decision logic, tested without launching, inspecting or killing any process.
    internal sealed class ShortcutPolicy
    {
        private bool f4Down;
        private bool suppressed;
        internal bool SuppressingPress { get { return suppressed; } }

        internal bool Handle(int message, uint key, uint flags, uint time,
            bool extraModifier, bool enabled, IGameTarget target)
        {
            if (key != 0x73 || (flags & 0x12) != 0) return false;
            bool down = message == 0x100 || message == 0x104;
            bool up = message == 0x101 || message == 0x105;
            if (up)
            {
                bool consumed = suppressed;
                Reset();
                return consumed;
            }
            if (!down) return false;
            if (f4Down) return suppressed;
            f4Down = true;
            if (!enabled || extraModifier || (flags & 0x20) == 0 || target == null) return false;
            suppressed = target.TryTerminateForeground(time);
            return suppressed;
        }

        internal void Reset() { f4Down = false; suppressed = false; }
    }

    internal sealed class ForegroundProof
    {
        private IntPtr window;
        private uint since;

        internal void Activated(IntPtr handle, uint time)
        {
            window = handle;
            since = time;
        }

        internal void Destroyed(IntPtr handle)
        {
            if (window == handle) window = IntPtr.Zero;
        }

        internal bool Matches(IntPtr foreground, uint inputTime)
        {
            // Signed subtraction handles the Win32 tick counter wrapping every ~49 days.
            return window != IntPtr.Zero && foreground == window &&
                unchecked((int)(inputTime - since)) >= 0;
        }
    }
}
