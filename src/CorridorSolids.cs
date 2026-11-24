// 코리더 솔리드 일괄 추출 (Civil 3D 2026)
//   CORSOLID  : 코리더 목록 창에서 골라 한 번에 추출 (CorridorDialog.cs)
//   -CORSOLID : 같은 기능의 명령행 버전
//   CORLIST   : 도면의 코리더 목록과 최신 상태를 보여 준다.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

[assembly: CommandClass(typeof(CorridorSolids.Cmds))]

namespace CorridorSolids
{
    public class Result
    {
        public string Name;
        public int Count;
        public bool Rebuilt;
        public bool StillOutOfDate;
        public string Error;
    }

    public class Cmds
    {
        // ---------- 공통 ----------

        public static List<(ObjectId Id, string Name, bool OutOfDate)> GetCorridors(Database db)
        {
            var list = new List<(ObjectId, string, bool)>();
            var civ = CivilApplication.ActiveDocument;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in civ.CorridorCollection)
                {
                    var c = (Corridor)tr.GetObject(id, OpenMode.ForRead);
                    list.Add((id, c.Name, c.IsOutOfDate));
                }
                tr.Commit();
            }
            return list.OrderBy(c => c.Item2, new NaturalComparer()).ToList();
        }

        static List<ObjectId> Match(List<(ObjectId Id, string Name, bool OutOfDate)> all, string input)
            => NameMatch.MatchIndexes(all.Select(c => c.Name).ToList(), input).Select(i => all[i].Id).ToList();

        // db: 코리더가 있는 도면, target: 솔리드를 넣을 도면 (같을 수도 있음)
        public static List<Result> Extract(Database db, Database target, IList<ObjectId> ids, Options o, Action<string> log)
        {
            var results = new List<Result>();
            var p = new ExportCorridorSolidsParams
            {
                ExportShapes = o.Shapes,
                ExportLinks = o.Links,
                CreateSolidForShape = o.Type != OutputType.Body,
                SweepSolidForShape = o.Type == OutputType.SweepSolid,
                IncludedCodes = new string[0],
                ExcludedCodes = o.Excluded,
            };
            var pm = new ProgressMeter();
            pm.SetLimit(ids.Count);
            pm.Start("코리더 솔리드 추출");
            try
            {
                int i = 0;
                foreach (var id in ids)
                {
                    i++;
                    var r = new Result();
                    try
                    {
                        bool trimmed = false;
                        // 1) 영역 측점 조정은 먼저 따로 커밋해야 다시 만들기에 반영된다
                        if (o.TrimRegions && o.TrimAmount > 0)
                        {
                            using (var tr = db.TransactionManager.StartTransaction())
                            {
                                var c = (Corridor)tr.GetObject(id, OpenMode.ForWrite);
                                r.Name = c.Name;
                                log(string.Format("\n[{0}/{1}] {2} ...", i, ids.Count, r.Name));
                                TrimRegions(c, o.TrimAmount, out int nChanged, out int nShort, out int nFail);
                                log(string.Format(" 영역 {0}개 측점 조정", nChanged)
                                    + (nShort > 0 ? ", 너무 짧아 건너뜀 " + nShort + "개" : "")
                                    + (nFail > 0 ? ", 실패 " + nFail + "개" : "") + ",");
                                trimmed = nChanged > 0;
                                tr.Commit();
                            }
                        }
                        // 2) 다시 만들기 + 추출
                        // 2) 다시 만들기 (따로 커밋해야 추출에 새 형상이 나온다)
                        using (var tr = db.TransactionManager.StartTransaction())
                        {
                            var c = (Corridor)tr.GetObject(id, OpenMode.ForRead);
                            if (r.Name == null)
                            {
                                r.Name = c.Name;
                                log(string.Format("\n[{0}/{1}] {2} ...", i, ids.Count, r.Name));
                            }
                            // 측점을 바꿨으면 다시 만들어야 솔리드에 반영된다
                            if (trimmed || o.ForceRebuild || (o.Rebuild && c.IsOutOfDate))
                            {
                                c = (Corridor)tr.GetObject(id, OpenMode.ForWrite);
                                c.Rebuild();
                                r.Rebuilt = true;
                            }
                            tr.Commit();
                        }
                        // 3) 추출
                        using (var tr = db.TransactionManager.StartTransaction())
                        {
                            var c = (Corridor)tr.GetObject(id, OpenMode.ForRead);
                            r.StillOutOfDate = c.IsOutOfDate;
                            var made = c.ExportSolids(p, target);
                            r.Count = made == null ? 0 : made.Count;
                            tr.Commit();
                        }
                        log(" 솔리드 " + r.Count + "개" + (r.Rebuilt ? " (다시 만든 뒤 추출)" : "")
                            + (r.StillOutOfDate ? " [주의: 아직 최신 아님]" : ""));
                    }
                    catch (System.Exception ex)
                    {
                        r.Error = ex.Message;
                        log(" 실패: " + ex.Message);
                    }
                    results.Add(r);
                    pm.MeterProgress();
                    if (HostApplicationServices.Current.UserBreak())
                    {
                        log("\nEsc로 중단했습니다. 여기까지 추출한 솔리드는 남아 있습니다.");
                        break;
                    }
                }
            }
            finally { pm.Stop(); }
            return results;
        }

        // 코리더의 모든 기준선, 모든 영역: 시작 측점 +amt, 끝 측점 -amt. 길이가 2*amt 이하인 영역은 건너뛴다.
        public static void TrimRegions(Corridor c, double amt, out int changed, out int tooShort, out int failed)
        {
            changed = tooShort = failed = 0;
            foreach (Baseline bl in c.Baselines)
            {
                foreach (BaselineRegion rg in bl.BaselineRegions)
                {
                    try
                    {
                        double s = rg.StartStation, e = rg.EndStation;
                        if (e - s <= 2 * amt + 1e-6) { tooShort++; continue; }
                        rg.StartStation = s + amt;
                        rg.EndStation = e - amt;
                        changed++;
                    }
                    catch { failed++; }
                }
            }
        }

        static void Summary(List<Result> rs, Action<string> log)
        {
            int ok = rs.Count(r => r.Error == null);
            log(string.Format("\n\n완료: 코리더 {0}개 중 {1}개 추출, 솔리드 합계 {2}개, 다시 만든 코리더 {3}개.",
                rs.Count, ok, rs.Sum(r => r.Count), rs.Count(r => r.Rebuilt)));
            var bad = rs.Where(r => r.Error != null).ToList();
            if (bad.Count > 0)
            {
                log("\n실패한 코리더:");
                foreach (var r in bad) log("\n  " + r.Name + " : " + r.Error);
            }
            var empty = rs.Where(r => r.Error == null && r.Count == 0).ToList();
            if (empty.Count > 0)
                log("\n솔리드가 0개 나온 코리더: " + string.Join(", ", empty.Select(r => r.Name))
                    + (empty.Any(r => r.Rebuilt) ? "\n  → 다시 만든 뒤 0개가 나왔습니다. 코리더 상태를 확인하세요 (현재 도면이면 U로 되돌릴 수 있음)." : ""));
        }

        // ---------- CORLIST ----------

        [CommandMethod("CORLIST", CommandFlags.Modal)]
        public static void CorList()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var all = GetCorridors(doc.Database);
            ed.WriteMessage("\n코리더 " + all.Count + "개 (최신 아님 " + all.Count(c => c.OutOfDate) + "개)");
            foreach (var c in all) ed.WriteMessage("\n  " + (c.OutOfDate ? "[최신 아님] " : "            ") + c.Name);
        }

        // ---------- CORSOLID ----------

        // 명령행 버전 (대화상자 버전은 CorridorDialog.cs의 CORSOLID)
        [CommandMethod("-CORSOLID", CommandFlags.Modal)]
        public static void CorSolidCli()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;
            var all = GetCorridors(db);
            if (all.Count == 0) { ed.WriteMessage("\n이 도면에는 코리더가 없습니다."); return; }

            // 1) 대상 고르기
            var kw = new PromptKeywordOptions("\n코리더 " + all.Count + "개가 있습니다. 대상 선택");
            kw.Keywords.Add("All", "All", "전체(A)");
            kw.Keywords.Add("Name", "Name", "이름으로(N)");
            kw.Keywords.Add("Pick", "Pick", "화면에서(P)");
            kw.Keywords.Default = "All";
            kw.AllowNone = true;
            var kr = ed.GetKeywords(kw);
            if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return;
            string mode = kr.Status == PromptStatus.None ? "All" : kr.StringResult;

            List<ObjectId> targets;
            string lastInput = null;
            if (mode == "Name")
            {
                var ps = new PromptStringOptions("\n코리더 이름 (이름 일부만 써도 됨. * = 여러 글자, ? = 한 글자, 쉼표로 여러 개. 예: 유지보수도로): ");
                ps.AllowSpaces = true;
                var pr = ed.GetString(ps);
                if (pr.Status != PromptStatus.OK || pr.StringResult.Trim() == "") return;
                lastInput = pr.StringResult;
                targets = Match(all, pr.StringResult);
            }
            else if (mode == "Pick")
            {
                var so = new PromptSelectionOptions { MessageForAdding = "\n코리더 선택: " };
                var sr = ed.GetSelection(so, new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "AECC_CORRIDOR") }));
                if (sr.Status != PromptStatus.OK) return;
                targets = sr.Value.GetObjectIds().ToList();
            }
            else targets = all.Select(c => c.Id).ToList();

            if (targets.Count == 0)
            {
                ed.WriteMessage("\n조건에 맞는 코리더가 없습니다" + (lastInput != null ? " (입력: '" + lastInput + "')" : "") + ".");
                ed.WriteMessage("\n도면의 코리더 이름 예: " + string.Join(", ", all.Take(5).Select(c => c.Name)) + (all.Count > 5 ? " ..." : "") + "  (전체 목록은 CORLIST)");
                return;
            }
            var set = new HashSet<ObjectId>(targets);
            var chosen = all.Where(c => set.Contains(c.Id)).ToList();
            int outdated = chosen.Count(c => c.OutOfDate);
            ed.WriteMessage("\n대상 코리더 " + chosen.Count + "개: " + string.Join(", ", chosen.Take(10).Select(c => c.Name)) + (chosen.Count > 10 ? " 외 " + (chosen.Count - 10) + "개" : ""));

            // 2) 옵션
            var o = new Options();
            if (outdated > 0)
            {
                var rb = new PromptKeywordOptions("\n최신이 아닌 코리더가 " + outdated + "개 있습니다. 다시 만든 뒤 추출할까요?");
                rb.Keywords.Add("Yes", "Yes", "예(Y)");
                rb.Keywords.Add("No", "No", "아니오(N)");
                rb.Keywords.Default = "Yes";
                rb.AllowNone = true;
                var rr = ed.GetKeywords(rb);
                if (rr.Status != PromptStatus.OK && rr.Status != PromptStatus.None) return;
                o.Rebuild = rr.Status == PromptStatus.None || rr.StringResult == "Yes";
            }
            while (true)
            {
                var ok = new PromptKeywordOptions(string.Format(
                    "\n추출 설정 - 유형:{0}  쉐이프:{1}  링크:{2}  제외 코드:{3}  (출력: 현재 도면)",
                    Options.TypeNames[(int)o.Type], o.Shapes ? "예" : "아니오", o.Links ? "예" : "아니오",
                    o.Excluded.Length == 0 ? "없음" : string.Join(",", o.Excluded)));
                ok.Keywords.Add("Go", "Go", "추출 시작(G)");
                ok.Keywords.Add("Type", "Type", "유형 바꾸기(T)");
                ok.Keywords.Add("Shape", "Shape", "쉐이프(S)");
                ok.Keywords.Add("Link", "Link", "링크(L)");
                ok.Keywords.Add("Exclude", "Exclude", "제외 코드(E)");
                ok.Keywords.Default = "Go";
                ok.AllowNone = true;
                var or = ed.GetKeywords(ok);
                if (or.Status != PromptStatus.OK && or.Status != PromptStatus.None) return;
                string k = or.Status == PromptStatus.None ? "Go" : or.StringResult;
                if (k == "Go") break;
                if (k == "Shape") o.Shapes = !o.Shapes;
                else if (k == "Link") o.Links = !o.Links;
                else if (k == "Type") o.Type = (OutputType)(((int)o.Type + 1) % 3);
                else if (k == "Exclude")
                {
                    var es = new PromptStringOptions("\n제외할 코드 이름 (쉼표로 구분, 비우려면 Enter): ") { AllowSpaces = true };
                    var er = ed.GetString(es);
                    if (er.Status == PromptStatus.OK)
                        o.Excluded = er.StringResult.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s != "").ToArray();
                }
            }
            if (!o.Shapes && !o.Links) { ed.WriteMessage("\n쉐이프와 링크가 모두 '아니오'라 추출할 것이 없습니다."); return; }

            // 3) 추출
            RunAndSelect(ed, db, chosen.Select(c => c.Id).ToList(), o);
        }

        // 추출 실행 → 결과 요약. 현재 도면이면 새 솔리드를 선택 상태로, 다른 도면이면 저장 (대화상자/명령행 공통)
        public static void RunAndSelect(Editor ed, Database db, IList<ObjectId> ids, Options o)
        {
            ed.WriteMessage("\n추출을 시작합니다. 중간에 멈추려면 Esc를 누르세요.");
            if (o.Target != OutputTarget.Current)
            {
                try
                {
                    var rs2 = ExportToFile(db, ids, o, s => ed.WriteMessage(s), out string saved);
                    Summary(rs2, s => ed.WriteMessage(s));
                    ed.WriteMessage("\n저장한 도면: " + saved);
                    if (o.Target == OutputTarget.Existing) ed.WriteMessage("\n(추출 전 도면은 같은 폴더에 .bak 파일로 남겨 두었습니다.)");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage("\n추출하지 못했습니다: " + ex.Message);
                }
                return;
            }
            ObjectId before = Last(db);
            var rs = Extract(db, db, ids, o, s => ed.WriteMessage(s));
            Summary(rs, s => ed.WriteMessage(s));
            var made = After(db, before);
            if (made.Count > 0)
            {
                ed.SetImpliedSelection(made.ToArray());
                ed.WriteMessage("\n새로 만든 솔리드 " + made.Count + "개가 선택되어 있습니다. 잘못됐으면 U로 되돌릴 수 있습니다.");
            }
        }

        // 기존 도면에 추가 / 새 도면에 추가. 대상 도면은 AutoCAD 화면에 열지 않고 뒤에서 열어 저장한다.
        public static List<Result> ExportToFile(Database src, IList<ObjectId> ids, Options o, Action<string> log, out string path)
        {
            path = Path.GetFullPath(o.TargetPath ?? "");
            if (!path.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)) path += ".dwg";
            if (!string.IsNullOrEmpty(src.Filename) && string.Equals(Path.GetFullPath(src.Filename), path, StringComparison.OrdinalIgnoreCase))
                throw new System.Exception("대상 도면이 지금 코리더 도면과 같습니다. '현재 도면에 삽입'을 고르세요.");
            foreach (Autodesk.AutoCAD.ApplicationServices.Document d in Application.DocumentManager)
                if (string.Equals(d.Name, path, StringComparison.OrdinalIgnoreCase))
                    throw new System.Exception("대상 도면이 AutoCAD에 열려 있습니다. 닫은 뒤 다시 하세요: " + path);

            bool existing = o.Target == OutputTarget.Existing;
            if (existing && !File.Exists(path)) throw new System.Exception("기존 도면을 찾을 수 없습니다: " + path);
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            List<Result> rs;
            using (var target = new Database(!existing, true))
            {
                if (existing)
                {
                    // 다른 사람이 열고 있으면 여기서 실패한다
                    target.ReadDwgFile(path, FileOpenMode.OpenForReadAndWriteNoShare, true, "");
                    target.CloseInput(true);
                    File.Copy(path, Path.ChangeExtension(path, ".bak"), true);
                }
                else
                {
                    target.Insunits = src.Insunits;
                    target.Measurement = src.Measurement;
                    target.Lunits = src.Lunits;
                    target.Luprec = src.Luprec;
                }
                var prev = HostApplicationServices.WorkingDatabase;
                try
                {
                    rs = Extract(src, target, ids, o, log);
                }
                finally { HostApplicationServices.WorkingDatabase = prev; }
                target.SaveAs(path, DwgVersion.Current);
            }
            return rs;
        }

        // 테스트용 (accoreconsole): 환경변수 CORSOLID_FILTER(이름 패턴, 비우면 전체), CORSOLID_REBUILD(Y/N), CORSOLID_LOG
        [CommandMethod("CORSOLIDTEST")]
        public static void CorSolidTest()
        {
            var db = HostApplicationServices.WorkingDatabase;
            var sb = new StringBuilder();
            var all = GetCorridors(db);
            sb.AppendLine("CORRIDORS " + all.Count + " OUTOFDATE " + all.Count(c => c.OutOfDate));
            foreach (var c in all) sb.AppendLine("C\t" + c.Name + "\t" + (c.OutOfDate ? "OUTOFDATE" : "OK"));
            var f = Environment.GetEnvironmentVariable("CORSOLID_FILTER") ?? "";
            var ids = f == "" ? all.Select(c => c.Id).ToList() : Match(all, f);
            var rbEnv = Environment.GetEnvironmentVariable("CORSOLID_REBUILD") ?? "N";
            var o = new Options { Rebuild = rbEnv == "Y", ForceRebuild = rbEnv == "F" };
            o.Type = (OutputType)int.Parse(Environment.GetEnvironmentVariable("CORSOLID_TYPE") ?? "0");
            o.Links = (Environment.GetEnvironmentVariable("CORSOLID_LINKS") ?? "N") == "Y";
            var trimEnv = Environment.GetEnvironmentVariable("CORSOLID_TRIM") ?? "";
            if (trimEnv != "") { o.TrimRegions = true; o.TrimAmount = double.Parse(trimEnv, System.Globalization.CultureInfo.InvariantCulture); }
            // 영역 측점 기록 (조정 전)
            Action<string> dumpRegions = tag =>
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (var id in ids)
                    {
                        var c = (Corridor)tr.GetObject(id, OpenMode.ForRead);
                        foreach (Baseline bl in c.Baselines)
                            foreach (BaselineRegion rg in bl.BaselineRegions)
                            {
                                int nst = -1; try { nst = rg.SortedStations().Length; } catch { }
                                sb.AppendLine(string.Format("REG\t{0}\t{1}\t{2}\t{3:F4}\t{4:F4}\tstations={5}\toutofdate={6}", tag, c.Name, rg.Name, rg.StartStation, rg.EndStation, nst, c.IsOutOfDate));
                            }
                    }
                    tr.Commit();
                }
            };
            if (o.TrimRegions) dumpRegions("BEFORE");
            var tg = Environment.GetEnvironmentVariable("CORSOLID_TARGET") ?? "current";
            var t0 = DateTime.Now;
            if (tg != "current")
            {
                o.Target = tg == "new" ? OutputTarget.New : OutputTarget.Existing;
                o.TargetPath = Environment.GetEnvironmentVariable("CORSOLID_PATH");
                try
                {
                    var rsf = ExportToFile(db, ids, o, s => sb.Append(s), out string saved);
                    Summary(rsf, s => sb.Append(s));
                    sb.AppendLine("\nSAVED\t" + saved);
                    // 저장된 도면을 다시 읽어서 확인
                    using (var chk = new Database(false, true))
                    {
                        chk.ReadDwgFile(saved, FileOpenMode.OpenForReadAndAllShare, true, "");
                        using (var tr = chk.TransactionManager.StartTransaction())
                        {
                            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(chk), OpenMode.ForRead);
                            var cnt = new SortedDictionary<string, int>();
                            foreach (ObjectId id in ms)
                            {
                                var e = (Autodesk.AutoCAD.DatabaseServices.Entity)tr.GetObject(id, OpenMode.ForRead);
                                string key = e.GetRXClass().DxfName + "\t" + e.Layer;
                                cnt[key] = (cnt.TryGetValue(key, out int n) ? n : 0) + 1;
                            }
                            sb.AppendLine("FILE_ENTS\t" + cnt.Values.Sum() + "\tINSUNITS\t" + chk.Insunits);
                            foreach (var kv in cnt.Take(8)) sb.AppendLine("FILE\t" + kv.Key + "\t" + kv.Value);
                            tr.Commit();
                        }
                    }
                }
                catch (System.Exception ex) { sb.AppendLine("ERROR\t" + ex); }
                File.WriteAllText(Environment.GetEnvironmentVariable("CORSOLID_LOG"), sb.ToString(), new UTF8Encoding(false));
                return;
            }
            ObjectId before = Last(db);
            var rs = Extract(db, db, ids, o, s => sb.Append(s));
            Summary(rs, s => sb.Append(s));
            sb.AppendLine("\nSECONDS " + (int)(DateTime.Now - t0).TotalSeconds);
            if (o.TrimRegions) dumpRegions("AFTER");
            // 만들어진 객체: 종류/레이어/특성세트
            var made = After(db, before);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var layers = new SortedDictionary<string, int>();
                foreach (var id in made)
                {
                    var e = (Autodesk.AutoCAD.DatabaseServices.Entity)tr.GetObject(id, OpenMode.ForRead);
                    string key = e.GetRXClass().DxfName + "\t" + e.Layer;
                    layers[key] = (layers.TryGetValue(key, out int n) ? n : 0) + 1;
                }
                foreach (var kv in layers) sb.AppendLine("MADE\t" + kv.Key + "\t" + kv.Value);
                tr.Commit();
            }
            if (made.Count > 0) sb.AppendLine("FIRSTHANDLE\t" + made[0].Handle);
            File.WriteAllText(Environment.GetEnvironmentVariable("CORSOLID_LOG"), sb.ToString(), new UTF8Encoding(false));
        }

        // 모형 공간의 마지막 객체 / 그 뒤에 생긴 객체
        static ObjectId Last(Database db)
        {
            ObjectId last = ObjectId.Null;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms) last = id;
                tr.Commit();
            }
            return last;
        }

        static List<ObjectId> After(Database db, ObjectId last)
        {
            var list = new List<ObjectId>();
            bool seen = last.IsNull;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (seen) list.Add(id);
                    else if (id == last) seen = true;
                }
                tr.Commit();
            }
            return list;
        }
    }
}
