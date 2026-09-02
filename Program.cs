using System;
using System.Windows.Forms;
using System.Net;
using System.Net.Cache;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using System.Drawing;

namespace EarthLiveSharp
{
    static class Program
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (System.Environment.OSVersion.Version.Major >= 6) { SetProcessDPIAware(); }
            if (File.Exists(Application.StartupPath + @"\trace.log"))
            {
                File.Delete(Application.StartupPath + @"\trace.log");
            }
            Trace.Listeners.Add(new TextWriterTraceListener(Application.StartupPath + @"\trace.log"));
            Trace.AutoFlush = true;

            try
            {
                Cfg.Load();
            }
            catch
            {
                return;
            }
            if (Cfg.source_selection ==0 & Cfg.cloud_name.Equals("demo"))
            {
                #if DEBUG

                #else
                DialogResult dr = MessageBox.Show("WARNING: it's recommended to get images from CDN. \n 注意：推荐使用CDN方式来抓取图片，以提高稳定性。", "EarthLiveSharp");
                if (dr == DialogResult.OK)
                {
                    Process.Start("https://github.com/bitdust/EarthLiveSharp/issues/32");
                }
                #endif
            }
            Cfg.image_folder = Application.StartupPath + @"\images";
            Cfg.Save();
            Scrap_wrapper.set_scraper();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new mainForm());
        }
    }

    public static class Scrap_wrapper
    {
        private static IScraper scraper;
        public static void set_scraper()
        {
            if (Cfg.source_selection == 2)
                scraper = new Scraper_bing();
            else
                scraper = new Scraper_himawari8();
        }
        public static void UpdateImage()
        {
            scraper.UpdateImage();
        }

        public static void ResetState()
        {
            scraper.ResetState();
        }

        public static void CleanCDN()
        {
            scraper.CleanCDN();
        }

        public static string LastUpdateStatus
        {
            get { return scraper.LastUpdateStatus; }
        }
    }

    interface IScraper
    {
        void UpdateImage();
        void ResetState();
        void CleanCDN();
        string LastUpdateStatus { get; }
    }
    public class Scraper_himawari8 : IScraper
    {
        private string imageID = "";
        private static string last_imageID = "0";
        public string lastUpdateStatus = "";
        public string LastUpdateStatus { get { return lastUpdateStatus; } }

        /// <summary>
        /// Timestamp API endpoints. NICT (official source, different network path)
        /// serves the same JSON format as the himawari.asia mirror.
        /// Note: proxying latest.json through Cloudinary is NOT possible without
        /// API keys — raw/fetch is rejected ("Invalid value fetch for parameter type")
        /// and image/fetch rejects JSON payloads ("Invalid image file").
        /// Both modes therefore use the same two-source direct cascade below.
        /// </summary>
        private const string LatestJsonUrl = "https://himawari.asia/img/D531106/latest.json";
        private const string NictLatestJsonUrl = "https://himawari8.nict.go.jp/img/D531106/latest.json";

        /// <summary>
        /// Fetch the latest image timestamp with source fallback.
        /// Returns format: "2026/06/26/062000", or null when all sources fail.
        ///
        /// Two rounds of [himawari.asia → NICT], with 2s/4s backoff between rounds.
        /// Each source gets at most 2 tries; worst case ~62s (4 × 15s timeout).
        /// </summary>
        private string GetLatestImageId()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            const int maxRounds = 2;
            for (int round = 1; round <= maxRounds; round++)
            {
                // Tier 1: himawari.asia mirror
                string id = TryFetchImageId(LatestJsonUrl, string.Format("direct r{0}", round));
                if (id != null) return id;

                // Tier 2: NICT official source (same JSON format, different route)
                id = TryFetchImageId(NictLatestJsonUrl, string.Format("nict r{0}", round));
                if (id != null) return id;

                if (round < maxRounds)
                {
                    int wait = (int)Math.Pow(2, round) * 1000; // 2s / 4s
                    Trace.WriteLine(string.Format("[api] both sources failed, retrying round {0} in {1}ms...", round + 1, wait));
                    System.Threading.Thread.Sleep(wait);
                }
            }
            Trace.WriteLine("[api] failed after " + maxRounds + " rounds (himawari.asia + NICT)");
            return null;
        }

        /// <summary>
        /// Single GET of a latest.json-style endpoint. Returns the parsed imageID
        /// ("2026/06/26/062000"), or null on any failure (network, non-200, bad payload).
        /// </summary>
        private string TryFetchImageId(string url, string label)
        {
            try
            {
                var request = WebRequest.Create(url) as HttpWebRequest;
                request.Timeout = 15000;
                request.ReadWriteTimeout = 15000;

                using (var response = request.GetResponse() as HttpWebResponse)
                {
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        Trace.WriteLine(string.Format("[api] {0}: HTTP {1}", label, (int)response.StatusCode));
                        return null;
                    }
                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        string id = ParseImageId(reader.ReadToEnd());
                        if (id == null)
                            Trace.WriteLine(string.Format("[api] {0}: unparseable response", label));
                        return id;
                    }
                }
            }
            catch (Exception e)
            {
                Trace.WriteLine(string.Format("[api] {0} failed: {1}", label, e.Message));
                return null;
            }
        }

        /// <summary>
        /// Parse {"date":"2026-06-26 06:20:00","file":"..."} into "2026/06/26/062000".
        /// Works on the raw byte stream regardless of Content-Type.
        /// Returns null if the payload doesn't contain a usable date field.
        /// </summary>
        private string ParseImageId(string json)
        {
            int dateStart = json.IndexOf("\"date\":\"") + 8;
            int dateEnd = json.IndexOf("\"", dateStart);
            if (dateStart < 8 || dateEnd < 0) return null;
            string dateStr = json.Substring(dateStart, dateEnd - dateStart);
            return dateStr.Replace("-", "/")
                          .Replace(":", "")
                          .Replace(" ", "/");
        }

        /// <summary>
        /// Build the origin Himawari tile URL.
        /// </summary>
        private string BuildOriginUrl(int ii, int jj)
        {
            return string.Format("https://himawari.asia/img/D531106/{0}d/550/{1}_{2}_{3}.png",
                Cfg.size, imageID, ii, jj);
        }

        private int SaveImage()
        {
            System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string image_path = "";
            bool isCdnMode = (Cfg.source_selection == 1);

            try
            {
                for (int ii = 0; ii < Cfg.size; ii++)
                {
                    for (int jj = 0; jj < Cfg.size; jj++)
                    {
                        string originUrl = BuildOriginUrl(ii, jj);
                        image_path = string.Format("{0}\\{1}_{2}.png", Cfg.image_folder, ii, jj);

                        if (isCdnMode)
                        {
                            // CDN mode: only Cloudinary fetch, no fallback to NICT
                            string fetchUrl = CloudinaryUpload.BuildFetchUrl(Cfg.cloud_name, originUrl);
                            Trace.WriteLine("[cdn] fetch URL: " + fetchUrl);

                            if (!CloudinaryUpload.DownloadFileWithRetry(fetchUrl, image_path))
                            {
                                Trace.WriteLine("[cdn] Cloudinary fetch failed: " + fetchUrl);
                                return -1;
                            }
                        }
                        else
                        {
                            // Origin mode: direct download with retry
                            if (!CloudinaryUpload.DownloadFileWithRetry(originUrl, image_path))
                            {
                                Trace.WriteLine("[origin] download failed: " + originUrl);
                                return -1;
                            }
                        }
                    }
                }
                Trace.WriteLine("[save image] " + imageID + " source=" + (isCdnMode ? "CDN(" + Cfg.cloud_name + ")" : "NICT"));
                return 0;
            }
            catch (Exception e)
            {
                Trace.WriteLine(e.Message + " " + imageID);
                Trace.WriteLine(string.Format("[url]{0} [image_path]{1}", BuildOriginUrl(0, 0), image_path));
                return -1;
            }
        }

        private void JoinImage()
        {
            // join & convert the images to wallpaper.bmp
            Bitmap bitmap = new Bitmap(550 * Cfg.size, 550 * Cfg.size);
            Image[,] tile = new Image[Cfg.size, Cfg.size];
            Graphics g = Graphics.FromImage(bitmap);
            for (int ii = 0; ii < Cfg.size; ii++)
            {
                for (int jj = 0; jj < Cfg.size; jj++)
                {
                    tile[ii,jj] = Image.FromFile(string.Format("{0}\\{1}_{2}.png", Cfg.image_folder, ii, jj));
                    g.DrawImage(tile[ii, jj], 550 * ii, 550 * jj);
                    tile[ii, jj].Dispose();
                }
            }
            g.Save();
            g.Dispose();
            if (Cfg.zoom == 100)
            {
                bitmap.Save(string.Format("{0}\\wallpaper.bmp", Cfg.image_folder),System.Drawing.Imaging.ImageFormat.Bmp);
            }
            else if (1 < Cfg.zoom & Cfg.zoom <100)
            {
                int new_size = bitmap.Height * Cfg.zoom /100;
                Bitmap zoom_bitmap = new Bitmap(new_size, new_size);
                Graphics g_2 = Graphics.FromImage(zoom_bitmap);
                g_2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g_2.DrawImage(bitmap, 0, 0, new_size, new_size);
                g_2.Save();
                g_2.Dispose();
                zoom_bitmap.Save(string.Format("{0}\\wallpaper.bmp", Cfg.image_folder),System.Drawing.Imaging.ImageFormat.Bmp);
                zoom_bitmap.Dispose();
            }
            else
            {
                Trace.WriteLine("[himawari8 zoom error]");
            }

            bitmap.Dispose();

            if (Cfg.saveTexture && Cfg.saveDirectory != "selected Directory")
            {
                try
                {
                    // 文件名使用卫星拍摄时间：imageID 形如 "2026-09-02/062000"，
                    // 由 ParseImageId 以字面量替换生成（与文化无关），斜杠换成短横线以符合文件名规范
                    string fileName = "wallpaper_" + imageID.Replace("/", "-") + ".bmp";
                    File.Copy(string.Format("{0}\\wallpaper.bmp", Cfg.image_folder), Cfg.saveDirectory + "\\" + fileName, true);
                }
                catch (Exception e)
                {
                    Trace.WriteLine("[can't save wallpaper to distDirectory]");
                    Trace.WriteLine(e.Message);
                    return;
                }
            }
        }

        private void InitFolder()
        {
            if(Directory.Exists(Cfg.image_folder))
            {
            }
            else
            {
                Trace.WriteLine("[himawari8 create folder]");
                Directory.CreateDirectory(Cfg.image_folder);
            }
        }
        public void UpdateImage()
        {
            InitFolder();

            // Fetch latest timestamp from himawari.asia API
            imageID = GetLatestImageId();

            if (imageID == null)
            {
                Trace.WriteLine("[update] API request failed, keeping current wallpaper");
                lastUpdateStatus = "api_failed";
                return;
            }

            if (imageID.Equals(last_imageID))
            {
                Trace.WriteLine("[update] same imageID, skipping: " + imageID);
                lastUpdateStatus = "same_image";
                return;
            }

            if (SaveImage() == 0)
            {
                JoinImage();
                lastUpdateStatus = "success";
                last_imageID = imageID; // 仅在成功后更新，避免瞬时失败导致下一轮误判"unchanged"而跳过此帧
            }
            else
            {
                lastUpdateStatus = "download_failed";
            }
        }

        public void ResetState()
        {
            last_imageID = "0";
        }

        /// <summary>
        /// Clean Cloudinary fetch cache to prevent storage bloat.
        /// Deletes all cached fetch resources with himawari.asia prefix.
        /// Requires api_key and api_secret in App.config.
        /// </summary>
        public void CleanCDN()
        {
            if (Cfg.source_selection != 1) return; // only in CDN mode
            if (string.IsNullOrEmpty(Cfg.api_key)) return;
            if (string.IsNullOrEmpty(Cfg.api_secret)) return;

            try
            {
                string url = "https://api.cloudinary.com/v1_1/" + Cfg.cloud_name
                           + "/resources/image/fetch?prefix=https://himawari.asia&max_results=500";
                HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
                request.Method = "DELETE";
                request.Timeout = 30000;
                request.ReadWriteTimeout = 30000;
                request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);

                string credentials = Convert.ToBase64String(
                    System.Text.Encoding.ASCII.GetBytes(Cfg.api_key + ":" + Cfg.api_secret));
                request.Headers.Add("Authorization", "Basic " + credentials);

                for (int i = 0; i < 3; i++) // max 3 batches per cleanup
                {
                    using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                    {
                        if (response.StatusCode != HttpStatusCode.OK)
                        {
                            Trace.WriteLine("[cleanCDN] unexpected status: " + (int)response.StatusCode);
                            return;
                        }
                        using (var reader = new StreamReader(response.GetResponseStream()))
                        {
                            string result = reader.ReadToEnd();
                            if (result.Contains("\"error\""))
                            {
                                Trace.WriteLine("[cleanCDN] API error: " + result);
                                return;
                            }
                            if (result.Contains("\"partial\":false"))
                            {
                                Trace.WriteLine("[cleanCDN] cache cleanup done");
                                return;
                            }
                            Trace.WriteLine("[cleanCDN] more resources to delete, batch " + (i + 1));
                        }
                    }
                }
                Trace.WriteLine("[cleanCDN] reached max batches (3), remaining resources will be cleaned next cycle");
            }
            catch (Exception e)
            {
                Trace.WriteLine("[cleanCDN] error: " + e.Message);
            }
        }
    }

    public static class Autostart
    {
        static string key = "EarthLiveSharp";
        public static bool Set(bool enabled)
        {
            RegistryKey runKey = null;
            try
            {
                string path = Application.ExecutablePath;
                runKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (enabled)
                {
                    runKey.SetValue(key, path);
                }
                else
                {
                    if (runKey.GetValue(key) != null)
                    {
                        runKey.DeleteValue(key);
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Trace.WriteLine(e.Message);
                return false;
            }
            finally
            {
                if(runKey!=null)
                {
                    runKey.Close();
                }
            }
        }
    }
}
