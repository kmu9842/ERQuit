using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ERQuit
{
    internal sealed class GameGuard : IDisposable, IGameTarget
    {
        private readonly GameConnection game;
        private readonly ForegroundProof foreground = new ForegroundProof();
        private readonly ShortcutPolicy policy = new ShortcutPolicy();
        private readonly Native.KeyboardProc keyboardCallback;
        private readonly Native.WinEventProc eventCallback;
        private IntPtr keyboardHook, foregroundHook, destroyHook;
        internal bool Enabled = true;
        internal bool TerminationRequested;
        internal int LastError;
        internal bool Alive { get { return game.Alive; } }
        internal bool SuppressingPress { get { return policy.SuppressingPress; } }
        internal string Executable { get { return game.Executable; } }

        internal GameGuard(GameConnection connection)
        {
            game = connection;
            keyboardCallback = Keyboard;
            eventCallback = WindowEvent;
            try
            {
                if (!game.Alive || game.ProcessId == 0) throw new InvalidOperationException("게임이 이미 종료되었습니다.");
                // The operating system filters events to the verified game PID.
                // No events from other processes and no DLL injection (OUTOFCONTEXT = 0).
                foregroundHook = Native.SetWinEventHook(Native.ForegroundEvent, Native.ForegroundEvent,
                    IntPtr.Zero, eventCallback, game.ProcessId, 0, 0);
                destroyHook = Native.SetWinEventHook(Native.DestroyEvent, Native.DestroyEvent,
                    IntPtr.Zero, eventCallback, game.ProcessId, 0, 0);
                if (foregroundHook == IntPtr.Zero || destroyHook == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "게임 창 이벤트 등록에 실패했습니다.");
                keyboardHook = Native.SetWindowsHookExW(Native.KeyboardHook, keyboardCallback, Native.GetModuleHandleW(null), 0);
                if (keyboardHook == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Alt+F4 감지 등록에 실패했습니다.");
            }
            catch { Dispose(); throw; }
        }

        private void WindowEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint eventTime)
        {
            // Do not retrieve a window title, process ID, executable, or accessible object here.
            if (!game.Alive || window == IntPtr.Zero) return;
            if (eventType == Native.ForegroundEvent) foreground.Activated(window, eventTime);
            else if (eventType == Native.DestroyEvent && objectId == 0 && childId == 0) foreground.Destroyed(window);
        }

        private IntPtr Keyboard(int code, IntPtr message, IntPtr data)
        {
            if (code == 0)
            {
                try
                {
                    Native.KeyboardData key = (Native.KeyboardData)Marshal.PtrToStructure(data, typeof(Native.KeyboardData));
                    if (key.VirtualKey == 0x73)
                    {
                        bool extra = Down(0x10) || Down(0x11) || Down(0x5B) || Down(0x5C);
                        if (policy.Handle(message.ToInt32(), key.VirtualKey, key.Flags, key.Time, extra, Enabled, this))
                            return new IntPtr(1);
                    }
                }
                catch { /* A failed check must pass input through and never trigger another action. */ }
            }
            return Native.CallNextHookEx(keyboardHook, code, message, data);
        }

        public bool TryTerminateForeground(uint inputTime)
        {
            if (!Enabled || TerminationRequested || !game.Alive) return false;
            if (!foreground.Matches(Native.GetForegroundWindow(), inputTime)) return false;
            // Recheck just before using the pinned handle, never look up the foreground process.
            if (!foreground.Matches(Native.GetForegroundWindow(), inputTime)) return false;
            int error;
            bool terminated = game.TryTerminate(out error);
            LastError = error;
            if (terminated) TerminationRequested = true;
            return terminated;
        }

        internal void ResetReleasedKey()
        {
            // Recover from a key-up lost during a desktop switch; do not leave a held
            // Alt+F4 auto-repeat free to close the application exposed by game exit.
            if (!Down(0x73)) policy.Reset();
        }

        private static bool Down(int key) { return (Native.GetAsyncKeyState(key) & 0x8000) != 0; }

        public void Dispose()
        {
            if (keyboardHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(keyboardHook); keyboardHook = IntPtr.Zero; }
            if (foregroundHook != IntPtr.Zero) { Native.UnhookWinEvent(foregroundHook); foregroundHook = IntPtr.Zero; }
            if (destroyHook != IntPtr.Zero) { Native.UnhookWinEvent(destroyHook); destroyHook = IntPtr.Zero; }
            game.Dispose();
            GC.KeepAlive(keyboardCallback);
            GC.KeepAlive(eventCallback);
        }
    }
}
