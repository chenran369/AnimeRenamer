using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace AnimeRenamer
{
    public static class MetadataClient
    {
        private const string BangumiUa = "AnimeRenamer/1.0 (Windows NT 10.0; Win64; x64)";
        private const string TmdbUa = "AnimeRenamer/1.0";

        private static string BangumiBase
        {
            get
            {
                string b = Environment.GetEnvironmentVariable("ANIME_RENAMER_BGM_BASE");
                return string.IsNullOrWhiteSpace(b) ? "https://api.bgm.tv" : b.TrimEnd('/');
            }
        }

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, List<SubjectInfo>> SearchCache = new Dictionary<string, List<SubjectInfo>>();
        private static readonly Dictionary<string, Dictionary<int, string>> EpCache = new Dictionary<string, Dictionary<int, string>>();

        public static List<SubjectInfo> Search(string source, string keyword, Config cfg)
        {
            keyword = (keyword ?? "").Trim();
            if (keyword.Length == 0) return new List<SubjectInfo>();
            source = (source ?? "bangumi").ToLowerInvariant();
            if (source == "local") return new List<SubjectInfo>();

            string key = source + "|" + keyword.ToLowerInvariant() + "|" + (source == "tmdb" ? cfg.TmdbKey : "");
            lock (CacheLock)
            {
                List<SubjectInfo> hit;
                if (SearchCache.TryGetValue(key, out hit))
                {
                    return hit.Select(Clone).ToList();
                }
            }

            List<SubjectInfo> result;
            if (source == "tmdb") result = SearchTmdb(keyword, cfg);
            else result = SearchBangumi(keyword, cfg);

            lock (CacheLock)
            {
                if (SearchCache.Count > 120) SearchCache.Clear();
                SearchCache[key] = result.Select(Clone).ToList();
            }
            return result;
        }

        private static SubjectInfo Clone(SubjectInfo s)
        {
            return new SubjectInfo
            {
                Source = s.Source,
                Id = s.Id,
                Name = s.Name,
                NameCn = s.NameCn,
                Year = s.Year,
                Eps = s.Eps,
                Score = s.Score
            };
        }

        public static Dictionary<int, string> GetEpisodeTitles(SubjectInfo subject, int season, Config cfg)
        {
            if (subject == null || string.IsNullOrEmpty(subject.Id)) return new Dictionary<int, string>();
            string key = subject.Source + "|" + subject.Id + "|" + season;
            lock (CacheLock)
            {
                Dictionary<int, string> hit;
                if (EpCache.TryGetValue(key, out hit)) return new Dictionary<int, string>(hit);
            }

            Dictionary<int, string> result;
            if (subject.Source == "tmdb") result = TmdbEpisodes(subject.Id, season, cfg);
            else result = BangumiEpisodes(subject.Id, cfg);

            lock (CacheLock)
            {
                if (EpCache.Count > 300) EpCache.Clear();
                EpCache[key] = new Dictionary<int, string>(result);
            }
            return result;
        }

        public static bool TestConnection(string source, Config cfg, out string message)
        {
            message = "";
            try
            {
                if (source == "tmdb")
                {
                    string url = "https://api.themoviedb.org/3/configuration";
                    if (!IsBearer(cfg.TmdbKey)) url += "?api_key=" + Uri.EscapeDataString(cfg.TmdbKey);
                    Http(url, cfg, null, false, IsBearer(cfg.TmdbKey) ? "Bearer " + cfg.TmdbKey : null);
                    message = "TMDB 连接正常";
                }
                else
                {
                    Http("https://api.bgm.tv/v0/subjects/253", cfg, null, false, null);
                    message = "Bangumi 连接正常";
                }
                return true;
            }
            catch (Exception ex)
            {
                message = "连接失败：" + ex.Message;
                return false;
            }
        }

        private static List<SubjectInfo> SearchBangumi(string keyword, Config cfg)
        {
            var payload = new Dictionary<string, object>();
            payload["keyword"] = keyword;
            payload["sort"] = "match";
            var filter = new Dictionary<string, object>();
            filter["type"] = new int[] { 2 };
            payload["filter"] = filter;

            var ser = new JavaScriptSerializer();
            string body = ser.Serialize(payload);
            string url = BangumiBase + "/v0/search/subjects?limit=12&offset=0";

            string json;
            try
            {
                json = Http(url, cfg, body, true, null);
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                if (resp != null && resp.StatusCode == HttpStatusCode.NotFound) return new List<SubjectInfo>();
                throw Friendly(ex, "Bangumi 搜索");
            }

            BgmSearchResponse data;
            try { data = Deserialize<BgmSearchResponse>(json); }
            catch (Exception ex) { throw new Exception("Bangumi 返回数据无法解析：" + ex.Message); }
            if (data == null || data.data == null) return new List<SubjectInfo>();

            var list = new List<SubjectInfo>();
            foreach (BgmSubject s in data.data)
            {
                var info = new SubjectInfo();
                info.Source = "bangumi";
                info.Id = s.id.ToString();
                info.Name = s.name ?? "";
                info.NameCn = s.name_cn ?? "";
                info.Year = ParseYear(s.date);
                info.Eps = s.eps;
                if (s.rating != null) info.Score = s.rating.score;
                list.Add(info);
            }
            return list;
        }

        private static Dictionary<int, string> BangumiEpisodes(string id, Config cfg)
        {
            var result = new Dictionary<int, string>();
            int offset = 0;
            int total = int.MaxValue;
            int guard = 0;
            while (offset < total && guard++ < 12)
            {
                string url = BangumiBase + "/v0/episodes?subject_id=" + Uri.EscapeDataString(id) +
                             "&type=0&limit=100&offset=" + offset;
                string json = Http(url, cfg, null, false, null);
                BgmEpisodeResponse resp;
                try { resp = Deserialize<BgmEpisodeResponse>(json); }
                catch (Exception ex) { throw new Exception("Bangumi 剧集数据无法解析：" + ex.Message); }
                if (resp == null || resp.data == null) break;

                total = resp.total;
                if (total <= 0) total = offset + resp.data.Count;
                for (int i = 0; i < resp.data.Count; i++)
                {
                    BgmEpisode e = resp.data[i];
                    int num = (int)Math.Round(e.sort);
                    if (num <= 0) num = (int)Math.Round(e.ep);
                    if (num <= 0) num = offset + i + 1;
                    string title = !string.IsNullOrWhiteSpace(e.name_cn) ? e.name_cn : e.name;
                    if (!string.IsNullOrWhiteSpace(title)) result[num] = title.Trim();
                }
                offset += resp.data.Count;
                if (resp.data.Count == 0) break;
            }
            return result;
        }

        private static List<SubjectInfo> SearchTmdb(string keyword, Config cfg)
        {
            if (string.IsNullOrWhiteSpace(cfg.TmdbKey))
                throw new Exception("尚未配置 TMDB API Key，请在“设置”中填写，或改用 Bangumi。");

            bool bearer = IsBearer(cfg.TmdbKey);
            string url = "https://api.themoviedb.org/3/search/tv?language=zh-CN&include_adult=false&page=1&query=" +
                         Uri.EscapeDataString(keyword);
            if (!bearer) url += "&api_key=" + Uri.EscapeDataString(cfg.TmdbKey);

            string json;
            try { json = Http(url, cfg, null, false, bearer ? "Bearer " + cfg.TmdbKey : null); }
            catch (WebException ex) { throw Friendly(ex, "TMDB 搜索"); }

            TmdbSearchResponse data;
            try { data = Deserialize<TmdbSearchResponse>(json); }
            catch (Exception ex) { throw new Exception("TMDB 返回数据无法解析：" + ex.Message); }
            if (data == null || data.results == null) return new List<SubjectInfo>();

            var list = new List<SubjectInfo>();
            foreach (TmdbTv t in data.results)
            {
                if (t == null) continue;
                var info = new SubjectInfo();
                info.Source = "tmdb";
                info.Id = t.id.ToString();
                info.Name = t.original_name ?? t.name ?? "";
                info.NameCn = t.name ?? "";
                info.Year = ParseYear(t.first_air_date);
                list.Add(info);
            }
            return list;
        }

        private static Dictionary<int, string> TmdbEpisodes(string id, int season, Config cfg)
        {
            if (string.IsNullOrWhiteSpace(cfg.TmdbKey)) return new Dictionary<int, string>();
            bool bearer = IsBearer(cfg.TmdbKey);
            string url = "https://api.themoviedb.org/3/tv/" + Uri.EscapeDataString(id) + "/season/" + season +
                         "?language=zh-CN";
            if (!bearer) url += "&api_key=" + Uri.EscapeDataString(cfg.TmdbKey);

            string json;
            try { json = Http(url, cfg, null, false, bearer ? "Bearer " + cfg.TmdbKey : null); }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                if (resp != null && resp.StatusCode == HttpStatusCode.NotFound)
                    return new Dictionary<int, string>();
                throw Friendly(ex, "TMDB 剧集");
            }

            TmdbSeason data;
            try { data = Deserialize<TmdbSeason>(json); }
            catch (Exception ex) { throw new Exception("TMDB 剧集数据无法解析：" + ex.Message); }
            var result = new Dictionary<int, string>();
            if (data == null || data.episodes == null) return result;
            foreach (TmdbEpisode e in data.episodes)
            {
                if (e == null || e.episode_number <= 0) continue;
                if (!string.IsNullOrWhiteSpace(e.name)) result[e.episode_number] = e.name.Trim();
            }
            return result;
        }

        private static bool IsBearer(string key)
        {
            return !string.IsNullOrEmpty(key) && key.StartsWith("eyJ", StringComparison.Ordinal) && key.Length > 80;
        }

        private static int ParseYear(string date)
        {
            if (string.IsNullOrEmpty(date) || date.Length < 4) return 0;
            int y;
            if (int.TryParse(date.Substring(0, 4), out y)) return y;
            return 0;
        }

        private static Exception Friendly(WebException ex, string action)
        {
            var resp = ex.Response as HttpWebResponse;
            if (resp != null)
            {
                int code = (int)resp.StatusCode;
                if (code == 401 || code == 403) return new Exception(action + "失败：API Key 无效或没有权限（HTTP " + code + "）。");
                if (code == 429) return new Exception(action + "失败：请求过于频繁，请稍后再试。");
                if (code == 404) return new Exception(action + "失败：没有找到内容（HTTP 404）。");
                return new Exception(action + "失败：服务器返回 HTTP " + code + "。");
            }
            if (ex.Status == WebExceptionStatus.Timeout)
                return new Exception(action + "失败：请求超时。请检查网络，或在设置中填写代理。");
            return new Exception(action + "失败：" + ex.Message + "。请检查网络连接，或在设置中填写代理。");
        }

        private static string Http(string url, Config cfg, string body, bool post, string authorization)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;

            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = url.IndexOf("themoviedb", StringComparison.OrdinalIgnoreCase) >= 0 ? TmdbUa : BangumiUa;
            req.Accept = "application/json";
            req.Timeout = 25000;
            req.ReadWriteTimeout = 25000;
            req.KeepAlive = true;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.6");

            if (!string.IsNullOrWhiteSpace(cfg.Proxy))
            {
                try { req.Proxy = new WebProxy(cfg.Proxy.Trim()); }
                catch { }
            }
            if (!string.IsNullOrEmpty(authorization))
                req.Headers.Add("Authorization", authorization);

            if (post)
            {
                req.Method = "POST";
                req.ContentType = "application/json";
                byte[] bytes = Encoding.UTF8.GetBytes(body ?? "{}");
                req.ContentLength = bytes.Length;
                using (Stream s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
            }

            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var stream = resp.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        private static T Deserialize<T>(string json)
        {
            var ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;
            ser.RecursionLimit = 100;
            return ser.Deserialize<T>(json);
        }

        // ---------- DTOs ----------

        public class BgmSearchResponse
        {
            public int total { get; set; }
            public List<BgmSubject> data { get; set; }
        }

        public class BgmSubject
        {
            public int id { get; set; }
            public string name { get; set; }
            public string name_cn { get; set; }
            public string date { get; set; }
            public int eps { get; set; }
            public BgmRating rating { get; set; }
        }

        public class BgmRating
        {
            public double score { get; set; }
        }

        public class BgmEpisodeResponse
        {
            public int total { get; set; }
            public List<BgmEpisode> data { get; set; }
        }

        public class BgmEpisode
        {
            public double sort { get; set; }
            public double ep { get; set; }
            public string name { get; set; }
            public string name_cn { get; set; }
        }

        public class TmdbSearchResponse
        {
            public List<TmdbTv> results { get; set; }
        }

        public class TmdbTv
        {
            public int id { get; set; }
            public string name { get; set; }
            public string original_name { get; set; }
            public string first_air_date { get; set; }
        }

        public class TmdbSeason
        {
            public List<TmdbEpisode> episodes { get; set; }
        }

        public class TmdbEpisode
        {
            public int episode_number { get; set; }
            public string name { get; set; }
        }
    }
}
