// 코리더 솔리드 일괄 추출 설치 프로그램
//   실행하면 설치/제거 창이 뜬다.
//   명령행(테스트용): /install | /uninstall  [/target <번들폴더>]  [/log <파일>]   ,  /preview <png>
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CorridorSolidsSetup
{
    static class Core
    {
        public const string ReleasesPage = "https://github.com/ryunhyun/civil3d-corsolid/releases/latest";

        public static string DefaultTarget => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Autodesk", "ApplicationPlugins", "CorridorSolids.bundle");

        static string PendingFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CorridorSolids", "update", "pending.txt");

        static readonly Assembly Me = Assembly.GetExecutingAssembly();

        static string[] BundleResources => Me.GetManifestResourceNames().Where(n => n.StartsWith("bundle/")).ToArray();

        static string AppVersionOf(string xml)
        {
            var m = Regex.Match(xml ?? "", "AppVersion=\"([^\"]+)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        public static string Show(string v)
        {
            if (string.IsNullOrEmpty(v)) return null;
            var p = v.Split('.');
            return "v" + (p.Length >= 3 && p[2] != "0" ? string.Join(".", p.Take(3)) : string.Join(".", p.Take(2)));
        }

        public static string PackageVersion
        {
            get
            {
                using var s = Me.GetManifestResourceStream("bundle/PackageContents.xml");
                using var r = new StreamReader(s, Encoding.UTF8);
                return AppVersionOf(r.ReadToEnd());
            }
        }

        public static string InstalledVersion(string target)
        {
            try
            {
                var f = Path.Combine(target, "PackageContents.xml");
                return File.Exists(f) ? AppVersionOf(File.ReadAllText(f, Encoding.UTF8)) ?? "?" : null;
            }
            catch { return "?"; }
        }

        public static bool AcadRunning => Process.GetProcessesByName("acad").Length > 0;

        public static bool Civil3D2026Present =>
            Directory.Exists(@"C:\Program Files\Autodesk\AutoCAD 2026\C3D");

        public static Icon AppIcon()
        {
            using var s = Me.GetManifestResourceStream("corsolid.ico");
            return s == null ? null : new Icon(s);
        }

        public static void Install(string target)
        {
            if (Directory.Exists(target)) Directory.Delete(target, true);
            foreach (var name in BundleResources)
            {
                var rel = name.Substring("bundle/".Length).Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                var dst = Path.Combine(target, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                using var src = Me.GetManifestResourceStream(name);
                using var fs = File.Create(dst);
                src.CopyTo(fs);
            }
            ClearPendingUpdate();
        }

        public static void Uninstall(string target)
        {
            if (Directory.Exists(target)) Directory.Delete(target, true);
            ClearPendingUpdate();
        }

        // 자동 업데이트로 받아 두고 아직 설치 안 된 것이 있으면 지운다 (직접 설치한 버전을 나중에 덮어쓰지 않도록)
        static void ClearPendingUpdate()
        {
            try { if (File.Exists(PendingFile)) File.Delete(PendingFile); } catch { }
        }
    }

    class SetupForm : Form
    {
        readonly string target;
        readonly Label lblInstalled, lblStatus;
        readonly Button btnInstall, btnRemove, btnClose;
        readonly Timer timer;
        bool done;

        public SetupForm(string target)
        {
            this.target = target;
            Text = "코리더 솔리드 일괄 추출 설치";
            Font = new Font("맑은 고딕", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(500, 296);
            BackColor = Color.White;
            var ico = Core.AppIcon();
            if (ico != null) Icon = ico;

            var pic = new PictureBox
            {
                Location = new Point(20, 20), Size = new Size(64, 64), SizeMode = PictureBoxSizeMode.Zoom,
                Image = ico != null ? new Icon(ico, 64, 64).ToBitmap() : null,
            };
            var title = new Label
            {
                Text = "코리더 솔리드 일괄 추출", Font = new Font("맑은 고딕", 14f, FontStyle.Bold),
                Location = new Point(100, 20), AutoSize = true,
            };
            var ver = new Label
            {
                Text = $"설치할 버전 {Core.Show(Core.PackageVersion)}   ·   Civil 3D 2026 전용",
                ForeColor = Color.DimGray, Location = new Point(102, 52), AutoSize = true,
            };
            lblInstalled = new Label { Location = new Point(102, 72), AutoSize = true, ForeColor = Color.DimGray };
            var desc = new Label
            {
                Text = "도면의 여러 코리더에서 솔리드를 한 번에 추출하는 Civil 3D 플러그인입니다.\n" +
                       "설치 후 Civil 3D 명령행에 CORSOLID 를 입력하면 코리더 목록 창이 뜹니다.\n" +
                       "새 버전이 나오면 Civil 3D를 켤 때 자동으로 알려 줍니다.",
                Location = new Point(22, 104), Size = new Size(460, 54),
            };
            var where = new Label
            {
                Text = "설치 위치: " + target, ForeColor = Color.Gray, Font = new Font("맑은 고딕", 8f),
                Location = new Point(22, 164), Size = new Size(460, 30), AutoEllipsis = true,
            };
            lblStatus = new Label { Location = new Point(22, 198), Size = new Size(460, 40) };
            var link = new LinkLabel { Text = "사용법 · 새 버전 (GitHub)", Location = new Point(22, 258), AutoSize = true };
            link.LinkClicked += (s, e) => { try { Process.Start(new ProcessStartInfo(Core.ReleasesPage) { UseShellExecute = true }); } catch { } };

            btnInstall = new Button { Location = new Point(222, 250), Size = new Size(84, 30) };
            btnRemove = new Button { Text = "제거", Location = new Point(312, 250), Size = new Size(84, 30) };
            btnClose = new Button { Text = "닫기", Location = new Point(402, 250), Size = new Size(84, 30), DialogResult = DialogResult.Cancel };
            btnInstall.Click += (s, e) => Run(true);
            btnRemove.Click += (s, e) => Run(false);
            CancelButton = btnClose;
            AcceptButton = btnInstall;

            Controls.AddRange(new Control[] { pic, title, ver, lblInstalled, desc, where, lblStatus, link, btnInstall, btnRemove, btnClose });

            timer = new Timer { Interval = 2000 };
            timer.Tick += (s, e) => { if (!done) Refresh2(); };
            timer.Start();
            Refresh2();
        }

        void Refresh2()
        {
            var inst = Core.InstalledVersion(target);
            var pkg = Core.PackageVersion;
            lblInstalled.Text = "지금 설치된 버전: " + (inst == null ? "없음" : Core.Show(inst));
            btnInstall.Text = inst == null ? "설치" : (inst == pkg ? "다시 설치" : "업데이트");
            btnRemove.Enabled = inst != null;

            if (Core.AcadRunning)
            {
                Status("Civil 3D(AutoCAD)가 켜져 있습니다. 모두 끈 뒤 설치하세요.\n(켜져 있는 동안은 파일이 잠겨 있어서 바꿀 수 없습니다)", Color.OrangeRed);
                btnInstall.Enabled = false; btnRemove.Enabled = false;
            }
            else
            {
                btnInstall.Enabled = true;
                if (!Core.Civil3D2026Present)
                    Status("이 PC에서 Civil 3D 2026을 찾지 못했습니다. 다른 버전에서는 동작하지 않습니다.", Color.DarkGoldenrod);
                else
                    Status("", Color.Black);
            }
        }

        void Status(string s, Color c) { lblStatus.Text = s; lblStatus.ForeColor = c; }

        void Run(bool install)
        {
            if (Core.AcadRunning) { Refresh2(); return; }
            if (!install && MessageBox.Show(this, "코리더 솔리드 일괄 추출을 제거할까요?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                Cursor = Cursors.WaitCursor;
                if (install) Core.Install(target); else Core.Uninstall(target);
                done = true;
                Refresh2Labels();
                if (install)
                    Status($"{Core.Show(Core.PackageVersion)} 설치를 마쳤습니다. Civil 3D를 켜고 CORSOLID 를 입력하세요.\n처음 켤 때 보안 경고가 나오면 [항상 로드]를 누르세요.", Color.SeaGreen);
                else
                    Status("제거했습니다. 다음에 Civil 3D를 켜면 CORSOLID 명령이 없어집니다.", Color.SeaGreen);
                btnInstall.Enabled = false; btnRemove.Enabled = false;
                btnClose.Text = "마침"; AcceptButton = btnClose; btnClose.Focus();
            }
            catch (Exception ex)
            {
                Status((install ? "설치" : "제거") + " 실패: " + ex.Message, Color.Red);
            }
            finally { Cursor = Cursors.Default; }
        }

        void Refresh2Labels()
        {
            var inst = Core.InstalledVersion(target);
            lblInstalled.Text = "지금 설치된 버전: " + (inst == null ? "없음" : Core.Show(inst));
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            string Arg(string name)
            {
                var i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            bool Has(string name) => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

            var target = Arg("/target") ?? Core.DefaultTarget;
            var log = Arg("/log");

            if (Has("/install") || Has("/uninstall"))
            {
                try
                {
                    // 실제 설치 위치일 때만 AutoCAD 실행 여부를 본다 (/target으로 다른 폴더에 시험 설치할 때는 건너뜀)
                    if (string.Equals(Path.GetFullPath(target).TrimEnd('\\'), Core.DefaultTarget, StringComparison.OrdinalIgnoreCase) && Core.AcadRunning)
                        throw new Exception("AutoCAD is running");
                    if (Has("/install")) Core.Install(target); else Core.Uninstall(target);
                    if (log != null) File.WriteAllText(log, "OK installed=" + (Core.InstalledVersion(target) ?? "none"));
                    return 0;
                }
                catch (Exception ex)
                {
                    if (log != null) File.WriteAllText(log, "FAIL " + ex.Message);
                    return 1;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            var form = new SetupForm(target);

            var preview = Arg("/preview");
            if (preview != null)
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-3000, -3000);
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();
                using var bmp = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(preview, System.Drawing.Imaging.ImageFormat.Png);
                return 0;
            }

            Application.Run(form);
            return 0;
        }
    }
}
