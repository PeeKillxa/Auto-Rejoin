using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NexusRobloxManager
{
    public sealed class RobloxAccountsForm : Form
    {
        private readonly Label status = new Label();
        private readonly Button login = new Button();
        private readonly Button shutdown = new Button();
        private readonly Button dismiss = new Button();
        private bool busy;

        public RobloxAccountsForm()
        {
            Text = "บัญชี Roblox / Real";
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(640, 310);
            MinimumSize = new Size(610, 340);
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(15, 23, 36);
            ForeColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Segoe UI", 10);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(18);
            layout.ColumnCount = 1;
            layout.RowCount = 4;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            Controls.Add(layout);

            Label description = new Label();
            description.Dock = DockStyle.Fill;
            description.Text = "ล็อกอิน Roblox ผ่าน Nexus รอได้ 15 นาที\nกรอก CAPTCHA และรหัส 2 ชั้นตามปกติ แล้วตรวจชื่อบัญชีก่อนนำเข้า Real";
            layout.Controls.Add(description, 0, 0);
            status.Dock = DockStyle.Fill;
            status.Padding = new Padding(10);
            status.BackColor = Color.FromArgb(24, 39, 57);
            status.ForeColor = Color.FromArgb(125, 211, 252);
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.Text = "เมื่อกดนำเข้าบัญชี Nexus จะปิด Real ชั่วคราวก่อนบันทึกบัญชี\nจากนั้นเปิด Real เพื่อดูบัญชีและเปิดเกมได้ตามเดิม";
            layout.Controls.Add(status, 0, 1);

            TableLayoutPanel actions = new TableLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.ColumnCount = 2;
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            login.Text = "เพิ่มบัญชี Roblox · รอ 15 นาที";
            login.Name = "OpenRobloxLoginButton";
            shutdown.Text = "ปิด Real ให้หมด";
            shutdown.Name = "CloseAllRealButton";
            login.Click += async delegate { await OpenLogin(); };
            shutdown.Click += delegate { CloseReal(); };
            Style(login, Color.FromArgb(2, 132, 199));
            Style(shutdown, Color.FromArgb(146, 64, 14));
            actions.Controls.Add(login, 0, 0);
            actions.Controls.Add(shutdown, 1, 0);
            layout.Controls.Add(actions, 0, 2);

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.ColumnCount = 2;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            LinkLabel legacy = new LinkLabel();
            legacy.Dock = DockStyle.Fill;
            legacy.Text = "ตัวปรับเวลาเดิมของ Real (ทดลอง)";
            legacy.LinkColor = Color.FromArgb(148, 163, 184);
            legacy.TextAlign = ContentAlignment.MiddleLeft;
            legacy.LinkClicked += delegate { using (RealLoginTimeoutForm dialog = new RealLoginTimeoutForm()) dialog.ShowDialog(this); };
            footer.Controls.Add(legacy, 0, 0);
            dismiss.Text = "ปิดหน้านี้";
            dismiss.DialogResult = DialogResult.Cancel;
            Style(dismiss, Color.FromArgb(51, 65, 85));
            footer.Controls.Add(dismiss, 1, 0);
            layout.Controls.Add(footer, 0, 3);
            CancelButton = dismiss;
        }

        private static void Style(Button button, Color color)
        {
            button.Dock = DockStyle.Fill;
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = color;
            button.ForeColor = Color.White;
            button.Margin = new Padding(3, 5, 3, 2);
        }

        private async Task OpenLogin()
        {
            if (busy) return;
            string previousStatus = status.Text;
            busy = true;
            login.Enabled = shutdown.Enabled = dismiss.Enabled = false;
            status.Text = "กำลังตรวจคลังบัญชี Real...";
            try
            {
                await Task.Run((Action)RealAccountImport.EnsureCanImport);
                if (IsDisposed) return;
                status.Text = previousStatus;
                WebViewRuntime.Register();
                using (RobloxLoginForm browser = new RobloxLoginForm(ImportAccount)) browser.ShowDialog(this);
            }
            catch (Exception error)
            {
                if (!IsDisposed) status.Text = "ยังเปิดล็อกอินเพื่อนำเข้าบัญชีไม่ได้\n" + error.Message;
            }
            finally
            {
                busy = false;
                if (!IsDisposed) login.Enabled = shutdown.Enabled = dismiss.Enabled = true;
            }
        }

        private static string ImportAccount(RobloxLoginAccount account)
        {
            RealAccountImport.EnsureCanImport();
            RobloxLoginAccount verified = RobloxLoginApi.AuthenticateCookie(account.Cookie);
            try
            {
                if (verified.UserId != account.UserId || !String.Equals(verified.Username, account.Username, StringComparison.Ordinal))
                    throw new InvalidOperationException("ข้อมูลบัญชีเปลี่ยนระหว่างตรวจสอบ กรุณาล็อกอินใหม่เพื่อตรวจชื่อบัญชีอีกครั้ง");
                RealProcessShutdownResult stopped = RealShutdownService.CloseWithElevation();
                if (!stopped.Success) throw new InvalidOperationException(stopped.Message);
                RealAccountImportResult result = RealAccountImport.Import(verified.Cookie, new RealAccountImportIdentity
                {
                    UserId = verified.UserId, Username = verified.Username,
                    DisplayName = verified.DisplayName, Created = verified.Created
                });
                return (result.UpdatedExisting ? "อัปเดตบัญชี @" : "เพิ่มบัญชี @") + result.Username +
                    " เข้า Real แล้ว\nเปิด Real เพื่อดูบัญชีและเปิดเกมได้ตามเดิม";
            }
            finally { verified.ForgetCookie(); }
        }

        private void CloseReal()
        {
            if (busy) return;
            busy = true;
            login.Enabled = shutdown.Enabled = dismiss.Enabled = false;
            status.Text = "กำลังปิด Real และตรวจโปรเซสค้าง...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string message;
                try { message = RealShutdownService.CloseWithElevation().Message; }
                catch (Exception ex) { message = ex.Message; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        busy = false;
                        if (IsDisposed) return;
                        login.Enabled = shutdown.Enabled = dismiss.Enabled = true;
                        status.Text = message;
                    });
                }
                catch (InvalidOperationException) { }
            });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
            base.OnFormClosing(e);
        }
    }
}
