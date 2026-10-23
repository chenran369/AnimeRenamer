using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace AnimeRenamer
{
    public static class Program
    {
        public const string VersionText = "1.0.9";
        public static string DataDir;
        public static string DataDirNote = "";

        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            DataDir = Config.DefaultDataDir();
            string startPath = null;
            string renderPath = null;
            string renderSettings = null;
            string renderHelp = null;
            string apiTest = null;
            string renamePath = null;
            string renameTitle = "";
            int renameSeason = 0;
            int renameOffset = 0;
            bool renameYes = false;
            bool selfTest = false;
            bool demo = false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "--data-dir" && i + 1 < args.Length) { DataDir = args[++i]; continue; }
                if (a == "--selftest") { selfTest = true; continue; }
                if (a == "--render" && i + 1 < args.Length) { renderPath = args[++i]; continue; }
                if (a == "--render-settings" && i + 1 < args.Length) { renderSettings = args[++i]; continue; }
                if (a == "--render-help" && i + 1 < args.Length) { renderHelp = args[++i]; continue; }
                if (a == "--apitest" && i + 1 < args.Length) { apiTest = args[++i]; continue; }
                if (a == "--rename" && i + 1 < args.Length) { renamePath = args[++i]; continue; }
                if (a == "--title" && i + 1 < args.Length) { renameTitle = args[++i]; continue; }
                if (a == "--season" && i + 1 < args.Length) { int.TryParse(args[++i], out renameSeason); continue; }
                if (a == "--offset" && i + 1 < args.Length) { int.TryParse(args[++i], out renameOffset); continue; }
                if (a == "--yes") { renameYes = true; continue; }
                if (a == "--demo") { demo = true; continue; }
                if (!a.StartsWith("-") && Directory.Exists(args[i])) startPath = args[i];
            }

            try { Directory.CreateDirectory(DataDir); }
            catch { }

            DataDir = PrepareDataDir(DataDir);

            if (selfTest)
            {
                int failures = SelfTest.Run(DataDir);
                Environment.ExitCode = failures == 0 ? 0 : 1;
                return;
            }

            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                LogFatal(e.Exception);
                MessageBox.Show("程序遇到错误：\n" + e.Exception.Message, "出错了",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                LogFatal(e.ExceptionObject as Exception);
            };

            Config config = Config.Load(DataDir);

            if (!string.IsNullOrEmpty(apiTest))
            {
                RunApiTest(apiTest, config);
                return;
            }

            if (!string.IsNullOrEmpty(renamePath))
            {
                RunRename(renamePath, renameTitle, renameSeason, renameOffset, renameYes, config);
                return;
            }

            if (!string.IsNullOrEmpty(renderSettings))
            {
                using (var form = new SettingsForm(config, DataDir))
                {
                    SaveForm(form, renderSettings);
                }
                return;
            }

            if (!string.IsNullOrEmpty(renderHelp))
            {
                using (var form = new HelpForm())
                {
                    SaveForm(form, renderHelp);
                }
                return;
            }

            if (!string.IsNullOrEmpty(renderPath))
            {
                using (var form = new MainForm(config, DataDir, null))
                {
                    form.RenderTo(renderPath, demo);
                }
                return;
            }

            Application.Run(new MainForm(config, DataDir, startPath));
        }

        private static void LogFatal(Exception ex)
        {
            try
            {
                string path = Path.Combine(DataDir, "error.log");
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + (ex == null ? "unknown" : ex.ToString()) + Environment.NewLine);
            }
            catch { }
        }

        private static string PrepareDataDir(string preferred)
        {
            if (CanWriteDataDir(preferred)) return preferred;

            string temp = Path.Combine(Path.GetTempPath(), "AnimeRenamer");
            if (CanWriteDataDir(temp))
            {
                DataDirNote = "配置目录不可写，已临时改用：" + temp;
                return temp;
            }

            string exeDir = "";
            try { exeDir = Path.GetDirectoryName(Application.ExecutablePath); } catch { }
            if (!string.IsNullOrEmpty(exeDir))
            {
                string portable = Path.Combine(exeDir, "AnimeRenamer-data");
                if (CanWriteDataDir(portable))
                {
                    DataDirNote = "配置目录不可写，已临时改用：" + portable;
                    return portable;
                }
            }

            return preferred;
        }

        private static bool CanWriteDataDir(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".write-test-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void RunRename(string path, string title, int season, int offset, bool yes, Config cfg)
        {
            var sb = new StringBuilder();
            int failures = 0;
            try
            {
                string root = Path.GetFullPath(path);
                if (!Directory.Exists(root)) throw new Exception("文件夹不存在：" + root);

                var job = new FolderJob();
                job.Root = root;
                job.Recursive = true;
                job.Season = season > 0 ? season : 1;
                job.Offset = offset;
                job.Files = Scanner.Scan(root, true);

                FileItem first = null;
                foreach (FileItem f in job.Files)
                {
                    if (f.Kind == MediaKind.Video) { first = f; break; }
                }
                if (first == null && job.Files.Count > 0) first = job.Files[0];

                if (string.IsNullOrWhiteSpace(title))
                {
                    title = first != null ? NameParser.DetectTitle(root, first.Path) : NameParser.CleanTitle(Path.GetFileName(root));
                }
                if (string.IsNullOrWhiteSpace(title)) title = NameParser.CleanTitle(Path.GetFileName(root));
                job.Title = title;

                if (season <= 0 && first != null)
                    job.Season = first.Parsed.SeasonExplicit ? first.Parsed.Season : Math.Max(1, NameParser.DetectSeason(first.Path, root));

                var opt = new PlanOptions();
                opt.Template = cfg.Template;
                opt.RenameSubs = cfg.RenameSubs;
                opt.RenameSidecars = cfg.RenameSidecars;
                opt.IncludeSpecials = cfg.IncludeSpecials;
                opt.SkipExtras = cfg.SkipExtras;

                Planner.Build(job, opt);

                sb.AppendLine("root: " + root);
                sb.AppendLine("title: " + job.Title);
                sb.AppendLine("season: " + job.Season + "  offset: " + job.Offset);
                sb.AppendLine("files: " + job.Files.Count + "  plan: " + job.Plan.Count);
                foreach (PlanItem p in job.Plan)
                {
                    sb.AppendLine(p.Status + " | " + p.OldName + " -> " + p.NewName + (string.IsNullOrEmpty(p.Note) ? "" : " | " + p.Note));
                }

                if (yes)
                {
                    RenameResult result = Renamer.Execute(job.Plan, DataDir, null);
                    failures = result.Failed;
                    sb.AppendLine();
                    sb.AppendLine("RESULT renamed=" + result.Renamed + " skipped=" + result.Skipped + " failed=" + result.Failed);
                    sb.AppendLine("history: " + result.HistoryPath);
                    foreach (string err in result.Errors) sb.AppendLine("ERROR " + err);
                }
                else
                {
                    sb.AppendLine();
                    sb.AppendLine("DRY RUN (add --yes to execute)");
                }
            }
            catch (Exception ex)
            {
                failures = 1;
                sb.AppendLine("ERROR " + ex);
            }

            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(Path.Combine(DataDir, "rename-result.txt"), sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
            Environment.ExitCode = failures == 0 ? 0 : 1;
        }

        private static void SaveForm(Form form, string path)
        {
            form.Show();
            Application.DoEvents();
            using (var bmp = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
            {
                form.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            form.Close();
        }

        private static void RunApiTest(string keyword, Config cfg)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var results = MetadataClient.Search("bangumi", keyword, cfg);
                sb.AppendLine("results: " + results.Count);
                foreach (var s in results)
                {
                    sb.AppendLine(s.Source + "|" + s.Id + "|" + s.NameCn + "|" + s.Name + "|" + s.Year + "|" + s.Eps + "|" + s.Score);
                }
                if (results.Count > 0)
                {
                    var eps = MetadataClient.GetEpisodeTitles(results[0], 1, cfg);
                    sb.AppendLine("episodes: " + eps.Count);
                    foreach (var kv in eps) sb.AppendLine("  " + kv.Key + " = " + kv.Value);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("ERROR: " + ex);
            }
            try { File.WriteAllText(Path.Combine(DataDir, "apitest.txt"), sb.ToString(), new System.Text.UTF8Encoding(false)); }
            catch { }
        }
    }
}
