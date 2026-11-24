// 자동 업데이트 핵심 기능 (AutoCAD에 의존하지 않음 → 테스트 프로그램에서도 같이 쓴다)
//  1) GitHub 최신 릴리스 확인 (api.github.com/repos/.../releases/latest)
//  2) zip을 받아 %LOCALAPPDATA%\CorridorSolids\update\<태그>\ 에 풀고 CorridorSolids.bundle을 찾는다
//  3) pending.txt 에 '태그, 받은 번들 경로'를 적고 apply.ps1을 숨김 창으로 띄운다
//     apply.ps1은 AutoCAD(acad.exe)가 모두 꺼질 때까지 기다렸다가 설치 폴더에 덮어쓴다 (켜져 있으면 dll이 잠겨 있으므로)
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace CorridorSolids
{
    public class ReleaseInfo
    {
        public string Tag = "";
        public Version Version;
        public string ZipUrl = "";
        public string Notes = "";
        public string PageUrl = "";
    }

    public static class UpdateCore
    {
        public const string Repo = "ryunhyun/civil3d-corsolid";
        public static string ReleasesPage => "https://github.com/" + Repo + "/releases/latest";

        // CORSOLID_DATADIR: 테스트용으로 다른 폴더를 쓸 때만
        public static string DataDir => Environment.GetEnvironmentVariable("CORSOLID_DATADIR") is string d && d != ""
            ? d : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CorridorSolids");
        public static string UpdateDir => Path.Combine(DataDir, "update");
        public static string StateFile => Path.Combine(DataDir, "update_state.txt");
        public static string LogFile => Path.Combine(DataDir, "update.log");
        public static string PendingFile => Path.Combine(UpdateDir, "pending.txt");
        public static string AppliedFile => Path.Combine(UpdateDir, "applied.txt");
        public static string ScriptFile => Path.Combine(UpdateDir, "apply.ps1");

        public static Version CurrentVersion => Norm(Assembly.GetExecutingAssembly().GetName().Version);

        public static Version Norm(Version v)
        {
            if (v == null) return new Version(0, 0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
        }

        // "v1.4" / "1.4.1" → Version
        public static Version ParseTag(string tag)
        {
            var s = (tag ?? "").Trim().TrimStart('v', 'V');
            return Version.TryParse(s, out var v) ? Norm(v) : null;
        }

        public static string Show(Version v) => v == null ? "?" : (v.Build > 0 ? $"v{v.Major}.{v.Minor}.{v.Build}" : $"v{v.Major}.{v.Minor}");

        public static void Log(string msg)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}\r\n", Encoding.UTF8);
            }
            catch { }
        }

        static HttpClient NewHttp(int timeoutSec)
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSec) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("CorridorSolids-Updater");
            return h;
        }

        // 최신 릴리스 정보. 실패하면 예외.
        public static ReleaseInfo GetLatest(int timeoutSec = 10)
        {
            using var http = NewHttp(timeoutSec);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            var json = http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest").GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var r = new ReleaseInfo
            {
                Tag = root.GetProperty("tag_name").GetString() ?? "",
                Notes = root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : "",
                PageUrl = root.TryGetProperty("html_url", out var u) ? u.GetString() : ReleasesPage,
            };
            r.Version = ParseTag(r.Tag);
            if (root.TryGetProperty("assets", out var assets))
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        r.ZipUrl = a.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
            return r;
        }

        public static bool IsNewer(ReleaseInfo r, Version current) =>
            r != null && r.Version != null && !string.IsNullOrEmpty(r.ZipUrl) && r.Version > Norm(current);

        // ---- 하루 한 번만 자동 확인 ----
        public static bool DueForCheck()
        {
            try
            {
                if (!File.Exists(StateFile)) return true;
                var line = File.ReadAllLines(StateFile).FirstOrDefault(l => l.StartsWith("lastcheck="));
                return line == null || line.Substring(10) != DateTime.Today.ToString("yyyy-MM-dd");
            }
            catch { return true; }
        }

        public static void MarkChecked()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(StateFile, "lastcheck=" + DateTime.Today.ToString("yyyy-MM-dd") + "\r\n");
            }
            catch { }
        }

        // ---- 받아 두기 ----
        // 반환: 받은 CorridorSolids.bundle 폴더 경로
        public static string Download(ReleaseInfo r, int timeoutSec = 60)
        {
            var dir = Path.Combine(UpdateDir, r.Tag);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var zip = Path.Combine(dir, "release.zip");
            using (var http = NewHttp(timeoutSec))
            {
                var bytes = http.GetByteArrayAsync(r.ZipUrl).GetAwaiter().GetResult();
                File.WriteAllBytes(zip, bytes);
            }
            var x = Path.Combine(dir, "x");
            ZipFile.ExtractToDirectory(zip, x);

            var bundle = Directory.GetFiles(x, "PackageContents.xml", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .FirstOrDefault(d => File.Exists(Path.Combine(d, "Contents", "CorridorSolids.dll")));
            if (bundle == null) throw new Exception("받은 zip 안에 CorridorSolids.bundle이 없습니다.");

            var got = Norm(AssemblyName.GetAssemblyName(Path.Combine(bundle, "Contents", "CorridorSolids.dll")).Version);
            if (r.Version != null && got != r.Version)
                throw new Exception($"받은 dll 버전({Show(got)})이 릴리스({Show(r.Version)})와 다릅니다.");
            return bundle;
        }

        // ---- 끄면 설치 ----
        public static string PendingTag()
        {
            try { return File.Exists(PendingFile) ? File.ReadAllLines(PendingFile, Encoding.UTF8).FirstOrDefault() : null; }
            catch { return null; }
        }

        public static void SetPending(string tag, string stagedBundle)
        {
            Directory.CreateDirectory(UpdateDir);
            File.WriteAllLines(PendingFile, new[] { tag, stagedBundle }, new UTF8Encoding(true));
        }

        // apply.ps1을 숨김으로 실행. 같은 스크립트가 이미 기다리고 있으면 스크립트 쪽에서 바로 끝난다(뮤텍스).
        public static void LaunchApply(string installBundle, string waitProcess = "acad")
        {
            Directory.CreateDirectory(UpdateDir);
            File.WriteAllText(ScriptFile, ApplyScript, new UTF8Encoding(true));
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", ScriptFile,
                                      "-Pending", PendingFile, "-Dst", installBundle, "-Proc", waitProcess,
                                      "-Applied", AppliedFile, "-Log", LogFile })
                psi.ArgumentList.Add(a);
            Process.Start(psi);
            Log($"apply 대기 시작: {installBundle} (프로세스 {waitProcess} 종료 대기)");
        }

        // 지난번 업데이트가 끝났으면 그 태그를 돌려주고 표시를 지운다
        public static string TakeApplied()
        {
            try
            {
                if (!File.Exists(AppliedFile)) return null;
                var t = File.ReadAllText(AppliedFile, Encoding.UTF8).Trim();
                File.Delete(AppliedFile);
                return t;
            }
            catch { return null; }
        }

        const string ApplyScript = @"param([string]$Pending, [string]$Dst, [string]$Proc = 'acad', [string]$Applied, [string]$Log)
