using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace AnimeRenamer
{
    public static class PathUtil
    {
        public static string Full(string p)
        {
            try { return Path.GetFullPath(p); }
            catch { return p; }
        }

        public static bool Same(string a, string b)
        {
            return string.Equals(Full(a), Full(b), StringComparison.Ordinal);
        }

        public static bool SameIgnoreCase(string a, string b)
        {
            return string.Equals(Full(a), Full(b), StringComparison.OrdinalIgnoreCase);
        }

        public static bool SameNameLoose(string a, string b)
        {
            string da = Path.GetDirectoryName(Full(a)) ?? "";
            string db = Path.GetDirectoryName(Full(b)) ?? "";
            if (!string.Equals(da, db, StringComparison.OrdinalIgnoreCase)) return false;
            string na = Regex.Replace(Path.GetFileName(a) ?? "", @"\s+", "");
            string nb = Regex.Replace(Path.GetFileName(b) ?? "", @"\s+", "");
            return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
        }

        public static string Lp(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
            // Normal paths stay untouched: some sandboxes / filters reject the \\?\ prefix.
            if (path.Length < 240) return path;
            if (path.StartsWith(@"\\", StringComparison.Ordinal)) return @"\\?\UNC\" + path.Substring(2);
            return @"\\?\" + path;
        }

        public static bool Exists(string path)
        {
            try
            {
                if (File.Exists(path)) return true;
                return File.Exists(Lp(path));
            }
            catch { return false; }
        }

        public static bool IsUnder(string path, string root)
        {
            string p = Full(path).TrimEnd('\\') + "\\";
            string r = Full(root).TrimEnd('\\') + "\\";
            return p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
        }

        public static void Move(string from, string to)
        {
            string dir = Path.GetDirectoryName(to);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(Lp(dir))) Directory.CreateDirectory(Lp(dir));
            File.Move(Lp(from), Lp(to));
        }
    }

    public static class Renamer
    {
        public static RenameResult Execute(List<PlanItem> all, string dataDir, IProgress<ProgressInfo> progress)
        {
            var result = new RenameResult();
            var items = all.Where(i => i.Included && i.Status == "就绪").ToList();
            if (items.Count == 0) return result;

            var history = new HistoryEntry();
            history.Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            if (items.Count > 0) history.Root = items[0].Root ?? "";

            var tempMoves = new List<KeyValuePair<PlanItem, string>>();
            var sourceSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PlanItem it in items) sourceSet.Add(PathUtil.Full(it.Source));

            for (int i = 0; i < items.Count; i++)
            {
                PlanItem item = items[i];
                if (progress != null) progress.Report(new ProgressInfo((int)(i * 50.0 / items.Count), "准备 " + item.OldName));

                if (!PathUtil.Exists(item.Source))
                {
                    result.Skipped++;
                    result.Errors.Add("文件不存在：" + item.Source);
                    continue;
                }
                if (string.IsNullOrEmpty(item.Target) || !PathUtil.IsUnder(item.Target, item.Root))
                {
                    result.Failed++;
                    result.Errors.Add("目标路径不在原目录内，已跳过：" + item.Target);
                    continue;
                }
                if (!PathUtil.SameIgnoreCase(item.Source, item.Target) && PathUtil.Exists(item.Target))
                {
                    bool targetIsSource = sourceSet.Contains(PathUtil.Full(item.Target)) && !PathUtil.Same(item.Source, item.Target);
                    if (!targetIsSource)
                    {
                        result.Skipped++;
                        result.Errors.Add("目标已存在，已跳过：" + item.Target);
                        continue;
                    }
                }

                try
                {
                    string dir = Path.GetDirectoryName(item.Source);
                    string temp = Path.Combine(dir, ".anr-tmp-" + Guid.NewGuid().ToString("N").Substring(0, 10) + Path.GetExtension(item.Source));
                    PathUtil.Move(item.Source, temp);
                    tempMoves.Add(new KeyValuePair<PlanItem, string>(item, temp));
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Errors.Add("无法重命名：" + Path.GetFileName(item.Source) + " — " + ex.Message);
                }
            }

            for (int i = 0; i < tempMoves.Count; i++)
            {
                PlanItem item = tempMoves[i].Key;
                string temp = tempMoves[i].Value;
                if (progress != null) progress.Report(new ProgressInfo(50 + (int)(i * 50.0 / tempMoves.Count), "重命名 " + item.NewName));

                try
                {
                    if (!PathUtil.SameIgnoreCase(temp, item.Target) && PathUtil.Exists(item.Target))
                        throw new IOException("目标文件已存在");

                    PathUtil.Move(temp, item.Target);
                    result.Renamed++;
                    var mv = new HistoryMove();
                    mv.From = item.Source;
                    mv.To = item.Target;
                    mv.Kind = item.KindText;
                    history.Moves.Add(mv);
                }
                catch (Exception ex)
                {
                    string restored = "";
                    try
                    {
                        PathUtil.Move(temp, item.Source);
                        restored = "，已还原";
                    }
                    catch
                    {
                        restored = "，临时文件：" + temp;
                    }
                    result.Failed++;
                    result.Errors.Add("重命名失败：" + Path.GetFileName(item.Target) + " — " + ex.Message + restored);
                }
            }

            if (history.Moves.Count > 0)
                result.HistoryPath = SaveHistory(dataDir, history);

            if (progress != null) progress.Report(new ProgressInfo(100, "完成"));
            return result;
        }

        public static UndoResult UndoLast(string dataDir, IProgress<ProgressInfo> progress)
        {
            var result = new UndoResult();
            string dir = Path.Combine(dataDir, "history");
            if (!Directory.Exists(dir))
            {
                result.Message = "没有可撤销的重命名记录。";
                return result;
            }

            List<string> files;
            try
            {
                files = Directory.GetFiles(dir, "*.json")
                    .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                    .ToList();
            }
            catch (Exception ex)
            {
                result.Message = "无法读取历史记录：" + ex.Message;
                return result;
            }

            if (files.Count == 0)
            {
                result.Message = "没有可撤销的重命名记录。";
                return result;
            }

            foreach (string file in files)
            {
                HistoryEntry entry = null;
                try
                {
                    var ser = new JavaScriptSerializer();
                    ser.MaxJsonLength = int.MaxValue;
                    entry = ser.Deserialize<HistoryEntry>(File.ReadAllText(file, Encoding.UTF8));
                }
                catch
                {
                    MarkUndone(file);
                    continue;
                }
                if (entry == null || entry.Moves == null || entry.Moves.Count == 0)
                {
                    MarkUndone(file);
                    continue;
                }

                int restored = 0;
                for (int i = entry.Moves.Count - 1; i >= 0; i--)
                {
                    HistoryMove mv = entry.Moves[i];
                    if (progress != null)
                        progress.Report(new ProgressInfo((int)((entry.Moves.Count - 1 - i) * 100.0 / entry.Moves.Count), "还原 " + Path.GetFileName(mv.From)));

                    try
                    {
                        if (PathUtil.Same(mv.From, mv.To)) continue;
                        bool toExists = PathUtil.Exists(mv.To);
                        bool fromExists = PathUtil.Exists(mv.From);

                        if (!toExists)
                        {
                            result.Skipped++;
                            continue;
                        }
                        if (fromExists && !PathUtil.SameIgnoreCase(mv.From, mv.To))
                        {
                            result.Skipped++;
                            result.Errors.Add("原文件已存在，跳过：" + mv.From);
                            continue;
                        }

                        if (PathUtil.SameIgnoreCase(mv.From, mv.To) && !PathUtil.Same(mv.From, mv.To))
                        {
                            string temp = Path.Combine(Path.GetDirectoryName(mv.To), ".anr-undo-" + Guid.NewGuid().ToString("N").Substring(0, 8) + Path.GetExtension(mv.To));
                            PathUtil.Move(mv.To, temp);
                            PathUtil.Move(temp, mv.From);
                        }
                        else
                        {
                            PathUtil.Move(mv.To, mv.From);
                        }
                        restored++;
                    }
                    catch (Exception ex)
                    {
                        result.Failed++;
                        result.Errors.Add("还原失败：" + Path.GetFileName(mv.To) + " — " + ex.Message);
                    }
                }

                if (restored > 0)
                {
                    result.Restored = restored;
                    result.Message = "已撤销最近一次重命名，还原 " + restored + " 个文件。";
                    MarkUndone(file);
                    if (progress != null) progress.Report(new ProgressInfo(100, "完成"));
                    return result;
                }
                MarkUndone(file);
            }

            result.Message = "最近的重命名记录已无法还原（文件可能已被移动或改名）。";
            return result;
        }

        private static void MarkUndone(string file)
        {
            try
            {
                string target = file.Substring(0, file.Length - 5) + ".undone.json";
                if (File.Exists(target)) File.Delete(target);
                File.Move(file, target);
            }
            catch { }
        }

        private static string SaveHistory(string dataDir, HistoryEntry entry)
        {
            try
            {
                string dir = Path.Combine(dataDir, "history");
                Directory.CreateDirectory(dir);
                string name = "history_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".json";
                string path = Path.Combine(dir, name);
                var ser = new JavaScriptSerializer();
                ser.MaxJsonLength = int.MaxValue;
                File.WriteAllText(path, ser.Serialize(entry), new UTF8Encoding(false));
                Prune(dir);
                return path;
            }
            catch
            {
                return "";
            }
        }

        private static void Prune(string dir)
        {
            try
            {
                var files = Directory.GetFiles(dir, "history_*.json")
                    .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                    .Skip(50)
                    .ToList();
                foreach (string f in files)
                {
                    try { File.Delete(f); } catch { }
                }
            }
            catch { }
        }
    }
}
