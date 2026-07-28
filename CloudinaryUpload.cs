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
        /// Plain fetch without transformations to minimize Credit consumption.
        /// Format: https://res.cloudinary.com/{cloud_name}/image/fetch/{origin_url}
        /// </summary>
        public static string BuildFetchUrl(string cloudName, string originUrl)
        {
            string encoded = originUrl.Replace(":", "%3A");
            return string.Format("https://res.cloudinary.com/{0}/image/fetch/{1}", cloudName, encoded);
        }

        /// <summary>
        /// 请求超时（毫秒）：连接+响应 与 流读写 均使用此值。
        /// </summary>
        private const int TimeoutMs = 30000;

        /// <summary>
        /// 单次下载（返回 bool），用于源站直连等不需重试的场景；失败时吞掉异常返回 false。
        /// </summary>
        public static bool DownloadFile(string url, string outputPath)
        {
            Trace.WriteLine("[download] fetching: " + url);
            try
            {
                DownloadFileCore(url, outputPath);
                Trace.WriteLine("[download] success: " + outputPath);
                return true;
            }
            catch (Exception e)
            {
                Trace.WriteLine("[download] error: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// CDN 下载（带重试）：瞬时失败（超时/5xx/连接错误）按指数退避 2s/4s/8s 最多重试 3 次；
        /// 非瞬时错误（4xx）或重试耗尽则返回 false。
        /// </summary>
        public static bool DownloadFileWithRetry(string url, string outputPath)
        {
            const int maxRetries = 3;
            Trace.WriteLine("[download] fetching (retry enabled): " + url);
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    DownloadFileCore(url, outputPath);
                    Trace.WriteLine("[download] success: " + outputPath);
                    return true;
                }
                catch (WebException e)
                {
                    bool transient = IsTransient(e);
                    if (!transient || attempt >= maxRetries)
                    {
                        Trace.WriteLine(string.Format("[download] failed: {0} ({1}), url={2}",
                            e.Message, transient ? "retries exhausted" : "non-transient, abort", url));
                        return false;
                    }
                    int wait = (int)Math.Pow(2, attempt + 1) * 1000; // 2000 / 4000 / 8000 ms
                    Trace.WriteLine(string.Format("[download] transient error: {0}, retry {1}/{2} in {3}ms, url={4}",
                        e.Message, attempt + 1, maxRetries, wait, url));
                    System.Threading.Thread.Sleep(wait);
                }
            }
        }

        /// <summary>
        /// 下载的真正实现：失败时抛出异常，以便上层（重试逻辑）区分错误类型。
        /// </summary>
        private static void DownloadFileCore(string url, string outputPath)
        {
            HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
            request.Method = "GET";
            request.Timeout = TimeoutMs;
            request.ReadWriteTimeout = TimeoutMs;

            using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
            {
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new WebException("HTTP " + (int)response.StatusCode);
                }
                using (Stream stream = response.GetResponseStream())
                using (FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fs);
                }
            }
        }

        /// <summary>
        /// 判断 WebException 是否为瞬时错误（值得重试）：超时/连接类错误，或 5xx 服务器错误。
        /// 4xx（如 400/401/403/404）视为非瞬时，不重试。
        /// </summary>
        private static bool IsTransient(WebException e)
        {
            switch (e.Status)
            {
                case WebExceptionStatus.Timeout:
                case WebExceptionStatus.ConnectFailure:
                case WebExceptionStatus.ConnectionClosed:
                case WebExceptionStatus.KeepAliveFailure:
                case WebExceptionStatus.SendFailure:
                case WebExceptionStatus.ReceiveFailure:
                    return true;
            }
            HttpWebResponse resp = e.Response as HttpWebResponse;
            if (resp != null && (int)resp.StatusCode >= 500) return true;
            return false;
        }
    }
}
