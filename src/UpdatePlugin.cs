// 자동 업데이트 (AutoCAD 쪽): Civil 3D를 켤 때 하루 한 번 GitHub 최신 릴리스를 확인하고,
// 새 버전이 있으면 물어본 뒤 받아 둔다. 설치는 Civil 3D를 모두 끈 뒤 apply.ps1이 한다(UpdateCore 참고).
// CORSOLIDUPDATE: 지금 바로 확인
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcCore = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using WinForms = System.Windows.Forms;

[assembly: ExtensionApplication(typeof(CorridorSolids.UpdatePlugin))]
[assembly: CommandClass(typeof(CorridorSolids.UpdateCmds))]

namespace CorridorSolids
{
    public class UpdatePlugin : IExtensionApplication
    {
        static readonly ConcurrentQueue<Action> ui = new ConcurrentQueue<Action>();

        // 설치된 번들(...\CorridorSolids.bundle\Contents\CorridorSolids.dll)에서 불러온 경우만 번들 폴더, 아니면 null(NETLOAD 개발용)
        public static string InstallBundle
        {
            get
            {
                try
                {
                    var dll = typeof(UpdatePlugin).Assembly.Location;
                    var b = Path.GetDirectoryName(Path.GetDirectoryName(dll));
                    return b != null && Path.GetFileName(b).Equals("CorridorSolids.bundle", StringComparison.OrdinalIgnoreCase) ? b : null;
                }
                catch { return null; }
            }
        }

        public void Initialize()
        {
            try
            {
                AcCore.Idle += OnIdle;
                var applied = UpdateCore.TakeApplied();
                if (applied != null)
                    Post(() => Msg($"\n[코리더 솔리드] {applied}(으)로 업데이트되었습니다. 바뀐 내용: {UpdateCore.ReleasesPage}\n"));

                if (InstallBundle == null) return;
                // 받아 두었는데 아직 설치 안 된 것(PC를 껐다 켠 경우 등) → 다시 '끄면 설치' 대기
                if (UpdateCore.PendingTag() != null) UpdateCore.LaunchApply(InstallBundle);
                if (UpdateCore.DueForCheck()) Task.Run(AutoCheck);
            }
            catch (System.Exception ex) { UpdateCore.Log("Initialize: " + ex.Message); }
        }

        public void Terminate() { }

        // 메인 스레드에서 할 일 (도면이 열려 있을 때 처리)
        public static void Post(Action a) => ui.Enqueue(a);

        static void OnIdle(object sender, EventArgs e)
        {
            if (ui.IsEmpty || AcCore.DocumentManager.MdiActiveDocument == null) return;
            while (ui.TryDequeue(out var a))
            {
                try { a(); } catch (System.Exception ex) { UpdateCore.Log("ui: " + ex.Message); }
            }
        }

        public static void Msg(string s) => AcCore.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(s);

        static void AutoCheck()
        {
            try
            {
                var r = UpdateCore.GetLatest();
                UpdateCore.MarkChecked();
                var cur = UpdateCore.CurrentVersion;
                if (!UpdateCore.IsNewer(r, cur)) return;
                if (UpdateCore.PendingTag() == r.Tag)
                {
                    Post(() => Msg($"\n[코리더 솔리드] {r.Tag}를 받아 두었습니다. Civil 3D를 모두 끄면 설치됩니다.\n"));
                    return;
                }
                Post(() => Ask(r, cur));
            }
            catch (System.Exception ex) { UpdateCore.Log("자동 확인 실패: " + ex.Message); }
        }

        class Owner : WinForms.IWin32Window { public IntPtr Handle { get; set; } }

