using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace EarthLiveSharp
{
    public static class CloudinaryUpload
    {
        private static readonly string UPLOAD_BASE = "https://api.cloudinary.com/v1_1";
        private static readonly string FETCH_BASE = "https://res.cloudinary.com";

        /// <summary>
        /// Build dynamic public_id from NICT image ID and size.
        /// imageID format: "2026/05/29/072000"
        /// Returns: "earthlivesharp/4x4/20260529_0720"
        /// Returns null if imageId format is invalid.
        /// </summary>
        public static string PublicIdFromImageId(string imageId, int size)
        {
            if (string.IsNullOrEmpty(imageId))
            {
                Trace.WriteLine("[upload_mode] PublicIdFromImageId: empty imageId");
                return null;
            }
            string[] parts = imageId.Split('/');
            if (parts.Length < 4)
            {
                Trace.WriteLine("[upload_mode] PublicIdFromImageId: invalid format: " + imageId);
                return null;
            }
            string timePart = parts[3].Trim();
            if (timePart.Length < 4)
            {
                Trace.WriteLine("[upload_mode] PublicIdFromImageId: time part too short: " + imageId);
                return null;
            }
            string timestamp = parts[0] + parts[1] + parts[2] + "_" + timePart.Substring(0, 4);
            return string.Format("earthlivesharp/{0}x{0}/{1}", size, timestamp);
        }

        public static string BuildCdnUrl(string publicId, string cloudName)
        {
            return string.Format("{0}/{1}/image/upload/{2}.png", FETCH_BASE, cloudName, publicId);
        }

        public static bool DownloadFromCloudinary(string publicId, string cloudName, string outputPath)
        {
            string url = BuildCdnUrl(publicId, cloudName);
            Trace.WriteLine("[upload_mode] downloading from Cloudinary: " + url);
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
                        Trace.WriteLine("[upload_mode] CDN download failed: HTTP " + response.StatusCode);
                        return false;
                    }
                    using (Stream stream = response.GetResponseStream())
                    using (FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                    {
                        stream.CopyTo(fs);
                    }
                }
                Trace.WriteLine("[upload_mode] download success: " + outputPath);
                return true;
            }
            catch (Exception e)
            {
                Trace.WriteLine("[upload_mode] download error: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// HTTP HEAD probe to check if a CDN resource exists.
        /// Returns true on 200, false on 404, throws on other errors.
        /// </summary>
        public static bool ProbeExists(string publicId, string cloudName)
        {
            string url = BuildCdnUrl(publicId, cloudName);
            HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
            request.Method = "HEAD";
            request.Timeout = 10000;
            try
            {
                using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                {
                    Trace.WriteLine("[upload_mode] HEAD probe 200: " + publicId);
                    return true;
                }
            }
            catch (WebException we)
            {
                if (we.Response is HttpWebResponse errorResponse)
                {
                    if (errorResponse.StatusCode == HttpStatusCode.NotFound)
                    {
                        Trace.WriteLine("[upload_mode] HEAD probe 404: " + publicId);
                        errorResponse.Close();
                        return false;
                    }
                    errorResponse.Close();
                }
                Trace.WriteLine("[upload_mode] HEAD probe error: " + we.Message);
                throw;
            }
        }

        /// <summary>
        /// Delete a resource from Cloudinary via Admin API (Basic Auth).
        /// Non-fatal — returns false on error, caller should log.
        /// </summary>
        public static bool DeleteResource(string publicId, string cloudName, string apiKey, string apiSecret)
        {
            if (string.IsNullOrEmpty(publicId) || string.IsNullOrEmpty(cloudName) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
            {
                Trace.WriteLine("[upload_mode] delete skipped: missing parameters");
                return false;
            }

            string deleteUrl = string.Format("https://api.cloudinary.com/v1_1/{0}/resources/image/upload/{1}", cloudName, publicId);
            HttpWebRequest request = WebRequest.Create(deleteUrl) as HttpWebRequest;
            request.Method = "DELETE";
            request.Timeout = 10000;
            request.ReadWriteTimeout = 10000;
            request.KeepAlive = false;
            request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string svcCredentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(apiKey + ":" + apiSecret));
            request.Headers.Add("Authorization", "Basic " + svcCredentials);
            try
            {
                using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                {
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        Trace.WriteLine("[upload_mode] deleted old resource: " + publicId);
                        return true;
                    }
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        Trace.WriteLine("[upload_mode] delete: resource already gone: " + publicId);
                        return true;
                    }
                    Trace.WriteLine("[upload_mode] delete returned HTTP " + (int)response.StatusCode + " for: " + publicId);
                    return false;
                }
            }
            catch (Exception e)
            {
                Trace.WriteLine("[upload_mode] delete error (non-fatal): " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Batch delete resources via Cloudinary Admin API.
        /// Returns (successCount, failCount).
        /// 404 treated as success (resource already gone).
        /// </summary>
        public static (int success, int failed) BatchDeleteResources(List<string> publicIds, string cloudName, string apiKey, string apiSecret)
        {
            if (publicIds == null || publicIds.Count == 0 || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
                return (0, 0);

            string deleteUrl = string.Format("https://api.cloudinary.com/v1_1/{0}/resources/image/upload/multiple?action=delete", cloudName);
            int success = 0;
            int failed = 0;

            // Process in batches of 50 (Cloudinary limit)
            for (int batchStart = 0; batchStart < publicIds.Count; batchStart += 50)
            {
                int count = Math.Min(50, publicIds.Count - batchStart);
                List<string> batch = publicIds.GetRange(batchStart, count);

                // Build POST body: public_ids[0]=id0&public_ids[1]=id1&...
                List<string> pairs = new List<string>();
                for (int i = 0; i < batch.Count; i++)
                {
                    pairs.Add(string.Format("public_ids[{0}]={1}", i, System.Net.WebUtility.UrlEncode(batch[i])));
                }
                string bodyData = string.Join("&", pairs);
                byte[] bodyBytes = Encoding.UTF8.GetBytes(bodyData);

                HttpWebRequest request = WebRequest.Create(deleteUrl) as HttpWebRequest;
                request.Method = "POST";
                request.ContentType = "application/x-www-form-urlencoded";
                request.Timeout = 30000;
                request.ReadWriteTimeout = 30000;
                request.KeepAlive = false;
                request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                string svcCredentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(apiKey + ":" + apiSecret));
                request.Headers.Add("Authorization", "Basic " + svcCredentials);
                request.ContentLength = bodyBytes.Length;

                try
                {
                    using (Stream requestStream = request.GetRequestStream())
                    {
                        requestStream.Write(bodyBytes, 0, bodyBytes.Length);
                    }
                    using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                    {
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            Trace.WriteLine(string.Format("[upload_mode] batch delete success: {0} items", count));
                            success += count;
                        }
                        else
                        {
                            Trace.WriteLine(string.Format("[upload_mode] batch delete HTTP {0}", (int)response.StatusCode));
                            failed += count;
                        }
                    }
                }
                catch (Exception e)
                {
                    Trace.WriteLine("[upload_mode] batch delete error: " + e.Message);
                    failed += count;
                }
            }
            return (success, failed);
        }

        public static bool UploadImage(string filePath, string publicId, string cloudName, string apiKey, string apiSecret)
        {
            string timestamp = ((long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds).ToString();
            string uploadUrl = string.Format("{0}/{1}/image/upload", UPLOAD_BASE, cloudName);
            string boundary = "----CloudinaryUpload" + timestamp;

            Dictionary<string, string> signParams = new Dictionary<string, string>
            {
                { "public_id", publicId },
                { "timestamp", timestamp },
                { "overwrite", "true" },
                { "unique_filename", "false" }
            };
            string signature = SignParams(signParams, apiSecret);

            HttpWebRequest request = WebRequest.Create(uploadUrl) as HttpWebRequest;
            request.Method = "POST";
            request.Timeout = 60000;
            request.ReadWriteTimeout = 60000;
            request.ContentType = "multipart/form-data; boundary=" + boundary;

            string uploadFilePath = filePath;
            bool converted = false;
            string ext = Path.GetExtension(filePath).ToLower();
            if (ext == ".bmp")
            {
                uploadFilePath = filePath + ".upload.png";
                try
                {
                    using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(filePath))
                    {
                        bmp.Save(uploadFilePath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    converted = true;
                }
                catch (Exception e)
                {
                    Trace.WriteLine("[upload_mode] BMP->PNG convert failed, trying raw upload: " + e.Message);
                    uploadFilePath = filePath;
                }
            }

            try
            {
                using (Stream requestStream = request.GetRequestStream())
                {
                    WriteFileField(requestStream, boundary, "file", uploadFilePath);
                    WriteFormField(requestStream, boundary, "public_id", publicId);
                    WriteFormField(requestStream, boundary, "overwrite", "true");
                    WriteFormField(requestStream, boundary, "unique_filename", "false");
                    WriteFormField(requestStream, boundary, "timestamp", timestamp);
                    WriteFormField(requestStream, boundary, "api_key", apiKey);
                    WriteFormField(requestStream, boundary, "signature", signature);
                    byte[] endBoundary = Encoding.UTF8.GetBytes("--" + boundary + "--\r\n");
                    requestStream.Write(endBoundary, 0, endBoundary.Length);
                }

                using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                {
                    int code = (int)response.StatusCode;
                    if (code >= 200 && code < 300)
                    {
                        Trace.WriteLine("[upload_mode] upload success: " + publicId);
                        return true;
                    }
                    else
                    {
                        using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                        {
                            string body = reader.ReadToEnd();
                            Trace.WriteLine("[upload_mode] upload failed: HTTP " + code + " body: " + body);
                        }
                        return false;
                    }
                }
            }
            catch (Exception e)
            {
                Trace.WriteLine("[upload_mode] upload error: " + e.Message);
                return false;
            }
            finally
            {
                if (converted && File.Exists(uploadFilePath))
                {
                    try { File.Delete(uploadFilePath); } catch { }
                }
            }
        }

        private static string SignParams(Dictionary<string, string> parameters, string apiSecret)
        {
            List<string> keys = new List<string>(parameters.Keys);
            keys.Sort();

            StringBuilder sb = new StringBuilder();
            foreach (string key in keys)
            {
                if (sb.Length > 0) sb.Append("&");
                sb.Append(key).Append("=").Append(parameters[key]);
            }
            string stringToSign = sb.ToString() + apiSecret;

            using (SHA1CryptoServiceProvider sha1 = new SHA1CryptoServiceProvider())
            {
                byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
                StringBuilder hex = new StringBuilder();
                foreach (byte b in hash)
                {
                    hex.Append(b.ToString("x2"));
                }
                return hex.ToString();
            }
        }

        private static void WriteFormField(Stream stream, string boundary, string name, string value)
        {
            string header = "--" + boundary + "\r\n"
                + "Content-Disposition: form-data; name=\"" + name + "\"\r\n\r\n";
            byte[] headerBytes = Encoding.UTF8.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
            byte[] valueBytes = Encoding.UTF8.GetBytes(value + "\r\n");
            stream.Write(valueBytes, 0, valueBytes.Length);
        }

        private static void WriteFileField(Stream stream, string boundary, string name, string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            string header = "--" + boundary + "\r\n"
                + "Content-Disposition: form-data; name=\"" + name + "\"; filename=\"" + fileName + "\"\r\n"
                + "Content-Type: image/bmp\r\n\r\n";
            byte[] headerBytes = Encoding.UTF8.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);

            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                byte[] buffer = new byte[8192];
                int len;
                while ((len = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    stream.Write(buffer, 0, len);
                }
            }
            byte[] crlf = Encoding.UTF8.GetBytes("\r\n");
            stream.Write(crlf, 0, crlf.Length);
        }
    }
}
