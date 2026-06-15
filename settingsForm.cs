using System;
using System.Windows.Forms;

namespace EarthLiveSharp
{
    public partial class settingsForm : Form
    {
        public settingsForm()
        {
            InitializeComponent();
            Cfg.Load();
        }

        private void save_config()
        {
            Cfg.satellite = satellite.Text;
            Cfg.interval = (int)(interval.Value);
            Cfg.zoom = (int)(image_zoom.Value);
            Cfg.autostart = autostart.Checked;
            Cfg.setwallpaper = setwallpaper.Checked;
            Cfg.saveTexture = Save_Texture.Checked;
            Cfg.saveMaxCount = (int)Save_Max_Count.Value;
            Cfg.saveDirectory = Directory_Display.Text;

            if (radioButton_CDN.Checked)
            {
                Cfg.source_selection = 1;
                Cfg.cloud_name = cloud_name.Text;
            }
            else if (radioButton_Bing.Checked)
            {
                Cfg.source_selection = 2;
            }
            else
            {
                Cfg.source_selection = 0;
            }
            switch (image_size.SelectedIndex)
            {
                case 0: Cfg.size = 1; break;
                case 1: Cfg.size = 2; break;
                case 2: Cfg.size = 4; break;
                case 3: Cfg.size = 8; break;
                case 4: Cfg.size = 16; break;
                default: Cfg.size = 1; break;
            }
            Cfg.Save();
            Autostart.Set(Cfg.autostart);
        }

        private void button3_Click(object sender, EventArgs e)
        {
            save_config();
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/bitdust/EarthLiveSharp/issues/32");
        }

        private void settingsForm_Load(object sender, EventArgs e)
        {
            Cfg.Load();
            version_number.Text = Cfg.version;
            cloud_name.Text = Cfg.cloud_name;
            autostart.Checked = Cfg.autostart;
            setwallpaper.Checked = Cfg.setwallpaper;
            interval.Value = Cfg.interval;
            image_zoom.Value = Cfg.zoom;
            Save_Texture.Checked = Cfg.saveTexture;
            Save_Max_Count.Value = Cfg.saveMaxCount;
            Directory_Display.Text = Cfg.saveDirectory;

            switch (Cfg.source_selection)
            {
                case 1: radioButton_CDN.Checked = true; break;
                case 2: radioButton_Bing.Checked = true; break;
                default: radioButton_Orgin.Checked = true; break;
            }

            if (Cfg.saveTexture)
            {
                panel3.Enabled = true;
            }
            else
            {
                panel3.Enabled = false;
            }

            bool isBing = (Cfg.source_selection == 2);
            satellite.Enabled = !isBing;
            image_size.Enabled = !isBing && (Cfg.satellite == "Himawari8");
            label5.Enabled = !isBing;
            label6.Enabled = !isBing;
            image_zoom.Enabled = !isBing;
            panel2.Enabled = (Cfg.source_selection == 1);

            switch (Cfg.satellite)
            {
                case "Himawari8": satellite.SelectedIndex = 0; break;
                case "FengYun4": satellite.SelectedIndex = 1; image_size.Enabled = false; break;
                default: satellite.SelectedIndex = 0; break;
            }

            switch (Cfg.size)
            {
                case 1: image_size.SelectedIndex = 0; break;
                case 2: image_size.SelectedIndex = 1; break;
                case 4: image_size.SelectedIndex = 2; break;
                case 8: image_size.SelectedIndex = 3; break;
                case 16: image_size.SelectedIndex = 4; break;
                default: image_size.SelectedIndex = 0; break;
            }

        }

        private void button1_Click(object sender, EventArgs e)
        {
            save_config();
            this.Close();
        }

        private void radioButton_Orgin_CheckedChanged(object sender, EventArgs e)
        {
            if (radioButton_CDN.Checked)
            {
                panel2.Enabled = true;
                satellite.Enabled = true;
                label5.Enabled = true;
                label6.Enabled = true;
                image_zoom.Enabled = true;
                image_size.Enabled = (satellite.SelectedIndex == 0);
            }
            else if (radioButton_Bing.Checked)
            {
                panel2.Enabled = false;
                satellite.Enabled = false;
                label5.Enabled = false;
                label6.Enabled = false;
                image_zoom.Enabled = false;
                image_size.Enabled = false;
            }
            else
            {
                panel2.Enabled = false;
                satellite.Enabled = true;
                label5.Enabled = true;
                label6.Enabled = true;
                image_zoom.Enabled = true;
                image_size.Enabled = (satellite.SelectedIndex == 0);
            }
        }

        private void satellite_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (radioButton_Bing.Checked) return;

            switch (satellite.Text)
            {
                case "Himawari8":
                    image_size.Enabled = true;
                    break;
                case "FengYun4":
                    image_size.Enabled = false;
                    break;
            }
        }

        private void Selected_Directory_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.Description = "selected Directory";
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                Directory_Display.Text = dialog.SelectedPath;
            }
        }

        private void Save_Texture_CheckedChanged(object sender, EventArgs e)
        {
            if (Save_Texture.Checked)
            {
                panel3.Enabled = true;
            }
            else
            {
                panel3.Enabled = false;
            }
        }
    }
}
