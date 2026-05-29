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
            //if (Cfg.language.Equals("en")| Cfg.language.Equals("zh-Hans")| Cfg.language.Equals("zh-Hant"))
            //{
            //    System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(Cfg.language);
            //}
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
        public static int SequenceCount = 0;
        private static IScraper scraper;
        public static void set_scraper()
        {
            scraper = new Scraper_himawari8();
            return;
        }
        public static void UpdateImage()
        {
            scraper.UpdateImage();
        }

        public static void ResetState()
        {
            scraper.ResetState();
        }

        public static string LastUpdateStatus
        {
            get
            {
                Scraper_himawari8 s = scraper as Scraper_himawari8;
                return s != null ? s.lastUpdateStatus : "";
            }
        }

        public static void CleanCDN()
        {
            scraper.CleanCDN();
        }
    }

    interface IScraper
    {
        void UpdateImage();
        void CleanCDN();
        void ResetState();
    }
    public class Scraper_himawari8 : IScraper
    {
        private string imageID = "";
        private static string last_imageID = "0";
        private string json_url = "https://himawari8-dl.nict.go.jp/himawari8/img/FULL_24h/latest.json";
        private bool stopUpdates = false;
        private string previousPublicId = ""; // tracks last uploaded public_id for cleanup
        public string lastUpdateStatus = "";

        private int GetImageID()
        {
            System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            HttpWebRequest request = WebRequest.Create(json_url) as HttpWebRequest;
            try 
            {
                HttpWebResponse response = request.GetResponse() as HttpWebResponse;
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new Exception("[himawari8 connection error]");
                }
                if (!response.ContentType.Contains("application/json"))
                {
                    throw new Exception("[himawari8 no json recieved. your Internet connection is hijacked]");
                }
                StreamReader reader = new StreamReader(response.GetResponseStream());
                string date = reader.ReadToEnd();
                imageID = date.Substring(9,19).Replace("-", "/").Replace(" ", "/").Replace(":", "");
                Trace.WriteLine("[himawari8 get latest ImageID] " + imageID);
                reader.Close();
            }
            catch (Exception e)
            {
                Trace.WriteLine(e.Message);
                return -1;
            }
            return 0;
        }

        private int SaveImage()
        {
            System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            WebClient client = new WebClient();
            string image_source = "";
            if (Cfg.source_selection == 1)
            {
               image_source = "https://res.cloudinary.com/" + Cfg.cloud_name + "/image/fetch/https://himawari8-dl.nict.go.jp/himawari8/img/D531106";
            }
            else
            {
               image_source = "https://himawari8-dl.nict.go.jp/himawari8/img/D531106";
            }
            string url = "";
            string image_path = "";
            try
            {
                for (int ii = 0; ii < Cfg.size; ii++)
                {
                    for (int jj = 0; jj < Cfg.size; jj++)
                    {
                        url = string.Format("{0}/{1}d/550/{2}_{3}_{4}.png", image_source, Cfg.size, imageID, ii, jj);
                        image_path = string.Format("{0}\\{1}_{2}.png", Cfg.image_folder, ii, jj); // remove the '/' in imageID
                        client.DownloadFile(url, image_path);
                    }
                }
                Trace.WriteLine("[save image] " + imageID);
                return 0;
            }
            catch (Exception e)
            {
                Trace.WriteLine(e.Message + " " + imageID);
                Trace.WriteLine(string.Format("[url]{0} [image_path]{1}", url, image_path));
                return -1;
            }
            finally
            {
                client.Dispose();
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
                if (Scrap_wrapper.SequenceCount >= Cfg.saveMaxCount)
                {
                    Scrap_wrapper.SequenceCount = 0;
                }
                try
                {
                    File.Copy(string.Format("{0}\\wallpaper.bmp", Cfg.image_folder), Cfg.saveDirectory + "\\" + "wallpaper_" + Scrap_wrapper.SequenceCount + ".bmp", true);
                    Scrap_wrapper.SequenceCount++;
                }
                catch (Exception e)
                {
                    Trace.WriteLine("[can't save wallpaper to distDirectory]");
                    Trace.WriteLine(e.Message);
                    return;
                }
            }
        }

        private string GetCurrentPublicId()
        {
            return CloudinaryUpload.PublicIdFromImageId(imageID, Cfg.size);
        }

        private void InitFolder()
        {
            if(Directory.Exists(Cfg.image_folder))
            {
                // delete all images in the image folder.
                //string[] files = Directory.GetFiles(image_folder);
                //foreach (string fn in files)
                //{
                //    File.Delete(fn);
                //}
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

            if (stopUpdates) return;

            bool isCdnMode = (Cfg.source_selection == 1 && Cfg.upload_mode == 1);
            bool hasFullKeys = isCdnMode && Cfg.cloud_name.Length > 0 && Cfg.api_key.Length > 0 && Cfg.api_secret.Length > 0;

            if (isCdnMode && !hasFullKeys)
            {
                UpdateImage_CdnOnly();
            }
            else if (hasFullKeys)
            {
                UpdateImage_UploadMode();
            }
            else
            {
                if (GetImageID() == -1) return;
                if (imageID.Equals(last_imageID)) return;
                if (SaveImage() == 0) JoinImage();
            }
            last_imageID = imageID;
        }

        private void UpdateImage_UploadMode()
        {
            string wallpaperPath = string.Format("{0}\\wallpaper.bmp", Cfg.image_folder);

            // Step 1: Get latest image ID from NICT (confirms official time slot)
            if (GetImageID() == -1)
            {
                Trace.WriteLine("[upload_mode] NICT GetImageID failed");
                lastUpdateStatus = "all_sources_failed";
                return;
            }

            // Step 2: Calculate dynamic public_id from confirmed NICT timestamp
            string dynamicId = GetCurrentPublicId();
            if (dynamicId == null)
            {
                Trace.WriteLine("[upload_mode] invalid dynamic public_id");
                lastUpdateStatus = "all_sources_failed";
                return;
            }

            // Step 3: Probe CDN for this dynamic_id
            bool cdnHasResource;
            try
            {
                cdnHasResource = CloudinaryUpload.ProbeExists(dynamicId, Cfg.cloud_name);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[upload_mode] HEAD probe failed — stopping updates: " + ex.Message);
                stopUpdates = true;
                lastUpdateStatus = "upload_failed_stop";
                return;
            }

            if (cdnHasResource)
            {
                // CDN hit — download directly, skip NICT tiles
                try
                {
                    if (CloudinaryUpload.DownloadFromCloudinary(dynamicId, Cfg.cloud_name, wallpaperPath))
                    {
                        Trace.WriteLine("[upload_mode] served from CDN cache: " + dynamicId);
                        lastUpdateStatus = "cdn_hit";
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("[upload_mode] CDN download failed after HEAD hit: " + ex.Message);
                }
                lastUpdateStatus = "cdn_get_failed";
            }

            // CDN miss or GET failure — fetch from NICT source station
            if (!imageID.Equals(last_imageID))
            {
                int originalSource = Cfg.source_selection;
                Cfg.source_selection = 0;
                bool saveOk = (SaveImage() == 0);
                Cfg.source_selection = originalSource;
                if (saveOk) JoinImage();
                else
                {
                    Trace.WriteLine("[upload_mode] NICT tile download failed");
                    lastUpdateStatus = "all_sources_failed";
                    return;
                }
            }

            // Upload to Cloudinary
            try
            {
                bool ok = CloudinaryUpload.UploadImage(
                    wallpaperPath, dynamicId,
                    Cfg.cloud_name, Cfg.api_key, Cfg.api_secret);
                if (ok)
                {
                    Trace.WriteLine("[upload_mode] NICT refresh uploaded to Cloudinary: " + dynamicId);

                    // Delete previous time slot's resource (skip if same image re-uploaded)
                    if (!string.IsNullOrEmpty(previousPublicId) && previousPublicId != dynamicId)
                    {
                        CloudinaryUpload.DeleteResource(previousPublicId, Cfg.cloud_name, Cfg.api_key, Cfg.api_secret);
                    }
                    previousPublicId = dynamicId;

                    lastUpdateStatus = "nict_upload_ok";
                }
                else
                {
                    Trace.WriteLine("[upload_mode] upload to Cloudinary failed — stopping updates");
                    stopUpdates = true;
                    lastUpdateStatus = "upload_failed_stop";
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[upload_mode] upload exception — stopping updates: " + ex.Message);
                stopUpdates = true;
                lastUpdateStatus = "upload_failed_stop";
            }
        }

        private void UpdateImage_CdnOnly()
        {
            string wallpaperPath = string.Format("{0}\\wallpaper.bmp", Cfg.image_folder);

            // Step 1: Get latest image ID from NICT (confirms official time slot)
            if (GetImageID() == -1)
            {
                Trace.WriteLine("[cdn_only] NICT GetImageID failed");
                lastUpdateStatus = "all_sources_failed";
                return;
            }

            // Step 2: Calculate dynamic public_id from confirmed NICT timestamp
            string dynamicId = GetCurrentPublicId();
            if (dynamicId == null)
            {
                Trace.WriteLine("[cdn_only] invalid dynamic public_id");
                lastUpdateStatus = "all_sources_failed";
                return;
            }

            // Step 3: Download from CDN (probe + fetch in one request)
            try
            {
                if (CloudinaryUpload.DownloadFromCloudinary(dynamicId, Cfg.cloud_name, wallpaperPath))
                {
                    Trace.WriteLine("[cdn_only] served from CDN cache: " + dynamicId);
                    lastUpdateStatus = "cdn_hit";
                    return;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[cdn_only] CDN download failed: " + ex.Message);
            }

            // CDN has no resource for this time slot yet — skip and wait for next cycle
            lastUpdateStatus = "cdn_miss_skip";
        }
        public void CleanCDN()
        {
            Cfg.Load();
            if (Cfg.api_key.Length == 0) return;
            if (Cfg.api_secret.Length == 0) return;
            try
            {
                HttpWebRequest request = WebRequest.Create("https://api.cloudinary.com/v1_1/" + Cfg.cloud_name + "/resources/image/fetch?prefix=https://himawari8-dl") as HttpWebRequest;
                request.Method = "DELETE";
                request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
                string svcCredentials = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(Cfg.api_key + ":" + Cfg.api_secret));
                request.Headers.Add("Authorization", "Basic " + svcCredentials);
                HttpWebResponse response = null;
                StreamReader reader = null;
                string result = null;
                for (int i = 0; i < 3;i++ ) // max 3 request each hour.
                {
                    response = request.GetResponse() as HttpWebResponse;
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        throw new Exception("[himawari8 clean CND cache connection error]");
                    }
                    if (!response.ContentType.Contains("application/json"))
                    {
                        throw new Exception("[himawari8 clean CND cache no json recieved. your Internet connection is hijacked]");
                    }
                    reader = new StreamReader(response.GetResponseStream());
                    result = reader.ReadToEnd();
                    if (result.Contains("\"error\""))
                    {
                        throw new Exception("[himawari8 clean CND cache request error]\n" + result);
                    }
                    if (result.Contains("\"partial\":false"))
                    {
                        Trace.WriteLine("[himawari8 clean CDN cache done]");
                        break; // end of Clean CDN
                    }
                    else
                    {
                        Trace.WriteLine("[himawari8 more images to delete]");
                    }
                }
            }
            catch (Exception e)
            {
                Trace.WriteLine("[himawari8 error when delete CDN cache]");
                Trace.WriteLine(e.Message);
                return;
            }
        }
        public void ResetState()
        {
            last_imageID = "0";
            previousPublicId = "";
            stopUpdates = false;
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
                    runKey.SetValue(key, path); // dirty fix: to avoid exception in next line.
                    runKey.DeleteValue(key);
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
