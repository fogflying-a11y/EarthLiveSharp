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

        public static string PublicIdForSize(int size)
        {
            return "earthlivesharp/latest_" + size + "x" + size;
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
