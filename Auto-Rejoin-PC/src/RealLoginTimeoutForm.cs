using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace NexusRobloxManager
{
    public sealed class RealLoginTimeoutForm : Form
    {
        private readonly Label status = new Label();
        private readonly Button extend = new Button();
        private readonly Button restore = new Button();
        private readonly Button shutdown = new Button();
        private bool busy;

        public RealLoginTimeoutForm()
        {
            Text = "เวลาล็อกอิน Roblox ใน Real (ทดลอง)";
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(640, 390);
            MinimumSize = new Size(610, 420);
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
            layout.RowCount = 5;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            Controls.Add(layout);
            Label description = new Label();
            description.Dock = DockStyle.Fill;
            description.Text = "เพิ่มเวลารอจาก 2 เป็น 15 นาทีในหน้าล็อกอินเดิมของ Real\nกดตั้งเวลาแล้ว Nexus จะปิด Real รวมโปรเซสค้างให้อัตโนมัติก่อนปรับค่า\nคุณยังกรอก CAPTCHA และรหัสยืนยัน 2 ชั้นตามปกติ";
            layout.Controls.Add(description, 0, 0);
            status.Dock = DockStyle.Fill;
            status.Padding = new Padding(10);
            status.BackColor = Color.FromArgb(24, 39, 57);
            status.ForeColor = Color.FromArgb(125, 211, 252);
            status.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(status, 0, 1);

            TableLayoutPanel actions = new TableLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.ColumnCount = 3;
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            extend.Text = "ตั้งเป็น 15 นาที";
            restore.Text = "คืนค่า 2 นาที";
            Button close = new Button();
            close.Text = "ปิดหน้านี้";
            close.Name = "DismissLoginTimeout";
            close.DialogResult = DialogResult.Cancel;
            foreach (Button button in new Button[] { extend, restore, close, shutdown })
            {
                button.Dock = DockStyle.Fill;
                button.FlatStyle = FlatStyle.Flat;
                button.BackColor = Color.FromArgb(51, 65, 85);
                button.ForeColor = Color.White;
                button.Margin = new Padding(3, 5, 3, 2);
            }
            extend.BackColor = Color.FromArgb(2, 132, 199);
            shutdown.Text = "ปิด Real ให้หมด (รวมโปรเซสค้าง)";
            shutdown.Name = "CloseAllRealButton";
            shutdown.BackColor = Color.FromArgb(146, 64, 14);
            shutdown.Click += delegate { RunOperation(0); };
            layout.Controls.Add(shutdown, 0, 2);
            extend.Click += delegate { Apply(15); };
            restore.Click += delegate { Apply(2); };
            actions.Controls.Add(extend, 0, 0);
            actions.Controls.Add(restore, 1, 0);
            actions.Controls.Add(close, 2, 0);
            layout.Controls.Add(actions, 0, 3);
            Label note = new Label();
            note.Dock = DockStyle.Fill;
            note.TextAlign = ContentAlignment.MiddleLeft;
            note.Font = new Font(Font.FontFamily, 8.5f);
            note.Text = "ถ้า Real ใช้สิทธิ์แอดมิน Windows จะขออนุญาตปิดโปรเซส\nหาก Windows Security บล็อกไฟล์ จะพักการปรับเวลาใน Real รุ่นนั้น";
            layout.Controls.Add(note, 0, 4);
            CancelButton = close;
            Shown += delegate { RefreshStatus(); };
        }

        private void RefreshStatus()
        {
            try
            {
                RealLoginTimeoutStatus current = RealLoginTimeout.InspectInstalled();
                status.Text = "Real " + current.Version + "\nเวลารอปัจจุบัน: " + current.Minutes + " นาที";
                if (current.SecurityBlocked)
                    status.Text += "\nWindows Security บล็อกไฟล์ Real: พักการปรับเวลาในรุ่นนี้\nตรวจรายละเอียดได้ที่ Windows Security → Protection history";
                extend.Enabled = !current.SecurityBlocked && current.Minutes != 15;
                restore.Enabled = !current.SecurityBlocked && current.Minutes != 2;
                extend.BackColor = current.SecurityBlocked ? Color.FromArgb(51, 65, 85) : Color.FromArgb(2, 132, 199);
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
                extend.Enabled = restore.Enabled = false;
            }
        }

        private void Apply(int minutes)
        {
            RunOperation(minutes);
        }

        private void RunOperation(int minutes)
        {
            if (busy) return;
            busy = true;
            extend.Enabled = restore.Enabled = shutdown.Enabled = false;
            ((Button)CancelButton).Enabled = false;
            status.Text = "กำลังปิด Real และตรวจโปรเซสค้าง...\nถ้า Windows ขอสิทธิ์แอดมิน กรุณายืนยันเพื่อดำเนินการต่อ";
            ThreadPool.QueueUserWorkItem(delegate
            {
                Exception failure = null;
                bool stopped = false;
                bool shutdownAttempted = false;
                string resultMessage = "";
                try
                {
                    if (minutes != 0) RealLoginTimeout.EnsureCanApplyInstalled();
                    shutdownAttempted = true;
                    RealProcessShutdownResult result = RealShutdownService.CloseWithElevation();
                    if (!result.Success) throw new InvalidOperationException(result.Message);
                    stopped = true;
                    resultMessage = result.Message;
                    if (minutes != 0) RealLoginTimeout.ApplyInstalled(minutes);
                }
                catch (Exception ex) { failure = ex; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (IsDisposed) return;
                        busy = false;
                        shutdown.Enabled = ((Button)CancelButton).Enabled = true;
                        RefreshStatus();
                        if (failure != null)
                        {
                            string title = failure is RealLoginSecurityBlockedException ? "Windows Security บล็อกการปรับเวลา" :
                                !shutdownAttempted ? "ยังปรับเวลาล็อกอินไม่ได้" :
                                stopped ? "ปิด Real ครบแล้ว แต่ยังเปลี่ยนเวลาไม่ได้" : "ยังปิด Real ไม่ครบ";
                            if (!(failure is RealLoginSecurityBlockedException)) status.Text = title + "\n" + failure.Message;
                            MessageBox.Show(this, title + "\n" + failure.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            status.Text += "\n" + resultMessage;
                            if (minutes != 0)
                                MessageBox.Show(this, "ปิด Real ครบแล้ว และตั้งเวลารอเป็น " + minutes + " นาทีแล้ว\nเปิด Real แล้วเพิ่มบัญชีได้ตามเดิม", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
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
