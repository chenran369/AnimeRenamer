using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace AnimeRenamer
{
    public enum MediaKind { Video, Subtitle, Sidecar }

    public class ParsedName
    {
        public int Season = 1;
        public int Episode = -1;
        public bool SeasonExplicit;
        public bool IsSpecial;
        public bool IsExtra;
        public string Resolution = "";
        public string Group = "";
        public string SubLang = "";

        public bool HasEpisode { get { return Episode >= 0; } }
    }

    public class FileItem
    {
        public string Path;
        public string Name;
        public long Size;
        public MediaKind Kind;
        public ParsedName Parsed;
    }

    public class SubjectInfo
    {
        public string Source = "";
        public string Id = "";
        public string Name = "";
        public string NameCn = "";
        public int Year;
        public int Eps;
        public double Score;

        public string PreferredTitle(bool chinese)
        {
            if (chinese && !string.IsNullOrEmpty(NameCn)) return NameCn;
            if (!string.IsNullOrEmpty(Name)) return Name;
            return NameCn;
        }

        public string Display
        {
            get
            {
                string t = PreferredTitle(true);
                string extra = "";
                if (!string.IsNullOrEmpty(NameCn) && !string.IsNullOrEmpty(Name) &&
                    !string.Equals(NameCn, Name, StringComparison.OrdinalIgnoreCase))
                    extra = " / " + Name;
                string meta = "";
                if (Year > 0) meta += "  " + Year;
                if (Eps > 0) meta += "  " + Eps + "话";
                if (Score > 0) meta += "  ★" + Score.ToString("0.0");
                return t + extra + meta;
            }
        }

        public string ShortDisplay
        {
            get
            {
                string t = PreferredTitle(true);
                if (Year > 0) t += " (" + Year + ")";
                return t;
            }
        }
    }

    public class PlanItem : INotifyPropertyChanged
    {
        public string Root { get; set; }
        public string Source { get; set; }
        public string Target { get; set; }
        public string TargetBase { get; set; }
        public MediaKind Kind { get; set; }
        public int Season { get; set; }
        public int Episode { get; set; }
        public int AbsEpisode { get; set; }
        public bool IsSpecial { get; set; }
        public string Language { get; set; }
        public string Status { get; set; }
        public string Note { get; set; }

        private bool _included;
        public bool Included
        {
            get { return _included; }
            set
            {
                if (_included == value) return;
                _included = value;
                Raise("Included");
            }
        }

        public string OldName { get { return Path.GetFileName(Source); } }
        public string NewName { get { return Path.GetFileName(Target); } }

        public string KindText
        {
            get
            {
                if (Kind == MediaKind.Video) return "视频";
                if (Kind == MediaKind.Subtitle) return "字幕";
                return "附属";
            }
        }

        public string EpisodeLabel
        {
            get
            {
                if (Episode < 0) return "";
                if (IsSpecial) return "SP" + Episode.ToString("00");
                return "S" + Season.ToString("00") + "E" + Episode.ToString("00");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string name)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }

    public class FolderJob
    {
        public string Root;
        public bool Recursive = true;
        public List<FileItem> Files = new List<FileItem>();
        public string DetectedTitle = "";
        public string Keyword = "";
        public string Title = "";
        public int Year;
        public int Season = 1;
        public int Offset;
        public string Template = "";
        public bool TitleCustomized;
        public List<SubjectInfo> Candidates = new List<SubjectInfo>();
        public SubjectInfo Subject;
        public Dictionary<int, string> EpisodeTitles = new Dictionary<int, string>();
        public List<PlanItem> Plan = new List<PlanItem>();
        public string MatchState = "待匹配";
        public bool MetadataLoading;
        public int SearchToken;

        public string DisplayName
        {
            get
            {
                string n = Path.GetFileName(Root.TrimEnd('\\'));
                if (string.IsNullOrEmpty(n)) n = Root;
                return n;
            }
        }

        public int VideoCount
        {
            get { return Files.Count(f => f.Kind == MediaKind.Video); }
        }

        public int SubCount
        {
            get { return Files.Count(f => f.Kind == MediaKind.Subtitle); }
        }

        public string SubText
        {
            get
            {
                string s = VideoCount + " 个视频";
                if (SubCount > 0) s += " · " + SubCount + " 字幕";
                s += " · " + MatchState;
                return s;
            }
        }

        public string EffectiveTitle
        {
            get
            {
                if (!string.IsNullOrEmpty(Title)) return Title;
                if (!string.IsNullOrEmpty(DetectedTitle)) return DetectedTitle;
                return DisplayName;
            }
        }
    }

    public class PlanOptions
    {
        public string Template = "{title} - S{season:02d}E{ep:02d}";
        public bool RenameSubs = true;
        public bool RenameSidecars = true;
        public bool IncludeSpecials = true;
        public bool SkipExtras = true;
        public string Language = "cn";
    }

    public class ProgressInfo
    {
        public int Percent;
        public string Message = "";

        public ProgressInfo() { }

        public ProgressInfo(int percent, string message)
        {
            Percent = percent;
            Message = message;
        }
    }

    public class RenameResult
    {
        public int Renamed;
        public int Skipped;
        public int Failed;
        public List<string> Errors = new List<string>();
        public string HistoryPath = "";
    }

    public class UndoResult
    {
        public int Restored;
        public int Skipped;
        public int Failed;
        public string Message = "";
        public List<string> Errors = new List<string>();
    }

    public class HistoryEntry
    {
        public string Time = "";
        public string Root = "";
        public List<HistoryMove> Moves = new List<HistoryMove>();
    }

    public class HistoryMove
    {
        public string From = "";
        public string To = "";
        public string Kind = "";
    }

    public class Scanner
    {
        private static readonly string[] SkipDirs = {
            "$RECYCLE.BIN", "System Volume Information", ".git", ".svn", "@eaDir",
            "#recycle", "node_modules", "__pycache__", ".anr", ".dsh"
        };

        public static List<FileItem> Scan(string root, bool recursive)
        {
            var list = new List<FileItem>();
            var stack = new Stack<string>();
            stack.Push(root);
            int guard = 0;
            while (stack.Count > 0 && guard++ < 20000)
            {
                string dir = stack.Pop();
                string[] files;
                string[] dirs;
                try
                {
                    files = Directory.GetFiles(dir);
                    dirs = Directory.GetDirectories(dir);
                }
                catch
                {
                    continue;
                }

                foreach (string f in files)
                {
                    string ext = Path.GetExtension(f);
                    MediaKind kind;
                    if (!NameParser.TryKind(ext, out kind)) continue;
                    long size = 0;
                    try { size = new FileInfo(f).Length; } catch { }
                    list.Add(new FileItem
                    {
                        Path = f,
                        Name = Path.GetFileName(f),
                        Size = size,
                        Kind = kind,
                        Parsed = NameParser.Parse(f)
                    });
                }

                if (recursive)
                {
                    foreach (string d in dirs)
                    {
                        string n = Path.GetFileName(d);
                        if (SkipDirs.Any(s => string.Equals(s, n, StringComparison.OrdinalIgnoreCase))) continue;
                        try
                        {
                            if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue;
                        }
                        catch { continue; }
                        stack.Push(d);
                    }
                }
            }

            list.Sort(delegate(FileItem a, FileItem b) { return NameParser.NaturalCompare(a.Path, b.Path); });
            return list;
        }
    }

    public class NaturalComparer : IComparer<string>
    {
        public int Compare(string a, string b)
        {
            return NameParser.NaturalCompare(a, b);
        }
    }
}
