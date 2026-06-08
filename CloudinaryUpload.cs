using System;
using System.Diagnostics;
using System.IO;
using System.Net;

namespace EarthLiveSharp
{
    public static class CloudinaryUpload
    {
        /// <summary>
        /// Build a Cloudinary fetch URL that proxies a Himawari origin URL.
        /// Format: https://res.cloudinary.com/{cloud_name}/image/fetch/f_auto,q_auto/{origin_url}
        /// </summary>
        public static string BuildFetchUrl(string cloudName, string originUrl)
        {
            string encoded = System.Net.WebUtility.UrlEncode(originUrl);
            return string.Format("https://res.cloudinary.com/{0}/image/fetch/f_auto,q_auto/{1}", cloudName, encoded);
        }

        /// <summary>
        /// Download a file from a given URL (Cloudinary fetch or direct origin) to the output path.
        /// Returns true on success, false on failure.
        /// </summary>
        public static bool DownloadFile(string url, string outputPath)
        {
            Trace.WriteLine("[download] fetching: " + url);
            try
            {
                HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
                request.Method = "GET";
                request.Timeout = 15000;
                request.ReadWriteTimeout = 30000;

                using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                {
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        Trace.WriteLine("[download] failed: HTTP " + (int)response.StatusCode);
                        return false;
                    }
                    using (Stream stream = response.GetResponseStream())
                    using (FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                    {
                        stream.CopyTo(fs);
                    }
                }
                Trace.WriteLine("[download] success: " + outputPath);
                return true;
            }
            catch (Exception e)
            {
                Trace.WriteLine("[download] error: " + e.Message);
                return false;
            }
        }
    }
}
