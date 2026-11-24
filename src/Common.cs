// AutoCAD에 의존하지 않는 공통 코드 (옵션, 이름 검색, 정렬). 창 테스트용 프로그램에서도 같이 쓴다.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CorridorSolids
{
    // 출력 객체 유형 (Civil 3D '코리더에서 솔리드 추출' 창의 '출력 객체 유형'과 같은 세 가지)
    public enum OutputType { SectionSolid = 0, SweepSolid = 1, Body = 2 }

    // 출력 대상
    public enum OutputTarget { Current = 0, Existing = 1, New = 2 }

    public class Options
    {
        public bool Rebuild = true;          // 최신이 아닌 코리더를 먼저 다시 만들기
        public bool ForceRebuild = false;    // (테스트용) 최신이어도 다시 만들기
        public bool Shapes = true;           // 쉐이프 추출
        public bool Links = false;           // 링크 추출
        public OutputType Type = OutputType.SectionSolid;
        public OutputTarget Target = OutputTarget.Current;
        public string TargetPath = "";       // 기존/새 도면 경로
        public string[] Excluded = new string[0];
        public bool TrimRegions = false;     // 추출 전에 모든 영역의 시작 측점 +TrimAmount, 끝 측점 -TrimAmount (코리더가 실제로 바뀜)
        public double TrimAmount = 0.01;     // m

        public bool Sweep { get => Type == OutputType.SweepSolid; }

        public Options Clone()
        {
            var o = (Options)MemberwiseClone();
            o.Excluded = (string[])Excluded.Clone();
            return o;
        }

        public static readonly string[] TypeNames =
        {
            "AutoCAD 3D 솔리드 (코리더 단면 기반)",
            "AutoCAD 3D 솔리드 (스윕 기반)",
            "AutoCAD 바디",
        };
    }

    public static class NameMatch
    {
        // 공백 무시, 한글 정규화
        public static string Norm(string s) => Regex.Replace((s ?? "").Normalize(NormalizationForm.FormC), "\\s+", "");

        // '*' = 아무 글자 여러 개, '?' = 아무 글자 한 개. 나머지 글자(#, [ 등)는 그대로 비교한다.
        // 와일드카드가 없으면 '이름에 그 글자가 들어 있으면' 맞는 것으로 본다 (예: 유지보수도로 → 유지보수도로#1 ...).
        public static bool Like(string name, string pattern)
        {
            name = Norm(name);
            pattern = Norm(pattern);
            if (pattern == "") return false;
            if (pattern.IndexOf('*') < 0 && pattern.IndexOf('?') < 0)
                return name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
            var rx = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(name, rx, RegexOptions.IgnoreCase);
        }

        // 쉼표로 나눈 패턴마다: 이름이 정확히 같은 것이 있으면 그것만, 없으면 Like 규칙으로 찾는다. 결과는 names의 인덱스.
        public static List<int> MatchIndexes(IList<string> names, string input)
        {
            var hit = new HashSet<int>();
            foreach (var pt in (input ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var exact = Enumerable.Range(0, names.Count)
                    .Where(i => string.Equals(Norm(names[i]), Norm(pt), StringComparison.OrdinalIgnoreCase)).ToList();
                var use = exact.Count > 0 ? exact : Enumerable.Range(0, names.Count).Where(i => Like(names[i], pt)).ToList();
                foreach (var i in use) hit.Add(i);
            }
            return hit.OrderBy(i => i).ToList();
        }

        // 검색칸용: 쉼표로 나눈 조건 중 하나라도 Like에 맞으면 보인다. 비어 있으면 모두 보인다.
        public static bool Visible(string name, string filter)
        {
            var parts = (filter ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Where(p => Norm(p) != "").ToList();
            return parts.Count == 0 || parts.Any(p => Like(name, p));
        }
    }

    // "유지보수도로#2" < "유지보수도로#10" 처럼 숫자를 숫자로 비교
    public class NaturalComparer : IComparer<string>
    {
        public int Compare(string a, string b)
        {
            var ra = Regex.Split(a ?? "", "(\\d+)");
            var rb = Regex.Split(b ?? "", "(\\d+)");
            for (int i = 0; i < Math.Min(ra.Length, rb.Length); i++)
            {
                int c;
                if (long.TryParse(ra[i], out long na) && long.TryParse(rb[i], out long nb)) c = na.CompareTo(nb);
                else c = string.Compare(ra[i], rb[i], StringComparison.CurrentCulture);
                if (c != 0) return c;
            }
            return ra.Length.CompareTo(rb.Length);
        }
    }
}
