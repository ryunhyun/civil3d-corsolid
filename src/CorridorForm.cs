// 코리더 선택 창 (AutoCAD에 의존하지 않음 → 창 미리보기 프로그램에서도 그대로 쓴다)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CorridorSolids
{
    public class CorridorForm : Form
    {
        readonly IList<string> names;
        readonly IList<bool> outdated;
        readonly bool[] chk;
        bool filling;

        readonly TextBox txtSearch = new TextBox();
        readonly ListView list = new ListView();
        readonly CheckBox cbRebuild = new CheckBox();
        readonly CheckBox cbShapes = new CheckBox();
        readonly CheckBox cbLinks = new CheckBox();
        readonly ComboBox cmbType = new ComboBox();
        readonly CheckBox cbTrim = new CheckBox();
        readonly NumericUpDown nudTrim = new NumericUpDown();
        readonly TextBox txtExclude = new TextBox();
        readonly RadioButton rbCurrent = new RadioButton();
        readonly RadioButton rbExisting = new RadioButton();
        readonly RadioButton rbNew = new RadioButton();
        readonly TextBox txtPath = new TextBox();
        readonly Button btnBrowse = new Button();
        readonly string currentDwg;
        readonly Label lblStatus = new Label();
        readonly Button btnOk = new Button();

        /// <summary>창을 닫은 뒤: 체크된 코리더(names의 인덱스)</summary>
        public List<int> SelectedIndexes { get; private set; } = new List<int>();
        /// <summary>창을 닫은 뒤: 추출 설정</summary>
        public Options Opt { get; private set; }
        /// <summary>'도면에서 선택'을 눌렀으면 DialogResult.Retry로 닫힌다.</summary>
        public string SearchText => txtSearch.Text;

        /// <param name="currentDwg">코리더가 있는 지금 도면의 경로 (새 도면 기본 이름을 만들 때 씀, 비어 있어도 됨)</param>
        public CorridorForm(IList<string> names, IList<bool> outdated, ICollection<int> preChecked, Options opt, string search, string currentDwg = "")
        {
            this.names = names;
            this.outdated = outdated;
            this.currentDwg = currentDwg ?? "";
            chk = new bool[names.Count];
            foreach (var i in preChecked) if (i >= 0 && i < chk.Length) chk[i] = true;
            Opt = opt.Clone();

            Text = "코리더 솔리드 추출";
            Font = new Font("맑은 고딕", 9F);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(540, 810);
            MinimumSize = new Size(480, 690);
            Padding = new Padding(10);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            // 0) 검색
            var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            searchRow.Controls.Add(new Label { Text = "검색", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 6, 0) }, 0, 0);
            txtSearch.Dock = DockStyle.Fill;
            txtSearch.PlaceholderText = "이름 일부 (예: 유지보수도로)   * = 여러 글자, ? = 한 글자, 쉼표로 여러 개";
            txtSearch.Text = search ?? "";
            txtSearch.TextChanged += (s, e) => Fill();
            searchRow.Controls.Add(txtSearch, 1, 0);
            var hint = new Label
            {
                Text = "이름 일부만 써도 됨 · * 여러 글자 · ? 한 글자 · 쉼표로 여러 개",
                AutoSize = false, Dock = DockStyle.Fill, Height = 20, AutoEllipsis = true,
                ForeColor = SystemColors.GrayText, Margin = new Padding(0, 3, 0, 0)
            };
            searchRow.Controls.Add(hint, 1, 1);
            root.Controls.Add(searchRow, 0, 0);

            // 1) 목록
            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.CheckBoxes = true;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.Columns.Add("코리더", 360);
            list.Columns.Add("상태", 90);
            list.ItemChecked += (s, e) =>
            {
                if (filling) return;
                chk[(int)e.Item.Tag] = e.Item.Checked;
                UpdateStatus();
            };
            list.Resize += (s, e) => FitColumns();
            root.Controls.Add(list, 0, 1);

            // 2) 선택 버튼
            var sel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
            sel.Controls.Add(MakeButton("전체 선택", (s, e) => SetVisible(true)));
            sel.Controls.Add(MakeButton("선택 해제", (s, e) => SetVisible(false)));
            sel.Controls.Add(MakeButton("도면에서 선택...", (s, e) => { Collect(); DialogResult = DialogResult.Retry; }));
            root.Controls.Add(sel, 0, 2);

            // 3) 추출 설정
            var grp = new GroupBox { Text = "추출 설정", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8, 4, 8, 6) };
            var opts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
            opts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            opts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int nOut = outdated.Count(b => b);
            cbRebuild.Text = "최신이 아닌 코리더는 다시 만든 뒤 추출" + (nOut > 0 ? " (" + nOut + "개)" : " (해당 없음)");
            cbRebuild.Checked = Opt.Rebuild;
            cbRebuild.AutoSize = true;
            cbRebuild.Margin = new Padding(0, 2, 0, 4);
            opts.Controls.Add(cbRebuild, 0, 0); opts.SetColumnSpan(cbRebuild, 2);

            opts.Controls.Add(RowLabel("출력 객체 유형"), 0, 1);
            cmbType.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbType.Items.AddRange(Options.TypeNames);
            cmbType.SelectedIndex = (int)Opt.Type;
            cmbType.Dock = DockStyle.Fill;
            cmbType.Margin = new Padding(0, 2, 0, 2);
            opts.Controls.Add(cmbType, 1, 1);

            opts.Controls.Add(RowLabel("추출할 것"), 0, 2);
            var what = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
            cbShapes.Text = "쉐이프";
            cbShapes.Checked = Opt.Shapes;
            cbLinks.Text = "링크";
            cbLinks.Checked = Opt.Links;
            foreach (var cb in new[] { cbShapes, cbLinks }) { cb.AutoSize = true; cb.Margin = new Padding(0, 4, 16, 2); what.Controls.Add(cb); }
            opts.Controls.Add(what, 1, 2);

            opts.Controls.Add(RowLabel("제외할 코드"), 0, 3);
            txtExclude.Dock = DockStyle.Fill;
            txtExclude.Margin = new Padding(0, 4, 0, 0);
            txtExclude.PlaceholderText = "쉼표로 구분 (비워 두면 전부 추출)";
            txtExclude.Text = string.Join(", ", Opt.Excluded);
            opts.Controls.Add(txtExclude, 1, 3);

            // 영역 측점 조정
            var trimRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 0), WrapContents = false };
            cbTrim.Text = "추출 전 모든 영역의 시작 측점 +, 끝 측점 −";
            cbTrim.AutoSize = true;
            cbTrim.Margin = new Padding(0, 4, 4, 0);
            cbTrim.Checked = Opt.TrimRegions;
            nudTrim.DecimalPlaces = 3;
            nudTrim.Increment = 0.005M;
            nudTrim.Minimum = 0.001M;
            nudTrim.Maximum = 1M;
            nudTrim.Value = (decimal)Math.Min(1.0, Math.Max(0.001, Opt.TrimAmount));
            nudTrim.Width = 70;
            nudTrim.TextAlign = HorizontalAlignment.Right;
            nudTrim.Margin = new Padding(0, 2, 4, 0);
            trimRow.Controls.Add(cbTrim);
            trimRow.Controls.Add(nudTrim);
            trimRow.Controls.Add(new Label { Text = "m", AutoSize = true, Margin = new Padding(0, 5, 0, 0) });
            opts.Controls.Add(trimRow, 0, 4); opts.SetColumnSpan(trimRow, 2);
            var trimHint = new Label
            {
                Text = "코리더의 영역 측점이 실제로 바뀝니다(U로 되돌림). 실행할 때마다 다시 줄어듭니다.",
                AutoSize = false, Dock = DockStyle.Fill, Height = 18, AutoEllipsis = true,
                ForeColor = SystemColors.GrayText, Margin = new Padding(18, 0, 0, 0)
            };
            opts.Controls.Add(trimHint, 0, 5); opts.SetColumnSpan(trimHint, 2);
            cbTrim.CheckedChanged += (s, e) => { nudTrim.Enabled = cbTrim.Checked; };
            nudTrim.Enabled = cbTrim.Checked;

            grp.Controls.Add(opts);
            root.Controls.Add(grp, 0, 3);

            // 4) 출력 대상
            var grpOut = new GroupBox { Text = "출력 대상", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8, 4, 8, 6), Margin = new Padding(3, 6, 3, 3) };
            var outs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
            outs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            outs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rbCurrent.Text = "현재 도면에 삽입";
            rbExisting.Text = "기존 도면에 추가";
            rbNew.Text = "새 도면에 추가";
            int r = 0;
            foreach (var rb in new[] { rbCurrent, rbExisting, rbNew })
            {
                rb.AutoSize = true;
                rb.Margin = new Padding(0, 2, 0, 2);
                rb.CheckedChanged += (s, e) => { if (((RadioButton)s).Checked) OnTargetChanged(); };
                outs.Controls.Add(rb, 0, r); outs.SetColumnSpan(rb, 2); r++;
            }
            txtPath.Dock = DockStyle.Fill;
            txtPath.Margin = new Padding(18, 4, 4, 0);
            txtPath.Text = Opt.TargetPath ?? "";
            outs.Controls.Add(txtPath, 0, 3);
            btnBrowse.Text = "...";
            btnBrowse.Size = new Size(36, 25);
            btnBrowse.Margin = new Padding(0, 3, 0, 0);
            btnBrowse.Click += (s, e) => Browse();
            outs.Controls.Add(btnBrowse, 1, 3);
            grpOut.Controls.Add(outs);
            root.Controls.Add(grpOut, 0, 4);
            (Opt.Target == OutputTarget.Existing ? rbExisting : Opt.Target == OutputTarget.New ? rbNew : rbCurrent).Checked = true;
            OnTargetChanged();

            // 5) 상태 + 추출/취소
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            lblStatus.AutoSize = false;
            lblStatus.Dock = DockStyle.Fill;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.AutoEllipsis = true;
            bottom.Controls.Add(lblStatus, 0, 0);
            btnOk.Text = "추출";
            btnOk.Size = new Size(90, 30);
            btnOk.Click += (s, e) =>
            {
                Collect();
                if (!Opt.Shapes && !Opt.Links)
                {
                    Warn("'쉐이프'와 '링크' 중 하나는 켜야 합니다.");
                    return;
                }
                if (!CheckTargetPath()) return;
                if (Opt.TrimRegions)
                {
                    string a = Opt.TrimAmount.ToString("0.###");
                    if (MessageBox.Show(this,
                        "선택한 코리더 " + SelectedIndexes.Count + "개의 모든 영역에서\n"
                        + "시작 측점을 +" + a + "m, 끝 측점을 −" + a + "m 바꾼 뒤 다시 만들고 추출합니다.\n\n"
                        + "코리더가 실제로 바뀌며, 다시 실행하면 또 " + a + "m씩 줄어듭니다.\n계속할까요?",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                }
                DialogResult = DialogResult.OK;
            };
            var btnCancel = new Button { Text = "취소", Size = new Size(90, 30), DialogResult = DialogResult.Cancel };
            bottom.Controls.Add(btnOk, 1, 0);
            bottom.Controls.Add(btnCancel, 2, 0);
            root.Controls.Add(bottom, 0, 5);
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            Fill();
            Shown += (s, e) => { FitColumns(); txtSearch.Focus(); txtSearch.SelectionStart = txtSearch.Text.Length; };
        }

        static Label RowLabel(string text) =>
            new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 10, 0) };

        void Warn(string msg) => MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);

        OutputTarget SelectedTarget => rbExisting.Checked ? OutputTarget.Existing : rbNew.Checked ? OutputTarget.New : OutputTarget.Current;

        void OnTargetChanged()
        {
            var t = SelectedTarget;
            txtPath.Enabled = btnBrowse.Enabled = t != OutputTarget.Current;
            txtPath.PlaceholderText = t == OutputTarget.Existing ? "솔리드를 추가할 도면 (... 로 찾기)"
                                    : t == OutputTarget.New ? "새로 만들 도면 경로 (... 로 지정)" : "";
            // 새 도면인데 경로가 비어 있으면 '지금도면이름_솔리드.dwg'를 제안
            if (t == OutputTarget.New && txtPath.Text.Trim() == "" && currentDwg != "")
            {
                try
                {
                    txtPath.Text = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(currentDwg),
                        System.IO.Path.GetFileNameWithoutExtension(currentDwg) + "_솔리드.dwg");
                }
                catch { }
            }
        }

        void Browse()
        {
            string cur = txtPath.Text.Trim();
            string dir = "";
            try { dir = cur != "" ? System.IO.Path.GetDirectoryName(cur) : System.IO.Path.GetDirectoryName(currentDwg); } catch { }
            FileDialog dlg = SelectedTarget == OutputTarget.Existing
                ? new OpenFileDialog { Title = "솔리드를 추가할 기존 도면", CheckFileExists = true }
                : new SaveFileDialog { Title = "새 도면 저장 위치", OverwritePrompt = true };
            using (dlg)
            {
                dlg.Filter = "도면 (*.dwg)|*.dwg";
                if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir)) dlg.InitialDirectory = dir;
                if (cur != "") dlg.FileName = System.IO.Path.GetFileName(cur);
                if (dlg.ShowDialog(this) == DialogResult.OK) txtPath.Text = dlg.FileName;
            }
        }

        bool CheckTargetPath()
        {
            if (Opt.Target == OutputTarget.Current) return true;
            string p = Opt.TargetPath;
            if (p == "") { Warn("출력할 도면 경로를 지정하세요."); return false; }
            if (!p.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)) { p += ".dwg"; Opt.TargetPath = p; txtPath.Text = p; }
            try { p = System.IO.Path.GetFullPath(p); } catch { Warn("경로가 올바르지 않습니다: " + p); return false; }
            if (currentDwg != "" && string.Equals(p, System.IO.Path.GetFullPath(currentDwg), StringComparison.OrdinalIgnoreCase))
            { Warn("지금 코리더 도면과 같은 파일입니다. '현재 도면에 삽입'을 고르세요."); return false; }
            if (Opt.Target == OutputTarget.Existing)
            {
                if (!System.IO.File.Exists(p)) { Warn("기존 도면을 찾을 수 없습니다:\n" + p); return false; }
                if (System.IO.File.Exists(System.IO.Path.ChangeExtension(p, ".dwl")))
                    return MessageBox.Show(this, "이 도면이 다른 곳(다른 사람/다른 AutoCAD)에서 열려 있을 수 있습니다.\n열려 있으면 추출이 실패합니다. 계속할까요?\n\n" + p,
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            }
            else if (System.IO.File.Exists(p))
            {
                return MessageBox.Show(this, "이미 있는 파일입니다. 덮어쓸까요?\n\n" + p, Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            }
            return true;
        }

        Button MakeButton(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(90, 28), Margin = new Padding(0, 0, 6, 0) };
            b.Click += click;
            return b;
        }

        // 검색어에 맞는 코리더만 목록에 보여 준다. 체크 상태는 숨겨져도 유지된다.
        void Fill()
        {
            filling = true;
            list.BeginUpdate();
            list.Items.Clear();
            for (int i = 0; i < names.Count; i++)
            {
                if (!NameMatch.Visible(names[i], txtSearch.Text)) continue;
                var it = new ListViewItem(names[i]) { Tag = i, Checked = chk[i], UseItemStyleForSubItems = false };
                var st = it.SubItems.Add(outdated[i] ? "최신 아님" : "최신");
                if (outdated[i]) st.ForeColor = Color.OrangeRed;
                else st.ForeColor = SystemColors.GrayText;
                list.Items.Add(it);
            }
            list.EndUpdate();
            filling = false;
            UpdateStatus();
        }

        void SetVisible(bool on)
        {
            filling = true;
            list.BeginUpdate();
            foreach (ListViewItem it in list.Items) { it.Checked = on; chk[(int)it.Tag] = on; }
            list.EndUpdate();
            filling = false;
            UpdateStatus();
        }

        void UpdateStatus()
        {
            int n = chk.Count(b => b);
            int nOut = Enumerable.Range(0, chk.Length).Count(i => chk[i] && outdated[i]);
            int shown = list.Items.Count;
            lblStatus.Text = "선택 " + n + " / " + names.Count + "개"
                + (nOut > 0 ? "  (최신 아님 " + nOut + "개 포함)" : "")
                + (shown < names.Count ? "\n검색 결과 " + shown + "개 표시 중" : "");
            btnOk.Enabled = n > 0;
        }

        void FitColumns()
        {
            int w = list.ClientSize.Width - list.Columns[1].Width - 4;
            if (w > 120) list.Columns[0].Width = w;
        }

        void Collect()
        {
            SelectedIndexes = Enumerable.Range(0, chk.Length).Where(i => chk[i]).ToList();
            Opt.Rebuild = cbRebuild.Checked;
            Opt.Shapes = cbShapes.Checked;
            Opt.Links = cbLinks.Checked;
            Opt.Type = (OutputType)Math.Max(0, cmbType.SelectedIndex);
            Opt.TrimRegions = cbTrim.Checked;
            Opt.TrimAmount = (double)nudTrim.Value;
            Opt.Target = SelectedTarget;
            Opt.TargetPath = txtPath.Text.Trim().Trim('"');
            Opt.Excluded = txtExclude.Text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s != "").ToArray();
        }
    }
}
