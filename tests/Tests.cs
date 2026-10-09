using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ERQuit
{
    internal static class Tests
    {
        private static int checks;
        private sealed class Target : IGameTarget
        {
            internal int Calls;
            internal bool Success = true;
            public bool TryTerminateForeground(uint inputTime) { Calls++; return Success; }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Policy();
                Focus();
                Identity();
                Arguments();
                Paths();
                ApiSurface();
                Console.WriteLine("PASS: " + checks + " safety checks (no process launched or terminated).");
                if (args.Contains("--verify-installed")) VerifyInstalled();
                if (args.Contains("--verify-running")) VerifyRunning();
                if (args.Contains("--render")) Render();
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }

        private static void Check(bool condition, string name)
        {
            checks++;
            if (!condition) throw new Exception("FAIL: " + name);
        }

        private static void Policy()
        {
            Target game = new Target();
            ShortcutPolicy policy = new ShortcutPolicy();
            Check(!policy.Handle(0x104, 0x41, 0x20, 100, false, true, game), "unrelated key passes");
            Check(game.Calls == 0, "unrelated key never touches target");
            Check(!policy.Handle(0x100, 0x73, 0, 100, false, true, game), "F4 alone passes");
            Check(!policy.Handle(0x104, 0x73, 0x20, 101, false, true, game), "adding Alt during an existing F4 press does not fire");
            Check(game.Calls == 0, "F4 alone never touches target");
            policy.Reset();
            Check(!policy.Handle(0x104, 0x73, 0x30, 100, false, true, game), "injected key ignored");
            Check(!policy.Handle(0x104, 0x73, 0x22, 100, false, true, game), "lower-integrity injection ignored");
            Check(game.Calls == 0, "injected key never touches target");
            Check(!policy.Handle(0x104, 0x73, 0x20, 100, true, true, game), "extra modifier passes");
            policy.Reset();
            Check(!policy.Handle(0x104, 0x73, 0x20, 100, false, false, game), "disabled passes");
            Check(game.Calls == 0, "disabled never touches target");
            policy.Reset();
            Check(!policy.Handle(0x104, 0x73, 0x20, 100, false, true, null), "disconnected passes");
            policy.Reset();
            game.Success = false;
            Check(!policy.Handle(0x104, 0x73, 0x20, 100, false, true, game), "wrong foreground or failed termination passes");
            Check(!policy.Handle(0x105, 0x73, 0x20, 101, false, true, game), "failed action key-up passes");
            Check(game.Calls == 1, "failed action attempted once");
            game.Success = true;
            Check(policy.Handle(0x104, 0x73, 0x20, 102, false, true, game), "valid action consumed");
            Check(policy.Handle(0x104, 0x73, 0x20, 103, false, true, game), "held key repeat consumed");
            Check(game.Calls == 2, "held key never kills twice");
            Check(!policy.Handle(0x105, 0x73, 0x30, 103, false, true, game), "injected key-up cannot release real held key");
            Check(policy.SuppressingPress, "real latch preserved");
            Check(policy.Handle(0x105, 0x73, 0, 104, false, false, null), "matching key-up consumed after focus/enable change");
            Check(!policy.SuppressingPress, "latch released");
            Check(!policy.Handle(0x104, 0x73, 0x20, 105, false, true, null), "next unrelated app press untouched");
            policy.Reset();
            game.Success = false;
            int before = game.Calls;
            Check(!policy.Handle(0x104, 0x73, 0x20, 200, false, true, game), "press begun outside game passes");
            game.Success = true;
            Check(!policy.Handle(0x104, 0x73, 0x20, 201, false, true, game), "moving focus with held key cannot kill game");
            Check(game.Calls == before + 1, "focus change repeat does not retry");
        }

        private static void Focus()
        {
            ForegroundProof proof = new ForegroundProof();
            Check(!proof.Matches(new IntPtr(123), 20), "no window proof rejects");
            proof.Activated(new IntPtr(123), 100);
            Check(proof.Matches(new IntPtr(123), 100), "same foreground accepted");
            Check(!proof.Matches(new IntPtr(456), 101), "other foreground rejected");
            Check(!proof.Matches(IntPtr.Zero, 101), "no foreground rejected");
            Check(!proof.Matches(new IntPtr(123), 99), "queued input before focus rejected");
            proof.Destroyed(new IntPtr(456));
            Check(proof.Matches(new IntPtr(123), 101), "unrelated destroy leaves proof intact");
            proof.Destroyed(new IntPtr(123));
            Check(!proof.Matches(new IntPtr(123), 102), "destroyed HWND rejected");
            proof.Activated(new IntPtr(789), UInt32.MaxValue - 10);
            Check(proof.Matches(new IntPtr(789), 4), "clock wrap accepted");
            Check(!proof.Matches(new IntPtr(789), UInt32.MaxValue - 11), "pre-focus before wrap rejected");
        }

        private static void Identity()
        {
            string path = @"D:\SteamLibrary\steamapps\common\Eternal Return\EternalReturn.exe";
            Check(GameConnection.IdentityMatches(path, 100, path.ToUpperInvariant(), 109), "path case and WMI precision accepted");
            Check(!GameConnection.IdentityMatches(path, 100, path, 110), "PID reused by new process rejected");
            Check(!GameConnection.IdentityMatches(path, 100, path, 99), "older process rejected");
            Check(!GameConnection.IdentityMatches(path, 0, path, 0), "unknown creation time rejected");
            Check(!GameConnection.IdentityMatches(path, 100, @"C:\other\EternalReturn.exe", 100), "same filename wrong path rejected");
            Check(!GameConnection.IdentityMatches(path, 100, path + ".exe", 100), "suffix spoof rejected");
        }

        private static void Arguments()
        {
            string[] cases = { "", "simple", "two words", "quote\"inside", @"C:\folder\", "back\\\"quote", "한글 경로", "$() & %PATH%" };
            string command = CommandLine.Build(@"C:\test dir\EternalReturn.exe", cases);
            int count;
            IntPtr parsed = Native.CommandLineToArgvW(command, out count);
            if (parsed == IntPtr.Zero) throw new Exception("CommandLineToArgvW failed");
            try
            {
                Check(count == cases.Length + 1, "argument count");
                for (int i = 0; i < cases.Length; i++)
                    Check(Marshal.PtrToStringUni(Marshal.ReadIntPtr(parsed, (i + 1) * IntPtr.Size)) == cases[i], "argument round trip " + i);
            }
            finally { Native.LocalFree(parsed); }
            bool rejected = false;
            try { CommandLine.Quote("bad\0arg"); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "NUL rejected");
        }

        private static void Paths()
        {
            Check(Installation.SafeFolderName("Eternal Return"), "normal install folder");
            Check(Installation.SafeFolderName("이터널 리턴"), "Unicode install folder");
            foreach (string path in new[] { null, "", ".", "..", @"..\Other", @"C:\Other", @"folder\sub", "folder.", "folder " })
                Check(!Installation.SafeFolderName(path), "reject unsafe folder " + path);
            string vdf = "\"libraryfolders\" { \"0\" { \"path\" \"D:\\\\스팀 라이브러리\" } \"1\" { \"path\" \"F:\\\\Games\\\\Steam\" } }";
            string[] paths = Installation.Values(vdf, "path").ToArray();
            Check(paths.Length == 2, "multiple library discovery");
            Check(paths[0] == @"D:\스팀 라이브러리", "Unicode library path unescaped");
            Check(paths[1] == @"F:\Games\Steam", "alternate drive path unescaped");
            Check(!Installation.Values(vdf, "appid").Any(), "missing key does not match others");
        }

        private static void ApiSurface()
        {
            Check(GameConnection.TargetQuery == "SELECT ProcessId, ExecutablePath, CreationDate FROM Win32_Process WHERE Name = 'EternalReturn.exe'", "query always name-scoped");
            string[] forbidden = { "ReadProcessMemory", "WriteProcessMemory", "CreateRemoteThread", "CreateToolhelp32Snapshot", "EnumProcesses", "GetWindowThreadProcessId", "EnumWindows", "AdjustTokenPrivileges" };
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    DllImportAttribute import = (DllImportAttribute)Attribute.GetCustomAttribute(method, typeof(DllImportAttribute));
                    if (import != null) Check(!forbidden.Contains(import.EntryPoint), "restricted API surface: " + import.EntryPoint);
                }
            Check(Native.TargetAccess == 0x00101001, "minimal attached-process permissions");
            Check(Native.TerminateAndSynchronize == 0x00100001, "minimal launched-process permissions");
        }

        private static void VerifyInstalled()
        {
            List<Installation> installations = Installation.Discover();
            if (installations.Count == 0) throw new Exception("No Eternal Return installation detected.");
            foreach (Installation installation in installations)
                using (FileStream file = installation.OpenVerified())
                    Console.WriteLine("PASS: Steam installation + AppID + Authenticode publisher: " + installation.Executable);
        }

        private static void VerifyRunning()
        {
            using (GameConnection game = GameConnection.FindRunning())
            {
                if (game == null) throw new Exception("Eternal Return is not running.");
                if (!game.Alive) throw new Exception("Game exited during verification.");
                Console.WriteLine("PASS: name-filtered attachment, executable and creation-time checks; game left running.");
            }
        }

        private static void Render()
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            using (MainForm form = new MainForm(new string[0], true))
            using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
            {
                form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                form.Location = new Point(-30000, -30000);
                form.ShowInTaskbar = false;
                form.Show();
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app-preview.png"));
                form.Close();
            }
            string installerPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "dist", "ERQuit-Setup.exe"));
            Assembly setup = Assembly.LoadFrom(installerPath);
            using (Stream embedded = setup.GetManifestResourceStream("ERQuit.exe"))
            using (FileStream executable = File.OpenRead(Path.Combine(Path.GetDirectoryName(installerPath), "ERQuit.exe")))
            using (System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create())
                Check(BitConverter.ToString(hash.ComputeHash(embedded)) == BitConverter.ToString(hash.ComputeHash(executable)), "installer embeds exact app binary");
            using (System.Windows.Forms.Form form = (System.Windows.Forms.Form)Activator.CreateInstance(setup.GetType("ERQuitSetup.SetupForm"),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { true }, null))
            using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
            {
                form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                form.Location = new Point(-30000, -30000);
                form.ShowInTaskbar = false;
                form.Show();
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "setup-preview.png"));
                form.Close();
            }
            Console.WriteLine("PASS: app and installer UI rendered; installer payload verified.");
        }
    }
}
