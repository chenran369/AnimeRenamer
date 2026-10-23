using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AnimeRenamer
{
    public static class SelfTest
    {
        public static int Run(string dataDir)
        {
            var sb = new StringBuilder();
            int pass = 0;
            int fail = 0;

            Action<string, bool, string> check = delegate(string name, bool ok, string detail)
            {
                if (ok) { pass++; sb.AppendLine("PASS  " + name); }
                else { fail++; sb.AppendLine("FAIL  " + name + "   " + detail); }
            };

            sb.AppendLine("AnimeRenamer self test  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("data dir: " + dataDir);
            sb.AppendLine();

            // ---------- parser ----------
            check("episode: dash", NameParser.Parse("[Lilith-Raws] Sousou no Frieren - 01 [Baha][WEB-DL][1080p][AVC AAC][CHT][MP4].mp4").Episode == 1, "expected 1");
            check("episode: bracket", NameParser.Parse("[VCB-Studio] Bocchi the Rock! [01][Ma10p_2160p][x265_flac].mkv").Episode == 1, "expected 1");
            check("episode: chinese", NameParser.Parse("葬送的芙莉莲 第01话.mp4").Episode == 1, "expected 1");
            check("episode: sxxexx", NameParser.Parse("Sousou no Frieren S01E03 1080p.mkv").Episode == 3, "expected 3");
            check("season: sxxexx", NameParser.Parse("Sousou no Frieren S02E07 1080p.mkv").Season == 2, "expected 2");
            check("episode: bracket group", NameParser.Parse("[Nekomoe kissaten][Title][12][1080p][CHS].mp4").Episode == 12, "expected 12");
            check("episode: v2", NameParser.Parse("Title - 05v2.mkv").Episode == 5, "expected 5");
            check("episode: number in title", NameParser.Parse("[Group] Title 2 - 05 [1080p].mkv").Episode == 5, "expected 5 (not 2)");
            check("episode: no false positive", NameParser.Parse("[Group] Title [1080p][5.1].mkv").Episode == -1, "expected -1");
            check("episode: chinese ji", NameParser.Parse("Title - 第5集.mkv").Episode == 5, "expected 5");
            check("episode: cjk glued", NameParser.Parse("轮到你了 番外篇03.mp4").Episode == 3, "expected 3");
            check("episode: cjk glued cut", NameParser.Parse("轮到你了 番外篇08cut.mp4").Episode == 8, "expected 8");
            check("episode: cjk glued CUT", NameParser.Parse("番外篇09CUT.mp4").Episode == 9, "expected 9");
            check("episode: cjk glued fix", NameParser.Parse("番外篇10fix.mp4").Episode == 10, "expected 10");
            check("episode: cjk glued xiuzheng", NameParser.Parse("番外篇11修正.mp4").Episode == 11, "expected 11");
            check("episode: dash cut", NameParser.Parse("Title - 12cut.mkv").Episode == 12, "expected 12");
            check("episode: cjk cut resolution", NameParser.Parse("番外篇13cut 1080p.mp4").Episode == 13, "expected 13");
            check("episode: cjk decimal cut", NameParser.Parse("番外篇5.1cut.mp4").Episode == -1, "expected -1");
            check("episode: cjk 1080cut", NameParser.Parse("番外篇1080cut.mp4").Episode == -1, "expected -1");
            check("episode: cjk 2019cut", NameParser.Parse("番外篇2019cut.mp4").Episode == -1, "expected -1");
            check("episode: latin cut rejected", NameParser.Parse("Title03cut.mp4").Episode == -1, "expected -1");
            check("episode: cjk glued bracket", NameParser.Parse("[Group] 轮到你了 番外篇03 [1080p].mp4").Episode == 3, "expected 3");
            check("episode: cjk glued hua", NameParser.Parse("番外篇03话.mp4").Episode == 3, "expected 3");
            check("episode: cjk glued v2", NameParser.Parse("番外篇03v2.mp4").Episode == 3, "expected 3");
            check("episode: cjk year", NameParser.Parse("番外篇2019.mp4").Episode == -1, "expected -1");
            check("episode: cjk 1080", NameParser.Parse("番外篇1080.mp4").Episode == -1, "expected -1");
            check("episode: cjk decimal", NameParser.Parse("番外篇5.1.mp4").Episode == -1, "expected -1");
            check("episode: latin glued rejected", NameParser.Parse("Title03.mkv").Episode == -1, "expected -1");
            check("extra: ncop", NameParser.Parse("[Group] Title [NCOP01][1080p].mkv").IsExtra, "expected extra");
            check("special: sp", NameParser.Parse("Title SP03.mkv").IsSpecial && NameParser.Parse("Title SP03.mkv").Episode == 3, "expected SP 3");

            ParsedName lang = NameParser.Parse("Title - 01 [1080p][CHS].ass");
            check("lang: chs", lang.SubLang == "chs", "got " + lang.SubLang);
            ParsedName parsedRes = NameParser.Parse("Title - 01 [1080p].mkv");
            check("resolution: 1080p", parsedRes.Resolution == "1080p", "got " + parsedRes.Resolution);

            // ---------- title ----------
            check("title: bracket release",
                NameParser.CleanTitle("[VCB-Studio] Sousou no Frieren [Ma10p_1080p]") == "Sousou no Frieren",
                "got " + NameParser.CleanTitle("[VCB-Studio] Sousou no Frieren [Ma10p_1080p]"));
            check("title: 2nd season",
                NameParser.CleanTitle("Sousou no Frieren 2nd Season") == "Sousou no Frieren",
                "got " + NameParser.CleanTitle("Sousou no Frieren 2nd Season"));
            check("title: fully bracketed",
                NameParser.CleanTitle("[Group][Sousou no Frieren]") == "Sousou no Frieren",
                "got " + NameParser.CleanTitle("[Group][Sousou no Frieren]"));
            check("title: cjk slash",
                NameParser.CleanTitle("葬送的芙莉莲 / Sousou no Frieren") == "葬送的芙莉莲",
                "got " + NameParser.CleanTitle("葬送的芙莉莲 / Sousou no Frieren"));
            check("title: latin slash kept",
                NameParser.CleanTitle("Fate / Stay Night") == "Fate / Stay Night",
                "got " + NameParser.CleanTitle("Fate / Stay Night"));

            // ---------- template ----------
            var tokens = new Dictionary<string, object>();
            tokens["title"] = "测试番剧";
            tokens["season"] = 2;
            tokens["ep"] = 5;
            string t = NameParser.ApplyTemplate("{title} - S{season:02d}E{ep:02d}", tokens);
            check("template: emby", t == "测试番剧 - S02E05", "got " + t);
            t = NameParser.ApplyTemplate("{title} 第{ep:02d}话", tokens);
            check("template: chinese", t == "测试番剧 第05话", "got " + t);

            // ---------- integration ----------
            string dir = Path.Combine(dataDir, "selftest_" + DateTime.Now.ToString("HHmmss_fff"));
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "Show - 01.mkv"), "v1");
                File.WriteAllText(Path.Combine(dir, "Show - 02.mkv"), "v2");
                File.WriteAllText(Path.Combine(dir, "Show - 01.chs.ass"), "s1");
                File.WriteAllText(Path.Combine(dir, "Show - 02.chs.ass"), "s2");
                File.WriteAllText(Path.Combine(dir, "Show - 01.nfo"), "n1");
                File.WriteAllText(Path.Combine(dir, "Show - NCOP01.mkv"), "ncop");
                File.WriteAllText(Path.Combine(dir, "Show SP01.mkv"), "sp");
                File.WriteAllText(Path.Combine(dir, "poster.jpg"), "img");

                var job = new FolderJob();
                job.Root = dir;
                job.Season = 2;
                job.Title = "测试番剧";
                job.Files = Scanner.Scan(dir, true);

                var opt = new PlanOptions();
                opt.Template = "{title} - S{season:02d}E{ep:02d}";
                Planner.Build(job, opt);

                check("plan: video count", job.Plan.Count(p => p.Kind == MediaKind.Video && p.Status == "就绪") == 3,
                    "got " + job.Plan.Count(p => p.Kind == MediaKind.Video && p.Status == "就绪"));
                check("plan: subtitle count", job.Plan.Count(p => p.Kind == MediaKind.Subtitle && p.Status == "就绪") == 2,
                    "got " + job.Plan.Count(p => p.Kind == MediaKind.Subtitle && p.Status == "就绪"));
                check("plan: nfo count", job.Plan.Count(p => p.Kind == MediaKind.Sidecar && p.Status == "就绪") == 1,
                    "got " + job.Plan.Count(p => p.Kind == MediaKind.Sidecar && p.Status == "就绪"));
                check("plan: ncop skipped", job.Plan.Any(p => p.Status == "跳过" && p.Source.EndsWith("NCOP01.mkv")), "ncop not skipped");
                check("plan: special s00", job.Plan.Any(p => p.Status == "就绪" && p.Target.EndsWith("S00E01.mkv", StringComparison.OrdinalIgnoreCase)), "special target missing");

                PlanItem firstPlan = job.Plan.First(p => p.Status == "就绪");
                check("path: raw exists", File.Exists(firstPlan.Source), "raw false: " + firstPlan.Source);
                check("path: lp exists", PathUtil.Exists(firstPlan.Source), "lp false: " + PathUtil.Lp(firstPlan.Source));

                RenameResult res = Renamer.Execute(job.Plan, dataDir, null);
                check("execute: renamed", res.Renamed == 6, "renamed " + res.Renamed + " skipped " + res.Skipped + " failed " + res.Failed +
                    (res.Errors.Count > 0 ? " | " + res.Errors[0] : ""));
                check("execute: video target", File.Exists(Path.Combine(dir, "测试番剧 - S02E01.mkv")), "video target missing");
                check("execute: subtitle target", File.Exists(Path.Combine(dir, "测试番剧 - S02E01.chs.ass")), "sub target missing");
                check("execute: special target", File.Exists(Path.Combine(dir, "测试番剧 - S00E01.mkv")), "special target missing");

                UndoResult undo = Renamer.UndoLast(dataDir, null);
                check("undo: restored", undo.Restored == 6, "restored " + undo.Restored + " " + undo.Message);
                check("undo: original back", File.Exists(Path.Combine(dir, "Show - 01.mkv")), "original missing");

                // offset / explicit season
                string dir2 = Path.Combine(dataDir, "selftest_offset");
                if (Directory.Exists(dir2)) Directory.Delete(dir2, true);
                Directory.CreateDirectory(dir2);
                File.WriteAllText(Path.Combine(dir2, "Show - 25.mkv"), "x");
                File.WriteAllText(Path.Combine(dir2, "Show - 26.mkv"), "x");
                File.WriteAllText(Path.Combine(dir2, "Show S03E25.mkv"), "x");
                var job2 = new FolderJob();
                job2.Root = dir2;
                job2.Season = 2;
                job2.Offset = -24;
                job2.Title = "测试番剧";
                job2.Files = Scanner.Scan(dir2, true);
                var opt2 = new PlanOptions();
                opt2.Template = "{title} - S{season:02d}E{ep:02d}";
                Planner.Build(job2, opt2);
                check("offset: 25 -> s02e01",
                    job2.Plan.Any(p => p.Status == "就绪" && p.Target.EndsWith("S02E01.mkv", StringComparison.OrdinalIgnoreCase)),
                    "offset target missing");
                check("season: explicit wins",
                    job2.Plan.Any(p => p.Status == "就绪" && p.Target.EndsWith("S03E01.mkv", StringComparison.OrdinalIgnoreCase)),
                    "explicit season target missing");
                if (Directory.Exists(dir2)) Directory.Delete(dir2, true);

                // already-correct name with only a whitespace difference must be a no-op
                string dir3 = Path.Combine(dataDir, "selftest_space");
                if (Directory.Exists(dir3)) Directory.Delete(dir3, true);
                Directory.CreateDirectory(dir3);
                File.WriteAllText(Path.Combine(dir3, "赛博朋克：边缘行者2 - S02E01.mkv"), "x");
                var job3 = new FolderJob();
                job3.Root = dir3;
                job3.Season = 2;
                job3.Title = "赛博朋克：边缘行者 2";
                job3.Files = Scanner.Scan(dir3, true);
                var opt3 = new PlanOptions();
                opt3.Template = "{title} - S{season:02d}E{ep:02d}";
                Planner.Build(job3, opt3);
                check("no-op: whitespace only",
                    job3.Plan.Count == 1 && job3.Plan[0].Status == "无变化",
                    job3.Plan.Count == 0 ? "empty plan" : job3.Plan[0].Status + " | " + job3.Plan[0].Target);
                if (Directory.Exists(dir3)) Directory.Delete(dir3, true);
            }
            catch (Exception ex)
            {
                fail++;
                sb.AppendLine("FAIL  integration exception: " + ex);
            }
            finally
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            }

            sb.AppendLine();
            sb.AppendLine("RESULT  pass=" + pass + " fail=" + fail);
            try { File.WriteAllText(Path.Combine(dataDir, "selftest-result.txt"), sb.ToString(), new UTF8Encoding(false)); }
            catch { }
            return fail;
        }
    }
}
