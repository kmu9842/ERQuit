using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: AssemblyTitle("ERQuit Setup")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace ERQuitSetup
{
    internal static class SetupProgram
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ERQuit");
        private readonly Label status;
        private readonly Button install, copy;
        private readonly TextBox option;
        private readonly bool preview;

        internal SetupForm() : this(false) { }

        internal SetupForm(bool preview)
        {
            this.preview = preview;
            Text = "ERQuit 설치";
            Font = new Font("맑은 고딕", 10F);
            ClientSize = new Size(650, 425);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Controls.Add(new Label { Text = "이터널 리턴과 함께 시작하는 ERQuit", Location = new Point(26, 25),
                Size = new Size(600, 40), Font = new Font(Font.FontFamily, 17F, FontStyle.Bold) });
            Controls.Add(new Label { Text = "관리자 권한 없이 현재 사용자에게 설치합니다.\n게임 설치 폴더는 각 PC의 Steam 설정에서 자동으로 찾습니다.",
                Location = new Point(28, 80), Size = new Size(590, 55) });
            Controls.Add(new TextBox { ReadOnly = true, Text = directory, Location = new Point(29, 142), Size = new Size(590, 28) });
            install = new Button { Text = "설치", Location = new Point(29, 184), Size = new Size(180, 42) };
            install.Click += Install;
            Controls.Add(install);
            status = new Label { Text = "설치 후 Steam 실행 옵션을 한 번 설정하면 됩니다.", Location = new Point(228, 185), Size = new Size(390, 48) };
            Controls.Add(status);
            Controls.Add(new Label { Text = "Steam → 이터널 리턴 → 속성 → 일반 → 실행 옵션", Location = new Point(28, 251), Size = new Size(590, 26) });
            option = new TextBox { ReadOnly = true, Text = "\"" + Path.Combine(directory, "ERQuit.exe") + "\" --steam %command%",
                Location = new Point(29, 282), Size = new Size(480, 28) };
            Controls.Add(option);
            copy = new Button { Text = "복사", Location = new Point(521, 277), Size = new Size(98, 38), Enabled = false };
            copy.Click += delegate { Clipboard.SetText(option.Text); status.Text = "복사했습니다. Steam의 실행 옵션에 붙여 넣으세요."; };
            Controls.Add(copy);
            Controls.Add(new Label { Text = "설정 후에는 Steam에서 평소처럼 게임을 실행하세요.\n지금 켜진 게임에는 시작 메뉴의 ERQuit을 실행해 연결할 수 있습니다.\n기존 실행 옵션이 있다면 %command% 뒤에 유지하세요.",
                Location = new Point(28, 337), Size = new Size(593, 75) });
        }

        protected override bool ShowWithoutActivation { get { return preview; } }

        private void Install(object sender, EventArgs args)
        {
            install.Enabled = false;
            try
            {
                Directory.CreateDirectory(directory);
                Extract("ERQuit.exe", Path.Combine(directory, "ERQuit.exe"));
                Extract("Usage.txt", Path.Combine(directory, "사용방법.txt"));
                string shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ERQuit.lnk");
                Type shellType = Type.GetTypeFromProgID("WScript.Shell", true);
                object shell = Activator.CreateInstance(shellType);
                object shortcut = null;
                try
                {
                    shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                    Type shortcutType = shortcut.GetType();
                    shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { Path.Combine(directory, "ERQuit.exe") });
                    shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { directory });
                    shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "이터널 리턴 Alt+F4 빠른 종료" });
                    shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                }
                finally
                {
                    if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
                    Marshal.FinalReleaseComObject(shell);
                }
                status.Text = "설치 완료. 아래 문구를 복사해 Steam 실행 옵션에 넣으세요.";
                install.Text = "설치 완료";
                copy.Enabled = true;
            }
            catch (Exception error)
            {
                status.Text = "설치하지 못했습니다.";
                install.Enabled = true;
                MessageBox.Show("실행 중인 ERQuit이 있다면 트레이에서 끝낸 후 다시 설치하세요.\n\n" + error.Message,
                    "ERQuit 설치", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void Extract(string resource, string path)
        {
            using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (input == null) throw new InvalidOperationException("설치 파일 리소스가 없습니다: " + resource);
                // CreateNew temporary file + atomic replacement avoids a partially written executable.
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                    if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
    }
}
