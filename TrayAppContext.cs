using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Forms;

namespace StoreSystem.Api
{
    /// <summary>
    /// Soat yonidagi (system tray) ikonka + boshqaruv oynasi.
    /// Server qo'lda ochilganda boshqaruv oynasi ko'rsatiladi. Ikkinchi nusxa
    /// ochilganda (signal fayl orqali) ham shu oyna oldinga chiqariladi —
    /// shu sabab foydalanuvchi yashiringan ikonkani qidirib qolmaydi.
    /// </summary>
    public class TrayAppContext : ApplicationContext
    {
        private readonly NotifyIcon _icon;
        private readonly ServerConfig _config;
        private readonly ServerHost _host;
        private readonly System.Windows.Forms.Timer _showWatcher;
        private SettingsForm? _settingsForm;

        public TrayAppContext(ServerConfig config, ServerHost host, bool showWindowOnStart)
        {
            _config = config;
            _host = host;

            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem("JESKO Server") { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Boshqaruv oynasini ochish", null, (_, _) => OpenWindow());
            menu.Items.Add("Server manzilini nusxalash", null, (_, _) => CopyUrl());
            menu.Items.Add("Brauzerda ochish (test)", null, (_, _) => OpenBrowser());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Chiqish (serverni to'xtatish)", null, (_, _) => ExitApp());

            Icon trayIcon;
            try { trayIcon = BuildJIcon(); }
            catch { trayIcon = SystemIcons.Application; }

            _icon = new NotifyIcon
            {
                Icon = trayIcon,
                ContextMenuStrip = menu,
                Text = TrimTip("JESKO Server")
            };
            _icon.DoubleClick += (_, _) => OpenWindow();
            _icon.Visible = true;

            RefreshTrayText();

            // Ikkinchi nusxa "oynani ko'rsat" signali (fayl) ni kuzatamiz.
            _showWatcher = new System.Windows.Forms.Timer { Interval = 800 };
            _showWatcher.Tick += (_, _) =>
            {
                try
                {
                    if (File.Exists(ServerConfig.ShowFlagPath))
                    {
                        File.Delete(ServerConfig.ShowFlagPath);
                        OpenWindow();
                    }
                }
                catch { }
            };
            _showWatcher.Start();

            if (showWindowOnStart)
                OpenWindow();
            else
                try
                {
                    _icon.ShowBalloonTip(4000, "JESKO Server",
                        _host.IsRunning ? "Server ishlayapti:\n" + PrimaryUrl()
                                        : "Server tray'da. Sozlash uchun ikonkani bosing.",
                        ToolTipIcon.Info);
                }
                catch { }
        }

        // ── Tarmoq ──

        public static List<string> LocalIPv4()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            var ip = ua.Address.ToString();
                            if (!ip.StartsWith("169.254") && !list.Contains(ip))
                                list.Add(ip);
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        private string PrimaryUrl()
        {
            int port = _host.IsRunning ? _host.RunningPort : _config.Port;
            var ip = LocalIPv4().FirstOrDefault() ?? "localhost";
            return $"http://{ip}:{port}/";
        }

        public void RefreshTrayText()
        {
            _icon.Text = TrimTip(_host.IsRunning
                ? "JESKO Server — ishlamoqda\n" + PrimaryUrl()
                : "JESKO Server — to'xtatilgan");
        }

        // ── Oyna / menyu amallari ──

        private void OpenWindow()
        {
            if (_settingsForm == null || _settingsForm.IsDisposed)
            {
                _settingsForm = new SettingsForm(_config, _host, RefreshTrayText);
                _settingsForm.FormClosed += (_, _) => { _settingsForm = null; RefreshTrayText(); };
                _settingsForm.Show();
            }
            _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.Show();
            _settingsForm.Activate();
            _settingsForm.BringToFront();
        }

        private void CopyUrl()
        {
            try { Clipboard.SetText(PrimaryUrl()); } catch { }
            _icon.ShowBalloonTip(2500, "Nusxalandi", "Manzil nusxalandi:\n" + PrimaryUrl(), ToolTipIcon.Info);
        }

        private void OpenBrowser()
        {
            if (!_host.IsRunning)
            {
                MessageBox.Show("Avval serverni ishga tushiring (boshqaruv oynasidan).",
                    "JESKO Server", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = $"http://localhost:{_host.RunningPort}/swagger",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void ExitApp()
        {
            var r = MessageBox.Show(
                "Serverni to'xtatib chiqasizmi?\n\nDiqqat: server to'xtasa kassir, admin, buxgalter va\n" +
                "sotuvchi ilovalari server bilan ishlay olmaydi.",
                "JESKO Server", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;

            _showWatcher.Stop();
            _icon.Visible = false;
            ExitThread();
        }

        private static Icon BuildJIcon()
        {
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var bg = new SolidBrush(Color.FromArgb(11, 17, 32));
                g.FillRectangle(bg, 0, 0, 32, 32);
                using var gold = new SolidBrush(Color.FromArgb(245, 158, 11));
                using var font = new Font("Segoe UI", 20, FontStyle.Bold, GraphicsUnit.Pixel);
                using var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString("J", font, gold, new RectangleF(0, 0, 32, 32), sf);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        private static string TrimTip(string s) => s.Length <= 63 ? s : s.Substring(0, 63);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _showWatcher?.Dispose();
                _icon?.Dispose();
                _settingsForm?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
