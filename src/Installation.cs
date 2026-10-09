using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ERQuit
{
    internal sealed class Installation
    {
        internal const string AppId = "1049590";
        internal const string ExecutableName = "EternalReturn.exe";
        internal readonly string Executable;
        internal string DirectoryPath { get { return Path.GetDirectoryName(Executable); } }

        private Installation(string executable) { Executable = Path.GetFullPath(executable); }

        internal static List<Installation> Discover()
        {
            HashSet<string> roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                AddRegistryPath(roots, RegistryHive.CurrentUser, view, "SteamPath");
                AddRegistryPath(roots, RegistryHive.LocalMachine, view, "InstallPath");
            }
            string defaultSteam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            if (Directory.Exists(defaultSteam)) roots.Add(defaultSteam);
            HashSet<string> libraries = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase);
            foreach (string root in roots)
            {
                foreach (string relative in new[] { @"steamapps\libraryfolders.vdf", @"config\libraryfolders.vdf" })
                {
                    string file = Path.Combine(root, relative);
                    if (!File.Exists(file)) continue;
                    foreach (string path in Values(File.ReadAllText(file), "path"))
                        if (Path.IsPathRooted(path)) libraries.Add(Path.GetFullPath(path));
                }
            }
            List<Installation> result = new List<Installation>();
            foreach (string library in libraries)
            {
                string manifest = Path.Combine(library, "steamapps", "appmanifest_" + AppId + ".acf");
                if (!File.Exists(manifest)) continue;
                string content = File.ReadAllText(manifest);
                if (FirstValue(content, "appid") != AppId) continue;
                string folder = FirstValue(content, "installdir");
                if (!SafeFolderName(folder)) continue;
                string executable = Path.Combine(library, "steamapps", "common", folder, ExecutableName);
                if (File.Exists(executable)) result.Add(new Installation(executable));
            }
            return result;
        }

        internal static bool SafeFolderName(string folder)
        {
            return !String.IsNullOrWhiteSpace(folder) && folder != "." && folder != ".." &&
                folder.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
                folder.TrimEnd(' ', '.') == folder;
        }

        internal static Installation Match(string executable)
        {
            if (String.IsNullOrWhiteSpace(executable) || !Path.IsPathRooted(executable))
                throw new InvalidOperationException("게임 실행 파일의 전체 경로를 확인할 수 없습니다.");
            string fullPath = Path.GetFullPath(executable);
            foreach (Installation installation in Discover())
                if (String.Equals(installation.Executable, fullPath, StringComparison.OrdinalIgnoreCase)) return installation;
            throw new InvalidOperationException("Steam 설치 기록에 등록된 이터널 리턴 실행 파일만 허용합니다.");
        }

        internal FileStream OpenVerified()
        {
            // Reject path redirection. The executable stays locked against replacement during verification/launch.
            for (string path = Executable; !String.IsNullOrEmpty(path); path = Path.GetDirectoryName(path))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("보안을 위해 심볼릭 링크 / 정션 경로는 지원하지 않습니다.");
            }
            FileStream heldFile = new FileStream(Executable, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                string steamId = Path.Combine(DirectoryPath, "EternalReturn_Data", "StreamingAssets", "steamAppId");
                if (!File.Exists(steamId) || File.ReadAllText(steamId).Trim() != AppId ||
                    !File.Exists(Path.Combine(DirectoryPath, "GameAssembly.dll")) ||
                    !File.Exists(Path.Combine(DirectoryPath, "UnityPlayer.dll")))
                    throw new InvalidOperationException("이터널 리턴 설치 파일 구성이 일치하지 않습니다.");
                Signature.VerifyPublisher(Executable);
                return heldFile;
            }
            catch { heldFile.Dispose(); throw; }
        }

        private static void AddRegistryPath(HashSet<string> paths, RegistryHive hive, RegistryView view, string valueName)
        {
            using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
            using (RegistryKey key = root.OpenSubKey(@"Software\Valve\Steam"))
            {
                if (key == null) return;
                string value = key.GetValue(valueName) as string;
                if (!String.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value)) paths.Add(Path.GetFullPath(value));
            }
        }

        internal static IEnumerable<string> Values(string content, string key)
        {
            string pattern = "\"" + Regex.Escape(key) + "\"\\s*\"((?:\\\\.|[^\"\\\\])*)\"";
            foreach (Match match in Regex.Matches(content, pattern, RegexOptions.CultureInvariant))
                yield return match.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"");
        }

        private static string FirstValue(string text, string key)
        {
            foreach (string value in Values(text, key)) return value;
            return null;
        }
    }

    internal static class Signature
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct TrustFile
        {
            internal uint Size;
            [MarshalAs(UnmanagedType.LPWStr)] internal string Path;
            internal IntPtr FileHandle, KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TrustData
        {
            internal uint Size;
            internal IntPtr Policy, Sip;
            internal uint UiChoice, Revocation, UnionChoice;
            internal IntPtr File;
            internal uint StateAction;
            internal IntPtr StateData, Url;
            internal uint ProviderFlags, UiContext;
            internal IntPtr SignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
        private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);

        internal static void VerifyPublisher(string path)
        {
            TrustFile file = new TrustFile { Size = (uint)Marshal.SizeOf(typeof(TrustFile)), Path = path };
            IntPtr filePointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TrustFile)));
            Marshal.StructureToPtr(file, filePointer, false);
            TrustData data = new TrustData {
                Size = (uint)Marshal.SizeOf(typeof(TrustData)), UiChoice = 2,
                UnionChoice = 1, File = filePointer, StateAction = 1,
                // Cache-only certificate verification: no network fetch and no telemetry.
                ProviderFlags = 0x1000 | 0x10
            };
            Guid action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            try
            {
                int result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                if (result != 0) throw new InvalidOperationException("게임 파일 서명 검증 실패: 0x" + result.ToString("X8"));
                using (X509Certificate2 certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))
                {
                    if (!String.Equals(certificate.GetNameInfo(X509NameType.SimpleName, false),
                        "Nimbleneuron Corp.", StringComparison.Ordinal))
                        throw new InvalidOperationException("Nimbleneuron Corp.에서 서명한 게임 파일이 아닙니다.");
                }
            }
            finally
            {
                data.StateAction = 2;
                WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                Marshal.DestroyStructure(filePointer, typeof(TrustFile));
                Marshal.FreeHGlobal(filePointer);
            }
        }
    }
}
