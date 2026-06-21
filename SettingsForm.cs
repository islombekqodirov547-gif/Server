using System;
using System.Linq;
using System.Windows.Forms;

namespace StoreSystem.Api
{
    /// <summary>
    /// Server boshqaruv/sozlamalar oynasi.
    /// Tartib: port tanlanadi -> "Ishga tushirish" bosiladi -> port bo'sh bo'lsa
    /// server o'sha portda ishga tushadi va port saqlanadi. Band bo'lsa — tushunarli
    /// xabar, server YIQILMAYDI.
    /// </summary>
    public class SettingsForm : Form
    {
        private readonly ServerConfig _config;
        private readonly ServerHost _host;
        private readonly Action _onChanged;

        private readonly Label _status = new();
        private readonly ListBox _ips = new();
        private readonly NumericUpDown _port = new();
        private readonly TextBox _url = new();
        private readonly Button _startStop = new();
        private readonly Label _portBusyHint = new();

        public SettingsForm(ServerConfig config, ServerHost host, Action onChanged)
        {
            _config = config;
            _host = host;
            _onChanged = onChanged;

            Text = "JESKO Server — Boshqaruv";
            Width = 500;
            Height = 500;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            ShowInTaskbar = true;
            TopMost = true;

            _status.Left = 16; _status.Top = 12; _status.Width = 460; _status.Height = 26;
            _status.Font = new System.Drawing.Font("Segoe UI", 12, System.Drawing.FontStyle.Bold);

            var lblIp = new Label
            {
                Text = "Shu kompyuter (server) IP manzillari —\nilova va telefonga aynan shu manzilni yozing:",
                Left = 16, Top = 44, Width = 460, Height = 36
            };

            _ips.Left = 16; _ips.Top = 84; _ips.Width = 460; _ips.Height = 88;
            foreach (var ip in TrayAppContext.LocalIPv4()) _ips.Items.Add(ip);
            _ips.SelectedIndexChanged += (_, _) => UpdateUrl();

            var lblPort = new Label { Text = "Port:", Left = 16, Top = 188, Width = 50, Height = 24 };
            _port.Left = 70; _port.Top = 184; _port.Width = 90;
            _port.Minimum = 1; _port.Maximum = 65535;
            _port.Value = _host.IsRunning ? _host.RunningPort : _config.Port;
            _port.ValueChanged += (_, _) => UpdateUrl();

            _portBusyHint.Left = 170; _portBusyHint.Top = 188; _portBusyHint.Width = 306; _portBusyHint.Height = 24;
            _portBusyHint.Font = new System.Drawing.Font("Segoe UI", 9);

            var lblUrl = new Label
            {
                Text = "Ilovalarga yoziladigan to'liq manzil:",
                Left = 16, Top = 222, Width = 460, Height = 22
            };
            _url.Left = 16; _url.Top = 246; _url.Width = 460; _url.ReadOnly = true;
            _url.Font = new System.Drawing.Font("Consolas", 11);

            var btnCopy = new Button { Text = "Manzilni nusxalash", Left = 16, Top = 280, Width = 170, Height = 32 };
            btnCopy.Click += (_, _) => { try { Clipboard.SetText(_url.Text); } catch { } };

            var lblNote = new Label
            {
                Text = "Server ishga tushgach, ilova/telefonda yuqoridagi IP ni yozing.\n" +
                       "Odatda 5050 portni o'zgartirmaysiz.",
                Left = 16, Top = 322, Width = 460, Height = 40,
                ForeColor = System.Drawing.Color.DimGray
            };

            _startStop.Left = 16; _startStop.Top = 380; _startStop.Width = 220; _startStop.Height = 40;
            _startStop.Font = new System.Drawing.Font("Segoe UI", 11, System.Drawing.FontStyle.Bold);
            _startStop.Click += (_, _) => ToggleServer();

            var btnClose = new Button { Text = "Yopish", Left = 386, Top = 384, Width = 90, Height = 34 };
            btnClose.Click += (_, _) => Close();

            Controls.AddRange(new Control[]
            {
                _status, lblIp, _ips, lblPort, _port, _portBusyHint, lblUrl, _url, btnCopy, lblNote, _startStop, btnClose
            });

            Shown += (_, _) => TopMost = false;

            if (_ips.Items.Count > 0) _ips.SelectedIndex = 0; else UpdateUrl();
            RefreshState();
        }

        private void UpdateUrl()
        {
            var ip = _ips.SelectedItem?.ToString() ?? TrayAppContext.LocalIPv4().FirstOrDefault() ?? "localhost";
            _url.Text = $"http://{ip}:{(int)_port.Value}/";

            // To'xtatilgan holatda port bo'shligini ko'rsatib turamiz
            if (!_host.IsRunning)
            {
                bool free = ServerHost.IsPortFree((int)_port.Value);
                _portBusyHint.Text = free ? "✅ port bo'sh" : "⛔ port band — boshqasini tanlang";
                _portBusyHint.ForeColor = free
                    ? System.Drawing.Color.FromArgb(0x16, 0xA3, 0x4A)
                    : System.Drawing.Color.FromArgb(0xDC, 0x26, 0x26);
            }
            else _portBusyHint.Text = "";
        }

        private void RefreshState()
        {
            if (_host.IsRunning)
            {
                _status.Text = $"●  Server ISHLAMOQDA  (port {_host.RunningPort})";
                _status.ForeColor = System.Drawing.Color.FromArgb(0x16, 0xA3, 0x4A);
                _port.Value = _host.RunningPort;
                _port.Enabled = false;
                _startStop.Text = "■  Serverni to'xtatish";
            }
            else
            {
                _status.Text = "●  Server TO'XTATILGAN";
                _status.ForeColor = System.Drawing.Color.FromArgb(0xDC, 0x26, 0x26);
                _port.Enabled = true;
                _startStop.Text = "▶  Serverni ishga tushirish";
            }
            UpdateUrl();
            _onChanged?.Invoke();
        }

        private void ToggleServer()
        {
            if (_host.IsRunning)
            {
                _host.Stop();
                RefreshState();
                return;
            }

            int port = (int)_port.Value;

            // Saqlashdan/ishga tushirishdan OLDIN port bo'shligini tekshiramiz.
            if (!ServerHost.IsPortFree(port))
            {
                MessageBox.Show(
                    $"{port}-port band (boshqa dastur ishlatyapti).\n\n" +
                    "Iltimos boshqa port tanlang (masalan 5051) yoki o'sha portni band\n" +
                    "qilgan dasturni yoping, so'ng qaytadan urinib ko'ring.",
                    "JESKO Server", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                _host.Start(port);
                _config.Port = port;
                _config.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Serverni ishga tushirib bo'lmadi.\n\nTexnik xabar: " + ex.Message,
                    "JESKO Server", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            RefreshState();
            MessageBox.Show(
                "Server ishga tushdi! ✅\n\nIlova va telefonlarga shu manzilni yozing:\n" + _url.Text,
                "JESKO Server", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
