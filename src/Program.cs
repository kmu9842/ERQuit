using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("ERQuit")]
[assembly: AssemblyDescription("Eternal Return foreground-only Alt+F4 helper")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace ERQuit
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Application.Run(new MainForm(args)); }
            catch (Exception error) { MessageBox.Show(error.Message, "ERQuit", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly string[] arguments;
        private readonly bool steamMode;
        private readonly bool preview;
        private readonly Label status;
        private readonly TextBox gamePath;
        private readonly Button connect;
        private readonly NotifyIcon tray;
        private readonly Timer timer;
        private readonly ToolStripMenuItem toggle;
        private GameGuard guard;
        private bool busy, closing, started, errorShown;
        private int nextSearch;

        internal MainForm(string[] args) : this(args, false) { }

        internal MainForm(string[] args, bool preview)
        {
            this.preview = preview;
            arguments = args;
            steamMode = args.Length >= 2 && args[0] == "--steam";
            if (args.Length != 0 && !steamMode) throw new ArgumentException("실행 형식: ERQuit.exe 또는 ERQuit.exe --steam %command%");
            Text = "ERQuit · 이터널 리턴 빠른 종료";
            Font = new Font("맑은 고딕", 10F);
            ClientSize = new Size(650, 470);
            MinimumSize = new Size(666, 509);
            MaximumSize = new Size(900, 509);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(24, 28, 37);
            ForeColor = Color.FromArgb(231, 236, 245);
            Icon = SystemIcons.Application;

            Label title = new Label { Text = "게임 안에서만, Alt + F4", Font = new Font(Font.FontFamily, 23F, FontStyle.Bold),
                AutoSize = true, Location = new Point(28, 25) };
            Controls.Add(title);
            Controls.Add(new Label { Text = "이터널 리턴 창이 활성화되어 있을 때 게임 본체만 강제 종료합니다.",
                AutoSize = true, Location = new Point(31, 82), ForeColor = Color.FromArgb(164, 178, 197) });

            status = new Label { Text = "게임 연결 준비 중", Location = new Point(30, 125), Size = new Size(580, 57),
                ForeColor = Color.FromArgb(112, 225, 183), Font = new Font(Font.FontFamily, 11F, FontStyle.Bold) };
            Controls.Add(status);
            gamePath = new TextBox { ReadOnly = true, Location = new Point(32, 183), Size = new Size(582, 28),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.FromArgb(35, 41, 53), ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle };
            Controls.Add(gamePath);

            connect = MakeButton("게임 연결 / 다시 확인", 32, 230, 208);
            connect.Click += delegate { Search(); };
            connect.Enabled = !steamMode;
            Button minimize = MakeButton("트레이로 보내기", 254, 230, 174);
            minimize.Click += delegate { Hide(); };
            Button quit = MakeButton("ERQuit 끝내기", 442, 230, 172);
            quit.Click += delegate { ExitHelper(); };

            Controls.Add(new Label { Text = "Steam과 함께 실행", Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
                AutoSize = true, Location = new Point(30, 295) });
            Controls.Add(new Label { Text = "Steam → 이터널 리턴 → 속성 → 일반 → 실행 옵션에 아래 문구를 넣으세요.",
                AutoSize = true, Location = new Point(30, 328) });
            TextBox option = new TextBox { ReadOnly = true, Text = SteamOption(Application.ExecutablePath),
                Location = new Point(32, 359), Size = new Size(470, 28),
                BackColor = gamePath.BackColor, ForeColor = ForeColor };
            Controls.Add(option);
            Button copy = MakeButton("복사", 515, 354, 99);
            copy.Click += delegate { Clipboard.SetText(option.Text); };
            Controls.Add(new Label { Text = "연결 후 게임 창으로 전환하세요. 창의 X 버튼은 트레이로 숨깁니다.\nERQuit을 끝내도 게임은 계속 실행됩니다.",
                Location = new Point(31, 405), Size = new Size(584, 48), ForeColor = Color.FromArgb(164, 178, 197) });

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("상태 / 사용 방법", null, delegate { ShowStatus(); });
            toggle = new ToolStripMenuItem("Alt+F4 빠른 종료 사용") { Checked = true, CheckOnClick = true };
            toggle.CheckedChanged += delegate { if (guard != null) guard.Enabled = toggle.Checked; };
            menu.Items.Add(toggle);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("ERQuit 끝내기 (게임 유지)", null, delegate { ExitHelper(); });
            tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "ERQuit · 연결 준비 중", ContextMenuStrip = menu, Visible = !preview };
            tray.DoubleClick += delegate { ShowStatus(); };
            timer = new Timer { Interval = 200 };
            timer.Tick += Tick;
            Shown += delegate
            {
                if (preview) return;
                if (started) return;
                started = true;
                if (steamMode) StartSteam(); else Search();
                timer.Start();
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!preview && !closing && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            };
            FormClosed += delegate
            {
                closing = true;
                timer.Dispose();
                if (guard != null) { guard.Dispose(); guard = null; }
                tray.Visible = false;
                tray.Dispose();
                menu.Dispose();
            };
            if (steamMode) { WindowState = FormWindowState.Minimized; ShowInTaskbar = false; }
        }

        protected override bool ShowWithoutActivation { get { return preview; } }

        internal static string SteamOption(string executable) { return CommandLine.Quote(executable) + " --steam %command%"; }

        private Button MakeButton(string text, int x, int y, int width)
        {
            Button button = new Button { Text = text, Location = new Point(x, y), Size = new Size(width, 42),
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(42, 54, 70), ForeColor = ForeColor };
            button.FlatAppearance.BorderColor = Color.FromArgb(72, 91, 112);
            Controls.Add(button);
            return button;
        }

        private void ShowStatus() { ShowInTaskbar = true; Show(); WindowState = FormWindowState.Normal; Activate(); }
        private void ExitHelper() { closing = true; Close(); }

        private async void Search()
        {
            if (closing || busy || guard != null || steamMode) return;
            busy = true;
            connect.Enabled = false;
            GameConnection result = null;
            try
            {
                result = await Task.Run(() => GameConnection.FindRunning());
                if (closing) { if (result != null) result.Dispose(); return; }
                if (result == null)
                {
                    SetStatus("이터널 리턴 실행 대기 중", false);
                    if (gamePath.Text.Length == 0)
                    {
                        List<Installation> installations = await Task.Run(() => Installation.Discover());
                        if (!closing && installations.Count > 0) gamePath.Text = installations[0].Executable;
                    }
                }
                else { Connect(result); result = null; }
            }
            catch (Exception error)
            {
                if (result != null) result.Dispose();
                if (!closing) SetStatus(error.Message, false);
            }
            finally
            {
                busy = false;
                nextSearch = unchecked(Environment.TickCount + 3000);
                if (!closing) connect.Enabled = guard == null;
            }
        }

        private void StartSteam()
        {
            IntPtr thread = IntPtr.Zero;
            GameConnection connection = null;
            try
            {
                List<string> gameArguments = new List<string>();
                for (int i = 2; i < arguments.Length; i++) gameArguments.Add(arguments[i]);
                connection = GameConnection.Launch(arguments[1], gameArguments, out thread);
                Connect(connection);
                connection = null;
                uint resumed = Native.ResumeThread(thread);
                if (resumed == UInt32.MaxValue) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "게임 시작을 재개하지 못했습니다.");
                Native.CloseHandle(thread);
                thread = IntPtr.Zero;
                Hide();
            }
            catch (Exception error)
            {
                // Do not kill the game if helper setup fails.
                if (connection != null) connection.Dispose();
                if (guard != null) { guard.Dispose(); guard = null; }
                SetStatus(error.Message, false);
                ShowStatus();
            }
            finally
            {
                if (thread != IntPtr.Zero) { Native.ResumeThread(thread); Native.CloseHandle(thread); }
            }
        }

        private void Connect(GameConnection connection)
        {
            guard = new GameGuard(connection);
            guard.Enabled = toggle.Checked;
            gamePath.Text = guard.Executable;
            errorShown = false;
            SetStatus("연결됨 · 게임 창으로 전환하면 Alt+F4가 작동합니다.", true);
            connect.Enabled = false;
        }

        private void Tick(object sender, EventArgs args)
        {
            if (closing) return;
            if (guard == null)
            {
                if (!steamMode && !busy && unchecked(Environment.TickCount - nextSearch) >= 0) Search();
                return;
            }
            if (guard.LastError != 0 && !errorShown)
            {
                errorShown = true;
                SetStatus("게임 강제 종료가 거부되었습니다. Windows 오류 " + guard.LastError, false);
                tray.ShowBalloonTip(3500, "ERQuit", "게임 강제 종료가 거부되었습니다. 권한 상승이나 우회는 시도하지 않습니다.", ToolTipIcon.Warning);
            }
            if (!guard.Alive)
            {
                guard.ResetReleasedKey();
                if (guard.SuppressingPress) return;
                guard.Dispose();
                guard = null;
                if (steamMode) { ExitHelper(); return; }
                SetStatus("게임 종료됨 · 다음 실행 대기 중", false);
                connect.Enabled = true;
                nextSearch = unchecked(Environment.TickCount + 3000);
            }
        }

        private void SetStatus(string text, bool connected)
        {
            status.Text = text;
            status.ForeColor = connected ? Color.FromArgb(112, 225, 183) : Color.FromArgb(232, 192, 119);
            tray.Text = connected ? "ERQuit · 게임 연결됨" : "ERQuit · " + (text.Length > 50 ? text.Substring(0, 50) : text);
        }
    }
}
