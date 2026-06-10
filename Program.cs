using System;
using System.Windows.Forms;
using System.Net;
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
    }

    interface IScraper
    {
        void UpdateImage();
        void ResetState();
    }
    public class Scraper_himawari8 : IScraper
    {
        private string imageID = "";
        private static string last_imageID = "0";
        public string lastUpdateStatus = "";

        /// <summary>
        /// Calculate the quantized Himawari target timestamp.
        /// UTC - 1.5 hours, then floor to the nearest whole hour.
        /// Returns format: "2026/05/29/130000"
        /// </summary>
        private string GetQuantizedImageId()
        {
            DateTime utc = DateTime.UtcNow;
            DateTime delayed = utc.AddMinutes(-90);
            DateTime quantized = new DateTime(delayed.Year, delayed.Month, delayed.Day, delayed.Hour, 0, 0);
            return quantized.ToString("yyyy\\/MM\\/dd\\/HH0000");
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

                            if (!CloudinaryUpload.DownloadFile(fetchUrl, image_path))
                            {
                                Trace.WriteLine("[cdn] Cloudinary fetch failed: " + fetchUrl);
                                return -1;
                            }
                        }
                        else
                        {
                            // Origin mode: direct download from NICT
                            if (!CloudinaryUpload.DownloadFile(originUrl, image_path))
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

            // Calculate quantized image ID (does not request NICT)
            imageID = GetQuantizedImageId();

            if (imageID.Equals(last_imageID))
            {
                Trace.WriteLine("[update] same quantized imageID, skipping: " + imageID);
                lastUpdateStatus = "same_image";
                return;
            }

            if (SaveImage() == 0)
            {
                JoinImage();
                lastUpdateStatus = "success";
            }
            else
            {
                lastUpdateStatus = "download_failed";
            }
            last_imageID = imageID;
        }

        public void ResetState()
        {
            last_imageID = "0";
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
