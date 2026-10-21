using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace AnimeRenamer
{
    public class Config
    {
        public string Template = "{title} - S{season:02d}E{ep:02d}";
        public string Source = "bangumi";
        public string Language = "cn";
        public string TmdbKey = "";
        public string Proxy = "";
        public bool Recursive = true;
        public bool RenameSubs = true;
        public bool RenameSidecars = true;
        public bool IncludeSpecials = true;
        public bool SkipExtras = true;
        public int WindowW = 1200;
        public int WindowH = 800;
        public List<string> RecentTemplates = new List<string>();

        public const string DefaultTemplate = "{title} - S{season:02d}E{ep:02d}";

        public static string DefaultDataDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnimeRenamer");
        }

        public static Config Load(string dataDir)
        {
            var cfg = new Config();
            try
            {
                string path = Path.Combine(dataDir, "config.json");
                if (!File.Exists(path)) return cfg;
                string json = File.ReadAllText(path, Encoding.UTF8);
                var ser = new JavaScriptSerializer();
                var loaded = ser.Deserialize<Config>(json);
                if (loaded == null) return cfg;

                if (!string.IsNullOrWhiteSpace(loaded.Template)) cfg.Template = loaded.Template;
                if (!string.IsNullOrWhiteSpace(loaded.Source)) cfg.Source = loaded.Source;
                if (!string.IsNullOrWhiteSpace(loaded.Language)) cfg.Language = loaded.Language;
                cfg.TmdbKey = loaded.TmdbKey ?? "";
                cfg.Proxy = loaded.Proxy ?? "";
                cfg.Recursive = loaded.Recursive;
                cfg.RenameSubs = loaded.RenameSubs;
                cfg.RenameSidecars = loaded.RenameSidecars;
                cfg.IncludeSpecials = loaded.IncludeSpecials;
                cfg.SkipExtras = loaded.SkipExtras;
                if (loaded.WindowW >= 1040) cfg.WindowW = loaded.WindowW;
                if (loaded.WindowH >= 600) cfg.WindowH = loaded.WindowH;
                if (loaded.RecentTemplates != null) cfg.RecentTemplates = loaded.RecentTemplates;
            }
            catch
            {
                // broken config - fall back to defaults
            }
            return cfg;
        }

        public void Save(string dataDir)
        {
            try
            {
                Directory.CreateDirectory(dataDir);
                string path = Path.Combine(dataDir, "config.json");
                var ser = new JavaScriptSerializer();
                string json = ser.Serialize(this);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                File.Copy(tmp, path, true);
                try { File.Delete(tmp); } catch { }
            }
            catch
            {
                // non fatal
            }
        }

        public void RememberTemplate(string template)
        {
            if (string.IsNullOrWhiteSpace(template)) return;
            if (RecentTemplates == null) RecentTemplates = new List<string>();
            RecentTemplates.RemoveAll(x => string.Equals(x, template, StringComparison.Ordinal));
            RecentTemplates.Insert(0, template);
            while (RecentTemplates.Count > 8) RecentTemplates.RemoveAt(RecentTemplates.Count - 1);
        }
    }
}