        // 새 버전 알림 창 → 예: 받아 두고 '끄면 설치' 대기
        public static void Ask(ReleaseInfo r, Version cur)
        {
            var notes = string.Join("\n", (r.Notes ?? "").Replace("\r", "").Split('\n')
                                         .Select(l => l.TrimEnd()).Where(l => l != "").Take(12));
            if (notes.Length > 700) notes = notes.Substring(0, 700) + " …";
            var text = "코리더 솔리드 일괄 추출(CORSOLID) 새 버전이 있습니다.\n\n" +
                       $"지금 {UpdateCore.Show(cur)}   →   새 버전 {UpdateCore.Show(r.Version)}\n" +
                       (notes != "" ? "\n" + notes + "\n" : "") +
                       "\n지금 받아 두고, Civil 3D를 모두 끄면 자동으로 설치할까요?\n" +
                       "(다음에 Civil 3D를 켤 때부터 새 버전이 적용됩니다. '아니요'면 내일 다시 알려 줍니다.)";
            WinForms.DialogResult res;
            try
            {
                res = WinForms.MessageBox.Show(new Owner { Handle = AcApp.MainWindow.Handle }, text, "코리더 솔리드 업데이트",
                    WinForms.MessageBoxButtons.YesNo, WinForms.MessageBoxIcon.Information);
            }
            catch
            {
                res = WinForms.MessageBox.Show(text, "코리더 솔리드 업데이트", WinForms.MessageBoxButtons.YesNo, WinForms.MessageBoxIcon.Information);
            }
            if (res != WinForms.DialogResult.Yes) return;

            var install = InstallBundle;
            if (install == null)
            {
                Msg($"\n[코리더 솔리드] 설치된 번들에서 불러온 것이 아니라(NETLOAD) 자동 설치를 할 수 없습니다. 직접 받으세요: {UpdateCore.ReleasesPage}\n");
                return;
            }
            Msg($"\n[코리더 솔리드] {r.Tag} 받는 중...\n");
            Task.Run(() =>
            {
                try
                {
                    var staged = UpdateCore.Download(r);
                    UpdateCore.SetPending(r.Tag, staged);
                    UpdateCore.LaunchApply(install);
                    UpdateCore.Log($"받음: {r.Tag} → {staged}");
                    Post(() => Msg($"\n[코리더 솔리드] {r.Tag}를 받았습니다. Civil 3D를 모두 끄면 자동으로 설치되고, 다음에 켤 때부터 적용됩니다.\n"));
                }
                catch (System.Exception ex)
                {
                    UpdateCore.Log("받기 실패: " + ex.Message);
                    Post(() => Msg($"\n[코리더 솔리드] 받기 실패: {ex.Message}\n  직접 받으세요: {UpdateCore.ReleasesPage}\n"));
                }
            });
        }
    }

    public class UpdateCmds
    {
        [CommandMethod("CORSOLIDUPDATE", CommandFlags.Modal)]
        public void CheckNow()
        {
            var cur = UpdateCore.CurrentVersion;
            UpdatePlugin.Msg($"\n코리더 솔리드 일괄 추출 현재 {UpdateCore.Show(cur)}. GitHub에서 최신 버전 확인 중...");
            ReleaseInfo r;
            try
            {
                r = UpdateCore.GetLatest(15);
                UpdateCore.MarkChecked();
            }
            catch (System.Exception ex)
            {
                UpdatePlugin.Msg($"\n확인 실패: {ex.Message}\n  인터넷 연결을 확인하거나 직접 받으세요: {UpdateCore.ReleasesPage}\n");
                return;
            }
            if (!UpdateCore.IsNewer(r, cur))
            {
                UpdatePlugin.Msg($"\n최신 버전입니다. (GitHub 최신: {UpdateCore.Show(r.Version)})\n");
                return;
            }
            if (UpdateCore.PendingTag() == r.Tag)
            {
                UpdatePlugin.Msg($"\n{r.Tag}를 이미 받아 두었습니다. Civil 3D를 모두 끄면 설치됩니다.\n");
                if (UpdatePlugin.InstallBundle != null) UpdateCore.LaunchApply(UpdatePlugin.InstallBundle);
                return;
            }
            UpdatePlugin.Ask(r, cur);
        }
    }
}
