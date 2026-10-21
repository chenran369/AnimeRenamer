using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AnimeRenamer
{
    public static class Planner
    {
        public static void Build(FolderJob job, PlanOptions opt)
        {
            job.Plan.Clear();
            var videos = job.Files.Where(f => f.Kind == MediaKind.Video).ToList();
            videos.Sort(delegate(FileItem a, FileItem b) { return NameParser.NaturalCompare(a.Name, b.Name); });

            var videoPlans = new List<PlanItem>();
            var children = new Dictionary<PlanItem, List<PlanItem>>();
            var byKey = new Dictionary<string, PlanItem>(StringComparer.OrdinalIgnoreCase);
            var miscRows = new List<PlanItem>();
            var orphanRows = new List<PlanItem>();
            int autoSpecial = 1;

            foreach (FileItem f in videos)
            {
                ParsedName parsed = f.Parsed;

                if (parsed.IsExtra && opt.SkipExtras)
                {
                    miscRows.Add(MakeRow(job, f, f.Path, "跳过", "片头 / 片尾 / 预告片"));
                    continue;
                }

                if (parsed.IsSpecial)
                {
                    if (!opt.IncludeSpecials)
                    {
                        miscRows.Add(MakeRow(job, f, f.Path, "跳过", "特别篇（未启用）"));
                        continue;
                    }
                    if (parsed.Episode < 0) parsed.Episode = autoSpecial;
                    if (parsed.Episode >= autoSpecial) autoSpecial = parsed.Episode + 1;
                }

                if (!parsed.HasEpisode)
                {
                    miscRows.Add(MakeRow(job, f, f.Path, "未识别", "无法识别集数，可手动改名或排除"));
                    continue;
                }

                int season = parsed.IsSpecial ? 0 : (parsed.SeasonExplicit ? parsed.Season : job.Season);
                int absEp = parsed.Episode;
                int outEp = parsed.IsSpecial ? absEp : absEp + job.Offset;
                if (outEp < 1)
                {
                    miscRows.Add(MakeRow(job, f, f.Path, "冲突", "集数偏移后小于 1"));
                    continue;
                }

                string dir = Path.GetDirectoryName(f.Path);
                string ext = Path.GetExtension(f.Path);
                string baseName = NameParser.SanitizeName(BuildBase(job, opt, parsed, season, outEp, absEp));
                string targetBase = Path.Combine(dir, baseName);
                string target = targetBase + ext;

                var item = new PlanItem();
                item.Root = job.Root;
                item.Source = f.Path;
                item.Target = target;
                item.TargetBase = targetBase;
                item.Kind = MediaKind.Video;
                item.Season = season;
                item.Episode = outEp;
                item.AbsEpisode = absEp;
                item.IsSpecial = parsed.IsSpecial;
                item.Status = "就绪";
                item.Included = true;
                if (parsed.IsSpecial) item.Note = "特别篇";

                if (string.Equals(f.Path, target, StringComparison.Ordinal))
                {
                    item.Status = "无变化";
                    item.Included = false;
                    item.Note = "已是目标名称";
                }
                else if (string.Equals(f.Path, target, StringComparison.OrdinalIgnoreCase))
                {
                    item.Note = "仅大小写不同";
                }
                else if (PathUtil.SameNameLoose(f.Path, target))
                {
                    item.Status = "无变化";
                    item.Included = false;
                    item.Note = "仅空格差异，保持原名";
                }

                videoPlans.Add(item);
                if (item.Status == "就绪" || item.Status == "无变化")
                {
                    string key = Key(dir, season, absEp);
                    if (!byKey.ContainsKey(key)) byKey[key] = item;
                }
            }

            foreach (FileItem f in job.Files)
            {
                if (f.Kind == MediaKind.Video) continue;

                if (f.Kind == MediaKind.Subtitle)
                {
                    if (!opt.RenameSubs) continue;
                    PlanItem parent = FindParent(f, job, videoPlans, byKey);
                    if (parent == null)
                    {
                        orphanRows.Add(MakeRow(job, f, f.Path, "未匹配", "未找到对应视频"));
                        continue;
                    }
                    if (parent.Status == "跳过" || parent.Status == "未识别" || parent.Status == "冲突")
                    {
                        orphanRows.Add(MakeRow(job, f, f.Path, "跳过", "对应视频未处理"));
                        continue;
                    }

                    string lang = f.Parsed.SubLang;
                    string subBase = parent.TargetBase + (lang.Length > 0 ? "." + lang : "");
                    string target = subBase + Path.GetExtension(f.Path);

                    var item = new PlanItem();
                    item.Root = job.Root;
                    item.Source = f.Path;
                    item.Target = target;
                    item.TargetBase = subBase;
                    item.Kind = MediaKind.Subtitle;
                    item.Season = parent.Season;
                    item.Episode = parent.Episode;
                    item.AbsEpisode = parent.AbsEpisode;
                    item.IsSpecial = parent.IsSpecial;
                    item.Language = lang;
                    item.Included = true;
                    item.Status = parent.Status == "无变化" ? "无变化" : "就绪";
                    if (item.Status == "无变化")
                    {
                        item.Included = false;
                        item.Note = "随视频保持不变";
                    }
                    else if (string.Equals(f.Path, target, StringComparison.OrdinalIgnoreCase))
                    {
                        item.Status = "无变化";
                        item.Included = false;
                    }
                    else if (PathUtil.SameNameLoose(f.Path, target))
                    {
                        item.Status = "无变化";
                        item.Included = false;
                        item.Note = "仅空格差异，保持原名";
                    }

                    AddChild(children, parent, item);
                    continue;
                }

                if (f.Kind == MediaKind.Sidecar)
                {
                    if (!opt.RenameSidecars) continue;
                    PlanItem parent = FindParent(f, job, videoPlans, byKey);
                    if (parent == null) continue;

                    string suffix = SidecarSuffix(f, parent);
                    if (suffix == null) continue;
                    string target = parent.TargetBase + suffix + Path.GetExtension(f.Path);

                    var item = new PlanItem();
                    item.Root = job.Root;
                    item.Source = f.Path;
                    item.Target = target;
                    item.TargetBase = parent.TargetBase;
                    item.Kind = MediaKind.Sidecar;
                    item.Season = parent.Season;
                    item.Episode = parent.Episode;
                    item.AbsEpisode = parent.AbsEpisode;
                    item.IsSpecial = parent.IsSpecial;
                    item.Included = true;
                    item.Status = parent.Status == "无变化" ? "无变化" : "就绪";
                    if (item.Status == "无变化")
                    {
                        item.Included = false;
                        item.Note = "随视频保持不变";
                    }
                    else if (string.Equals(f.Path, target, StringComparison.OrdinalIgnoreCase))
                    {
                        item.Status = "无变化";
                        item.Included = false;
                    }
                    else if (PathUtil.SameNameLoose(f.Path, target))
                    {
                        item.Status = "无变化";
                        item.Included = false;
                        item.Note = "仅空格差异，保持原名";
                    }

                    AddChild(children, parent, item);
                }
            }

            var ordered = new List<PlanItem>();
            foreach (PlanItem vp in videoPlans)
            {
                ordered.Add(vp);
                List<PlanItem> kids;
                if (children.TryGetValue(vp, out kids)) ordered.AddRange(kids);
            }
            ordered.AddRange(orphanRows);
            ordered.AddRange(miscRows);

            MarkDuplicates(ordered);
            MarkExistingTargets(ordered);
            job.Plan.AddRange(ordered);
        }

        private static PlanItem MakeRow(FolderJob job, FileItem f, string target, string status, string note)
        {
            var item = new PlanItem();
            item.Root = job.Root;
            item.Source = f.Path;
            item.Target = target;
            item.TargetBase = Path.Combine(Path.GetDirectoryName(f.Path), Path.GetFileNameWithoutExtension(f.Path));
            item.Kind = f.Kind;
            item.Season = f.Parsed != null ? f.Parsed.Season : 1;
            item.Episode = f.Parsed != null ? f.Parsed.Episode : -1;
            item.IsSpecial = f.Parsed != null && f.Parsed.IsSpecial;
            item.Status = status;
            item.Note = note;
            item.Included = false;
            return item;
        }

        private static void AddChild(Dictionary<PlanItem, List<PlanItem>> map, PlanItem parent, PlanItem child)
        {
            List<PlanItem> list;
            if (!map.TryGetValue(parent, out list))
            {
                list = new List<PlanItem>();
                map[parent] = list;
            }
            list.Add(child);
        }

        private static PlanItem FindParent(FileItem f, FolderJob job, List<PlanItem> videos, Dictionary<string, PlanItem> byKey)
        {
            ParsedName p = f.Parsed;

            if (p.HasEpisode)
            {
                int season = p.IsSpecial ? 0 : (p.SeasonExplicit ? p.Season : job.Season);
                PlanItem hit;
                string key = Key(Path.GetDirectoryName(f.Path), season, p.Episode);
                if (byKey.TryGetValue(key, out hit)) return hit;
            }

            string dir = Path.GetDirectoryName(f.Path);
            string stem = Path.GetFileNameWithoutExtension(f.Name);
            PlanItem best = null;
            int bestLen = -1;
            foreach (PlanItem v in videos)
            {
                if (!string.Equals(Path.GetDirectoryName(v.Source), dir, StringComparison.OrdinalIgnoreCase)) continue;
                string vstem = Path.GetFileNameWithoutExtension(v.Source);
                if (string.Equals(stem, vstem, StringComparison.OrdinalIgnoreCase))
                {
                    if (vstem.Length > bestLen) { best = v; bestLen = vstem.Length; }
                }
                else if (stem.StartsWith(vstem + ".", StringComparison.OrdinalIgnoreCase) ||
                         stem.StartsWith(vstem + "-", StringComparison.OrdinalIgnoreCase) ||
                         stem.StartsWith(vstem + "_", StringComparison.OrdinalIgnoreCase) ||
                         stem.StartsWith(vstem + " ", StringComparison.OrdinalIgnoreCase))
                {
                    if (vstem.Length > bestLen) { best = v; bestLen = vstem.Length; }
                }
            }
            if (best != null) return best;

            if (!p.HasEpisode)
            {
                var sameDir = videos.Where(v => string.Equals(Path.GetDirectoryName(v.Source), dir, StringComparison.OrdinalIgnoreCase)).ToList();
                if (sameDir.Count == 1) return sameDir[0];
            }
            return null;
        }

        private static string SidecarSuffix(FileItem f, PlanItem parent)
        {
            string stem = Path.GetFileNameWithoutExtension(f.Name);
            string vstem = Path.GetFileNameWithoutExtension(parent.Source);
            if (string.Equals(stem, vstem, StringComparison.OrdinalIgnoreCase)) return "";

            string[] known = { "-thumb", "-poster", "-fanart", "-banner", "-landscape", "-logo", "-backdrop", "-clearlogo", "-cover" };
            foreach (string k in known)
            {
                if (string.Equals(stem, vstem + k, StringComparison.OrdinalIgnoreCase)) return k;
            }

            string generic = stem.ToLowerInvariant();
            string[] noise = { "poster", "fanart", "banner", "logo", "folder", "cover", "backdrop", "thumb", "clearart", "disc", "keyart" };
            if (noise.Contains(generic)) return null;

            return null;
        }

        private static string Key(string dir, int season, int ep)
        {
            return (dir ?? "").ToLowerInvariant() + "|" + season + "|" + ep;
        }

        private static string BuildBase(FolderJob job, PlanOptions opt, ParsedName parsed, int season, int outEp, int absEp)
        {
            var tokens = new Dictionary<string, object>();
            string title = job.EffectiveTitle;
            tokens["title"] = title;
            tokens["season"] = season;
            tokens["ep"] = outEp;
            tokens["year"] = job.Year > 0 ? job.Year.ToString() : "";
            tokens["resolution"] = parsed.Resolution ?? "";
            tokens["group"] = parsed.Group ?? "";
            tokens["title_cn"] = job.Subject != null ? job.Subject.NameCn : title;
            tokens["title_orig"] = job.Subject != null ? job.Subject.Name : job.DetectedTitle;

            string epTitle = "";
            if (job.EpisodeTitles != null)
            {
                string t;
                if (job.EpisodeTitles.TryGetValue(outEp, out t)) epTitle = t;
                else if (absEp != outEp && job.EpisodeTitles.TryGetValue(absEp, out t)) epTitle = t;
            }
            tokens["ep_title"] = epTitle;
            tokens["lang"] = "";

            string template = string.IsNullOrWhiteSpace(opt.Template) ? Config.DefaultTemplate : opt.Template;
            return NameParser.ApplyTemplate(template, tokens);
        }

        private static void MarkDuplicates(List<PlanItem> ordered)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (PlanItem i in ordered)
            {
                if (!i.Included || i.Status != "就绪") continue;
                string k = i.Target;
                int c;
                counts.TryGetValue(k, out c);
                counts[k] = c + 1;
            }
            foreach (PlanItem i in ordered)
            {
                if (!i.Included || i.Status != "就绪") continue;
                int c;
                if (counts.TryGetValue(i.Target, out c) && c > 1)
                {
                    i.Status = "冲突";
                    i.Note = "新文件名重复（" + c + " 个文件）";
                    i.Included = false;
                }
            }
        }

        private static void MarkExistingTargets(List<PlanItem> ordered)
        {
            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PlanItem i in ordered)
            {
                if (i.Included && i.Status == "就绪") sources.Add(PathUtil.Full(i.Source));
            }

            foreach (PlanItem i in ordered)
            {
                if (!i.Included || i.Status != "就绪") continue;
                if (PathUtil.Same(i.Source, i.Target)) continue;

                bool exists;
                try { exists = PathUtil.Exists(i.Target); }
                catch { exists = false; }
                if (!exists) continue;

                string tf = PathUtil.Full(i.Target);
                if (sources.Contains(tf))
                {
                    if (PathUtil.Same(i.Source, i.Target)) continue;
                    continue;
                }
                if (PathUtil.SameIgnoreCase(i.Source, i.Target)) continue;

                i.Status = "冲突";
                i.Note = "目标文件已存在";
                i.Included = false;
            }
        }
    }
}