# CorridorSolids 자동 업데이트: AutoCAD가 모두 꺼지면 받아 둔 번들을 설치 폴더에 덮어쓴다.
function W($m) { try { Add-Content -LiteralPath $Log -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $m) -Encoding UTF8 } catch {} }
$mx = New-Object System.Threading.Mutex($false, 'Local\CorridorSolidsUpdate')
try { $got = $mx.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $got = $true }
if (-not $got) { exit 0 }
try {
    $deadline = (Get-Date).AddDays(7)
    while (Get-Process -Name $Proc -ErrorAction SilentlyContinue) {
        if ((Get-Date) -gt $deadline) { W 'apply: 7일 동안 AutoCAD가 꺼지지 않아 중단'; exit 1 }
        Start-Sleep -Seconds 5
    }
    Start-Sleep -Seconds 2
    if (-not (Test-Path -LiteralPath $Pending)) { W 'apply: 받아 둔 업데이트 없음'; exit 0 }
    $lines = Get-Content -LiteralPath $Pending -Encoding UTF8
    $tag = $lines[0]; $src = $lines[1]
    if (-not (Test-Path -LiteralPath (Join-Path $src 'Contents\CorridorSolids.dll'))) {
        W ('apply: 받은 번들이 없음 ' + $src); Remove-Item -LiteralPath $Pending -Force; exit 1
    }
    robocopy $src $Dst /MIR /R:5 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
    $rc = $LASTEXITCODE
    if ($rc -ge 8) { W ('apply: 복사 실패 robocopy ' + $rc + ' (다음에 Civil 3D를 켜고 끄면 다시 시도)'); exit 1 }
    Get-ChildItem -LiteralPath $Dst -Recurse -File | Unblock-File -ErrorAction SilentlyContinue
    Set-Content -LiteralPath $Applied -Value $tag -Encoding UTF8
    Remove-Item -LiteralPath $Pending -Force
    W ('apply: ' + $tag + ' 설치 완료 → ' + $Dst)
} catch {
    W ('apply 오류: ' + $_)
} finally {
    $mx.ReleaseMutex()
}
";
    }
}
