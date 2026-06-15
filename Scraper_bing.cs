using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;

namespace EarthLiveSharp
{
    public class Scraper_bing : IScraper
    {
        private static string lastImageUrl = "";
        public string LastUpdateStatus { get; private set; } = "";

        public void UpdateImage()
        {
            InitFolder();

            try
            {
                string jsonUrl = "https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=1&mkt=en-US";
                string jsonResponse = DownloadText(jsonUrl);

                string imageUrl = ParseImageUrl(jsonResponse);
                if (string.IsNullOrEmpty(imageUrl))
                {
                    Trace.WriteLine("[bing] failed to parse image URL");
                    LastUpdateStatus = "download_failed";
                    return;
                }

                if (imageUrl == lastImageUrl)
                {
                    Trace.WriteLine("[bing] same image URL, skipping: " + imageUrl);
                    LastUpdateStatus = "same_image";
                    return;
                }

                string fullUrl = "https://www.bing.com" + imageUrl;
                string wallpaperPath = Cfg.image_folder + "\\wallpaper.bmp";

                using (var wc = new WebClient())
                {
                    wc.Headers.Add(HttpRequestHeader.UserAgent,
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                    byte[] data = wc.DownloadData(fullUrl);

                    using (var ms = new MemoryStream(data))
                    using (var img = Image.FromStream(ms))
                    {
                        img.Save(wallpaperPath, System.Drawing.Imaging.ImageFormat.Bmp);
                    }
                }

                Trace.WriteLine("[bing] saved wallpaper from: " + fullUrl);
                LastUpdateStatus = "success";
                lastImageUrl = imageUrl;
            }
            catch (Exception e)
            {
                Trace.WriteLine("[bing] error: " + e.Message);
                LastUpdateStatus = "download_failed";
            }
        }

        public void ResetState()
        {
            lastImageUrl = "";
            LastUpdateStatus = "";
        }

        private void InitFolder()
        {
            if (!Directory.Exists(Cfg.image_folder))
            {
                Directory.CreateDirectory(Cfg.image_folder);
            }
        }

        private string DownloadText(string url)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (var wc = new WebClient())
            {
                wc.Headers.Add(HttpRequestHeader.UserAgent,
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                return wc.DownloadString(url);
            }
        }

        private string ParseImageUrl(string json)
        {
            int urlStart = json.IndexOf("\"url\":\"");
            if (urlStart < 0) return "";
            urlStart += 7;
            int urlEnd = json.IndexOf("\"", urlStart);
            if (urlEnd < 0) return "";
            return json.Substring(urlStart, urlEnd - urlStart).Replace("&amp;", "&");
        }
    }
}
