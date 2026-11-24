// CORSOLID : 코리더 목록 창을 띄워 골라서 추출한다.
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(CorridorSolids.DialogCmds))]

namespace CorridorSolids
{
    public class DialogCmds
    {
        // 같은 AutoCAD 세션 안에서는 마지막 설정과 선택(코리더 이름)을 기억한다.
        static Options lastOpt = new Options();
        static HashSet<string> lastNames = new HashSet<string>();
        static string lastSearch = "";

        [CommandMethod("CORSOLID", CommandFlags.Modal)]
        public static void CorSolidDialog()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;
            var all = Cmds.GetCorridors(db);
            if (all.Count == 0) { ed.WriteMessage("\n이 도면에는 코리더가 없습니다."); return; }

            var names = all.Select(c => c.Name).ToList();
            var outdated = all.Select(c => c.OutOfDate).ToList();
            var pre = new HashSet<int>(Enumerable.Range(0, all.Count).Where(i => lastNames.Contains(names[i])));
            var opt = lastOpt;
            string search = lastSearch;

            while (true)
            {
                List<int> picked;
                using (var f = new CorridorForm(names, outdated, pre, opt, search, db.Filename))
                {
                    var r = AcApp.ShowModalDialog(f);
                    opt = f.Opt;
                    search = f.SearchText;
                    picked = f.SelectedIndexes;
                    lastOpt = opt;
                    lastSearch = search;
                    lastNames = new HashSet<string>(picked.Select(i => names[i]));

                    if (r == DialogResult.Retry)
                    {
                        // 도면에서 코리더를 골라 체크에 더한 뒤 창을 다시 연다
                        pre = new HashSet<int>(picked);
                        var so = new PromptSelectionOptions { MessageForAdding = "\n추가할 코리더 선택 (끝나면 Enter): " };
                        var sr = ed.GetSelection(so, new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "AECC_CORRIDOR") }));
                        if (sr.Status == PromptStatus.OK)
                        {
                            var ids = new HashSet<ObjectId>(sr.Value.GetObjectIds());
                            int before = pre.Count;
                            for (int i = 0; i < all.Count; i++) if (ids.Contains(all[i].Id)) pre.Add(i);
                            ed.WriteMessage("\n코리더 " + (pre.Count - before) + "개를 추가했습니다.");
                        }
                        continue;
                    }
                    if (r != DialogResult.OK || picked.Count == 0) { ed.WriteMessage("\n취소했습니다."); return; }
                }

                ed.WriteMessage("\n대상 코리더 " + picked.Count + "개: " + string.Join(", ", picked.Take(10).Select(i => names[i]))
                    + (picked.Count > 10 ? " 외 " + (picked.Count - 10) + "개" : ""));
                Cmds.RunAndSelect(ed, db, picked.Select(i => all[i].Id).ToList(), opt);
                return;
            }
        }
    }
}
