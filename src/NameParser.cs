using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AnimeRenamer
{
    public static class NameParser
    {
        private static readonly HashSet<string> VideoExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".mkv", ".mp4", ".avi", ".rmvb", ".rm", ".wmv", ".flv", ".mov", ".m4v",
            ".ts", ".m2ts", ".mts", ".webm", ".mpg", ".mpeg", ".vob", ".iso"
        };

        private static readonly HashSet<string> SubExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".ass", ".ssa", ".srt", ".sup", ".vtt", ".sub", ".idx", ".smi"
        };

        private static readonly HashSet<string> SidecarExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".nfo", ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".torrent"
        };

        private static readonly Regex ReBracket = new Regex(@"[\[\(【（]([^\]\)】）]{0,120})[\]\)】）]", RegexOptions.Compiled);

        private static readonly Regex ReTech = new Regex(
            @"(?ix)\b(?:
                \d{3,4}[pi] | 4k | 8k | uhd | hdr(?:10)? | dolby(?:\s*vision)? | dv |
                hevc | avc | x26[45] | h\.?26[45] | 10[-_. ]?bit | 8[-_. ]?bit | hi10p | ma10p | yuv420p\d* |
                web[-_. ]?dl | web[-_. ]?rip | bd[-_. ]?rip | blu[-_. ]?ray | bd | remux |
                dvd[-_. ]?rip | hdtv | aac | flac | ddp? | eac3 | ac3 | dts(?:[-_. ]?hd)? |
                opus | mp3 | truehd | atmos | chs | cht | sc | tc | gb | big5 | jpn | jp |
                eng | en | multi(?:[-_. ]?sub)? | 简体 | 繁体 | 简繁 | 内嵌 | 外挂 |
                合集 | 全集 | 完结 | 修正版 | 招募 | 字幕组 | 生肉 | 熟肉
            )\b", RegexOptions.Compiled);

        private static readonly Regex ReSeasonTag = new Regex(
            @"(?ix)\b(?:
                s\d{1,2} |
                season[\s._-]*\d{1,2} |
                \d{1,2}(?:st|nd|rd|th)[\s._-]*season |
                part[\s._-]*\d{1,2} |
                第\s*[0-9一二三四五六七八九十]+\s*[季期]
            )\b", RegexOptions.Compiled);

        private static readonly Regex ReAudioDecimal = new Regex(@"\b\d{1,2}\.\d(?:\.\d)?\b", RegexOptions.Compiled);

        private static readonly Regex ReSxxExx = new Regex(
            @"(?i)(?:^|[^a-z0-9])s(\d{1,2})[\s._-]*e(?:p)?[\s._-]*(\d{1,4})(?:v\d+)?",
            RegexOptions.Compiled);

        private static readonly Regex ReSxxEpOnly = new Regex(
            @"(?i)(?:^|[^a-z0-9])s(\d{1,2})[\s._-]+(\d{1,4})(?:v\d+)?(?:[\s._-]|$)",
            RegexOptions.Compiled);

        private static readonly Regex ReChineseEp = new Regex(@"第\s*(\d{1,4})\s*[话話集]", RegexOptions.Compiled);

        private static readonly Regex ReEp = new Regex(
            @"(?i)(?:^|[^a-z0-9])(?:ep?|episode)[\s._-]*(\d{1,4})(?:v\d+)?",
            RegexOptions.Compiled);

        private static readonly Regex ReBracketEp = new Regex(
            @"[\[\(【（]\s*(\d{1,4})(?:v\d+)?\s*(?:end|fin|完)?\s*[\]\)】）]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ReDashEp = new Regex(
            @"(?i)(?:^|[\s._-])(\d{1,4})(?:v\d+)?(?:end|fin|完)?(?:[\s._-]|$)",
            RegexOptions.Compiled);

        private static readonly Regex ReSpecial = new Regex(
            @"(?i)(?:^|[^a-z0-9])(sp|ova|oad|specials?|ex)[\s._-]*(\d{1,3})?(?:[\s._-]|$)",
            RegexOptions.Compiled);

        private static readonly Regex ReExtra = new Regex(
            @"(?i)(?:^|[^a-z0-9])(ncop|nced|creditless|clean[\s._-]?(?:op|ed)|menu|preview|trailer|sample|interview|making|cm\d*|pv\d*|特典|映像特典|片头|片尾|预告|花絮|访谈)(?=[^a-z]|$)",
            RegexOptions.Compiled);

        private static readonly Regex ReLang = new Regex(
            @"(?i)(?:^|[._\s\[\]\(\)\-–])(chs|cht|sc|tc|gb|big5|zh[-_]?cn|zh[-_]?tw|zh|jpn|jp|eng|en|简日|繁日|简体|繁体|简|繁)(?=[._\s\[\]\(\)\-–]|$)",
            RegexOptions.Compiled);

        private static readonly Regex ReResolution = new Regex(@"(?i)(?<!\d)(\d{3,4})[pi](?!\w)", RegexOptions.Compiled);

        public static bool TryKind(string ext, out MediaKind kind)
        {
            if (VideoExts.Contains(ext)) { kind = MediaKind.Video; return true; }
            if (SubExts.Contains(ext)) { kind = MediaKind.Subtitle; return true; }
            if (SidecarExts.Contains(ext)) { kind = MediaKind.Sidecar; return true; }
            kind = MediaKind.Sidecar;
            return false;
        }

        public static bool IsSubtitleExt(string ext) { return SubExts.Contains(ext); }
        public static bool IsVideoExt(string ext) { return VideoExts.Contains(ext); }

        public static ParsedName Parse(string pathOrName)
        {
            string name = Path.GetFileName(pathOrName);
            string stem = Path.GetFileNameWithoutExtension(name);
            var p = new ParsedName();
            p.Resolution = ExtractResolution(stem);
            p.Group = ExtractGroup(stem);
            p.SubLang = ExtractLang(stem);

            if (ReExtra.IsMatch(stem))
            {
                p.IsExtra = true;
                p.Episode = FindNumber(stem, true);
                return p;
            }

            Match m = ReSxxExx.Match(stem);
            if (m.Success)
            {
                int season = SafeInt(m.Groups[1].Value);
                int ep = SafeInt(m.Groups[2].Value);
                if (ValidEpisode(ep))
                {
                    p.Season = season;
                    p.Episode = ep;
                    p.SeasonExplicit = true;
                    p.IsSpecial = season == 0;
                    return p;
                }
            }

            m = ReSxxEpOnly.Match(stem);
            if (m.Success)
            {
                int season = SafeInt(m.Groups[1].Value);
                int ep = SafeInt(m.Groups[2].Value);
                if (ValidEpisode(ep))
                {
                    p.Season = season;
                    p.Episode = ep;
                    p.SeasonExplicit = true;
                    return p;
                }
            }

            Match sp = ReSpecial.Match(stem);
            if (sp.Success)
            {
                p.IsSpecial = true;
                p.Season = 0;
                if (sp.Groups[2].Success) p.Episode = SafeInt(sp.Groups[2].Value);
                else p.Episode = FindNumber(stem, true);
                return p;
            }

            m = ReChineseEp.Match(stem);
            if (m.Success)
            {
                int ep = SafeInt(m.Groups[1].Value);
                if (ValidEpisode(ep)) { p.Episode = ep; return p; }
            }

            m = ReEp.Match(stem);
            if (m.Success)
            {
                int ep = SafeInt(m.Groups[1].Value);
                if (ValidEpisode(ep)) { p.Episode = ep; return p; }
            }

            p.Episode = FindNumber(stem, false);
            return p;
        }

        private static int FindNumber(string stem, bool loose)
        {
            string probe = Probe(stem);
            MatchCollection ms = ReBracketEp.Matches(stem);
            for (int i = ms.Count - 1; i >= 0; i--)
            {
                int n = SafeInt(ms[i].Groups[1].Value);
                if (ValidEpisode(n)) return n;
            }

            ms = ReDashEp.Matches(probe);
            for (int i = ms.Count - 1; i >= 0; i--)
            {
                int n = SafeInt(ms[i].Groups[1].Value);
                if (ValidEpisode(n)) return n;
            }

            if (!loose)
            {
                ms = ReEp.Matches(stem);
                for (int i = ms.Count - 1; i >= 0; i--)
                {
                    int n = SafeInt(ms[i].Groups[1].Value);
                    if (ValidEpisode(n)) return n;
                }
            }
            return -1;
        }

        private static string Probe(string stem)
        {
            string s = ReBracket.Replace(stem, " ");
            s = ReTech.Replace(s, " ");
            s = ReAudioDecimal.Replace(s, " ");
            return s;
        }

        private static int SafeInt(string s)
        {
            int n;
            if (int.TryParse(s, out n)) return n;
            return -1;
        }

        public static bool ValidEpisode(int n)
        {
            if (n < 0 || n > 999) return false;
            if (n >= 1900 && n <= 2099) return false;
            switch (n)
            {
                case 360:
                case 480:
                case 540:
                case 576:
                case 720:
                case 900:
                case 1080:
                case 1440:
                case 2160:
                case 4320:
                    return false;
            }
            return true;
        }

        public static string ExtractResolution(string stem)
        {
            Match m = ReResolution.Match(stem);
            if (m.Success)
            {
                int n = SafeInt(m.Groups[1].Value);
                if (n == 2160) return "2160p";
                if (n == 1080 || n == 720 || n == 480 || n == 576) return n + "p";
            }
            if (Regex.IsMatch(stem, @"(?i)\b(4k|uhd)\b")) return "2160p";
            if (Regex.IsMatch(stem, @"(?i)\b8k\b")) return "4320p";
            return "";
        }

        public static string ExtractGroup(string stem)
        {
            Match m = Regex.Match(stem, @"^\s*[\[\(【（]([^\]\)】）]{1,48})[\]\)】）]");
            if (!m.Success) return "";
            string g = m.Groups[1].Value.Trim();
            if (g.Length == 0) return "";
            if (Regex.IsMatch(g, @"^\d+$")) return "";
            if (Regex.IsMatch(g, @"(?i)^(1080p|720p|2160p|4k|8k|chs|cht|gb|big5|jpn|eng|sc|tc)$")) return "";
            return g;
        }

        public static string ExtractLang(string stem)
        {
            MatchCollection ms = ReLang.Matches(stem);
            for (int i = ms.Count - 1; i >= 0; i--)
            {
                string v = NormalizeLang(ms[i].Groups[1].Value);
                if (v.Length > 0) return v;
            }
            return "";
        }

        private static string NormalizeLang(string token)
        {
            string t = token.ToLowerInvariant().Replace("_", "-");
            switch (t)
            {
                case "chs":
                case "sc":
                case "gb":
                case "zh-cn":
                case "zh":
                case "简":
                case "简体":
                case "简日":
                    return "chs";
                case "cht":
                case "tc":
                case "big5":
                case "zh-tw":
                case "繁":
                case "繁体":
                case "繁日":
                    return "cht";
                case "jp":
                case "jpn":
                    return "jpn";
                case "en":
                case "eng":
                    return "eng";
            }
            return "";
        }

        public static string CleanTitle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string name = raw.Trim();

            var brackets = new List<string>();
            foreach (Match m in ReBracket.Matches(name))
            {
                string v = m.Groups[1].Value.Trim();
                if (v.Length > 0) brackets.Add(v);
            }

            string s = ReBracket.Replace(name, " ");
            s = ReTech.Replace(s, " ");
            s = ReSeasonTag.Replace(s, " ");
            s = ReAudioDecimal.Replace(s, " ");
            s = s.Replace('_', ' ').Replace('.', ' ');

            // "中文名 / Japanese Title" -> keep the Chinese side; "Fate / Stay Night" stays intact.
            int slash = s.IndexOf(" / ", StringComparison.Ordinal);
            if (slash > 1)
            {
                string left = s.Substring(0, slash).Trim();
                if (Regex.IsMatch(left, @"[\u4e00-\u9fff]")) s = left;
            }
            int bar = s.IndexOf(" | ", StringComparison.Ordinal);
            if (bar > 1)
            {
                string left = s.Substring(0, bar).Trim();
                if (Regex.IsMatch(left, @"[\u4e00-\u9fff]")) s = left;
            }

            s = Regex.Replace(s, @"\s*[-–—~]+\s*$", " ");
            s = Regex.Replace(s, @"^\s*[-–—~]+\s*", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim(' ', '-', '_', '~', '|', '·', '/');

            if (!HasLetter(s) && brackets.Count > 0)
            {
                string best = brackets.OrderByDescending(x => x.Length).First();
                s = CleanTitle(best);
            }
            if (!HasLetter(s))
            {
                s = Regex.Replace(name, @"[_\.]+", " ").Trim();
            }

            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }

        private static bool HasLetter(string s)
        {
            return !string.IsNullOrEmpty(s) && Regex.IsMatch(s, @"[\p{L}]");
        }

        public static int ParseSeasonName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return -1;
            string n = name.Trim();
            Match m = Regex.Match(n, @"(?i)^season[\s._-]*(\d{1,2})$");
            if (m.Success) return SafeInt(m.Groups[1].Value);
            m = Regex.Match(n, @"(?i)^s(\d{1,2})$");
            if (m.Success) return SafeInt(m.Groups[1].Value);
            m = Regex.Match(n, @"^第\s*(\d{1,2})\s*[季期]$");
            if (m.Success) return SafeInt(m.Groups[1].Value);
            m = Regex.Match(n, @"(?i)^(\d{1,2})(?:st|nd|rd|th)[\s._-]*season$");
            if (m.Success) return SafeInt(m.Groups[1].Value);
            return -1;
        }

        public static bool IsJunkDirName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            string n = name.Trim();
            if (ParseSeasonName(n) >= 0) return true;
            string[] junk = {
                "sub", "subs", "subtitle", "subtitles", "font", "fonts", "scan", "scans",
                "cd", "cds", "extra", "extras", "bonus", "sp", "sps", "specials", "special",
                "menu", "bdmv", "stream", "video", "videos", "download", "downloads",
                "torrent", "torrents", "incoming", "temp", "tmp", "anime", "bangumi",
                "番剧", "动漫", "动画", "未分类", "其他", "其他番剧", "字幕", "字体", "特典"
            };
            return junk.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase));
        }

        public static string DetectTitle(string root, string filePath)
        {
            string dir = Path.GetDirectoryName(filePath);
            string rootFull = null;
            try { rootFull = Path.GetFullPath(root).TrimEnd('\\'); } catch { }

            for (int i = 0; dir != null && i < 10; i++)
            {
                string name = Path.GetFileName(dir);
                if (!string.IsNullOrEmpty(name) && !IsJunkDirName(name))
                {
                    string t = CleanTitle(name);
                    if (t.Length >= 2) return t;
                }
                if (rootFull != null && string.Equals(dir.TrimEnd('\\'), rootFull, StringComparison.OrdinalIgnoreCase)) break;
                dir = Path.GetDirectoryName(dir);
            }
            return "";
        }

        public static int DetectSeason(string filePath, string root)
        {
            string dir = Path.GetDirectoryName(filePath);
            string rootFull = null;
            try { rootFull = Path.GetFullPath(root).TrimEnd('\\'); } catch { }

            for (int i = 0; dir != null && i < 10; i++)
            {
                string name = Path.GetFileName(dir);
                int s = ParseSeasonName(name);
                if (s >= 0) return s;
                if (rootFull != null && string.Equals(dir.TrimEnd('\\'), rootFull, StringComparison.OrdinalIgnoreCase)) break;
                dir = Path.GetDirectoryName(dir);
            }
            return 1;
        }

        public static string ApplyTemplate(string template, Dictionary<string, object> tokens)
        {
            if (string.IsNullOrWhiteSpace(template)) template = "{title} - S{season:02d}E{ep:02d}";
            return Regex.Replace(template, @"\{([a-zA-Z_]+)(?::([^}]+))?\}", delegate(Match m)
            {
                string key = m.Groups[1].Value.ToLowerInvariant();
                object val;
                if (!tokens.TryGetValue(key, out val) || val == null) return "";
                string spec = m.Groups[2].Success ? m.Groups[2].Value : "";
                if (val is int) return FormatNumber((int)val, spec);
                return Convert.ToString(val);
            });
        }

        private static string FormatNumber(int n, string spec)
        {
            if (string.IsNullOrEmpty(spec)) return n.ToString();
            string digits = new string(spec.Where(char.IsDigit).ToArray());
            int width = 0;
            int.TryParse(digits, out width);
            if (width <= 0) width = digits.Length;
            if (width <= 0) return n.ToString();
            return n.ToString().PadLeft(width, '0');
        }

        public static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "未命名";
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (c < 32 || c == '<' || c == '>' || c == ':' || c == '"' ||
                    c == '/' || c == '\\' || c == '|' || c == '?' || c == '*')
                    sb.Append(' ');
                else
                    sb.Append(c);
            }

            string s = Regex.Replace(sb.ToString(), @"\s+", " ").Trim(' ', '.', '-');
            s = Regex.Replace(s, @"\.{2,}", ".");
            if (s.Length == 0) s = "未命名";
            if (s.Length > 170) s = s.Substring(0, 170).TrimEnd(' ', '.');

            string upper = s.ToUpperInvariant();
            string[] reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
            if (reserved.Contains(upper)) s = "_" + s;
            return s;
        }

        public static int NaturalCompare(string a, string b)
        {
            if (a == null) return b == null ? 0 : -1;
            if (b == null) return 1;
            int ia = 0, ib = 0;
            while (ia < a.Length && ib < b.Length)
            {
                char ca = a[ia], cb = b[ib];
                if (char.IsDigit(ca) && char.IsDigit(cb))
                {
                    int sa = ia, sb = ib;
                    while (ia < a.Length && char.IsDigit(a[ia])) ia++;
                    while (ib < b.Length && char.IsDigit(b[ib])) ib++;
                    long na = 0, nb = 0;
                    long.TryParse(a.Substring(sa, ia - sa), out na);
                    long.TryParse(b.Substring(sb, ib - sb), out nb);
                    if (na != nb) return na < nb ? -1 : 1;
                }
                else
                {
                    char la = char.ToLowerInvariant(ca), lb = char.ToLowerInvariant(cb);
                    if (la != lb) return la < lb ? -1 : 1;
                    ia++;
                    ib++;
                }
            }
            return (a.Length - ia).CompareTo(b.Length - ib);
        }

        public static double Similarity(string a, string b)
        {
            string x = Norm(a);
            string y = Norm(b);
            if (x.Length == 0 || y.Length == 0) return 0;
            if (x == y) return 1.0;
            if (x.Contains(y) || y.Contains(x)) return 0.86;

            if (x.Length < 2 || y.Length < 2) return 0;
            var pairs = new Dictionary<string, int>();
            for (int i = 0; i < x.Length - 1; i++)
            {
                string k = x.Substring(i, 2);
                int c;
                pairs.TryGetValue(k, out c);
                pairs[k] = c + 1;
            }
            int hit = 0;
            for (int i = 0; i < y.Length - 1; i++)
            {
                string k = y.Substring(i, 2);
                int c;
                if (pairs.TryGetValue(k, out c) && c > 0)
                {
                    hit++;
                    pairs[k] = c - 1;
                }
            }
            return 2.0 * hit / (x.Length + y.Length - 2);
        }

        private static string Norm(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder();
            foreach (char c in s.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
