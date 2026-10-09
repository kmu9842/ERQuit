using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace ERQuit
{
    internal sealed class GameConnection : IDisposable
    {
        internal const string TargetQuery = "SELECT ProcessId, ExecutablePath, CreationDate FROM Win32_Process WHERE Name = 'EternalReturn.exe'";
        private IntPtr handle;
        internal readonly uint ProcessId;
        internal readonly string Executable;
        internal bool Alive { get { return handle != IntPtr.Zero && Native.WaitForSingleObject(handle, 0) == Native.WaitTimeout; } }

        private GameConnection(IntPtr process, uint processId, string executable)
        {
            handle = process;
            ProcessId = processId;
            Executable = executable;
        }

        internal static GameConnection FindRunning()
        {
            uint processId = 0;
            string path = null;
            long created = 0;
            int count = 0;
            EnumerationOptions options = new EnumerationOptions { Rewindable = false, Timeout = TimeSpan.FromSeconds(5) };
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\cimv2", TargetQuery, options))
            using (ManagementObjectCollection results = searcher.Get())
            {
                foreach (ManagementObject result in results)
                {
                    using (result)
                    {
                        count++;
                        if (count > 1) throw new InvalidOperationException("이터널 리턴이 둘 이상 감지되어 연결하지 않았습니다.");
                        processId = Convert.ToUInt32(result["ProcessId"]);
                        path = result["ExecutablePath"] as string;
                        string date = result["CreationDate"] as string;
                        if (String.IsNullOrEmpty(date)) throw new InvalidOperationException("게임 실행 시각을 확인할 수 없습니다.");
                        created = ManagementDateTimeConverter.ToDateTime(date).ToUniversalTime().ToFileTimeUtc();
                    }
                }
            }
            if (count == 0) return null;
            if (processId == 0) throw new InvalidOperationException("잘못된 게임 프로세스 ID입니다.");
            Installation installation = Installation.Match(path);
            using (FileStream heldFile = installation.OpenVerified())
            {
                // The only OpenProcess call. A filtered name is not sufficient proof:
                // creation time and full image path are rechecked on THIS same handle.
                IntPtr process = Native.OpenProcess(Native.TargetAccess, false, processId);
                if (process == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "게임 연결 권한을 얻지 못했습니다.");
                try
                {
                    long actualCreated, exited, kernel, user;
                    if (!Native.GetProcessTimes(process, out actualCreated, out exited, out kernel, out user))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (created <= 0 || actualCreated < created || actualCreated - created >= 10)
                        throw new InvalidOperationException("게임 프로세스가 조회 중 변경되었습니다. 연결을 취소했습니다.");
                    StringBuilder actualPath = new StringBuilder(32768);
                    uint size = (uint)actualPath.Capacity;
                    if (!Native.QueryFullProcessImageNameW(process, 0, actualPath, ref size))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (!IdentityMatches(installation.Executable, created, actualPath.ToString(), actualCreated))
                        throw new InvalidOperationException("게임 프로세스가 조회 중 변경되었습니다. 연결을 취소했습니다.");
                    if (Native.WaitForSingleObject(process, 0) != Native.WaitTimeout)
                        throw new InvalidOperationException("게임이 이미 종료되었습니다.");
                    GameConnection connection = new GameConnection(process, processId, installation.Executable);
                    process = IntPtr.Zero;
                    return connection;
                }
                finally { if (process != IntPtr.Zero) Native.CloseHandle(process); }
            }
        }

        internal static bool IdentityMatches(string expectedPath, long expectedCreated, string actualPath, long actualCreated)
        {
            // WMI reports microseconds; FILETIME reports 100-nanosecond ticks.
            return expectedCreated > 0 && actualCreated >= expectedCreated && actualCreated - expectedCreated < 10 &&
                String.Equals(expectedPath, actualPath, StringComparison.OrdinalIgnoreCase);
        }

        internal static GameConnection Launch(string path, IList<string> arguments, out IntPtr suspendedThread)
        {
            suspendedThread = IntPtr.Zero;
            Installation installation = Installation.Match(path);
            using (FileStream heldFile = installation.OpenVerified())
            {
                Native.StartupInfo startup = new Native.StartupInfo { Size = (uint)Marshal.SizeOf(typeof(Native.StartupInfo)) };
                Native.ProcessInformation info;
                if (!Native.CreateProcessW(installation.Executable, new StringBuilder(CommandLine.Build(installation.Executable, arguments)),
                    IntPtr.Zero, IntPtr.Zero, false, Native.CreateSuspended, IntPtr.Zero, installation.DirectoryPath, ref startup, out info))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "이터널 리턴을 실행하지 못했습니다.");
                IntPtr restricted = IntPtr.Zero;
                try
                {
                    // -1 is the pseudo-handle for ERQuit itself, never another application.
                    if (!Native.DuplicateHandle(new IntPtr(-1), info.Process, new IntPtr(-1), out restricted,
                        Native.TerminateAndSynchronize, false, 0))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    GameConnection connection = new GameConnection(restricted, info.ProcessId, installation.Executable);
                    restricted = IntPtr.Zero;
                    suspendedThread = info.Thread;
                    info.Thread = IntPtr.Zero;
                    return connection;
                }
                finally
                {
                    // Setup failure must never strand a suspended game or kill it without Alt+F4.
                    if (info.Thread != IntPtr.Zero) { Native.ResumeThread(info.Thread); Native.CloseHandle(info.Thread); }
                    if (restricted != IntPtr.Zero) Native.CloseHandle(restricted);
                    Native.CloseHandle(info.Process);
                }
            }
        }

        internal bool TryTerminate(out int error)
        {
            error = 0;
            if (!Alive) return false;
            if (Native.TerminateProcess(handle, 1)) return true;
            error = Marshal.GetLastWin32Error();
            return false;
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero) { Native.CloseHandle(handle); handle = IntPtr.Zero; }
        }
    }
}
