using System;
using System.Drawing;
using System.Windows.Forms;

namespace NexusRobloxManager
{
    public sealed class RealLoginTimeoutForm : Form
    {
        private readonly Label status = new Label();
        private readonly Button extend = new Button();
        private readonly Button restore = new Button();

        public RealLoginTimeoutForm()
        {
            Text = "เวลาล็อกอิน Roblox ใน Real (ทดลอง)";
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(590, 280);
            MinimumSize = new Size(550, 310);
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
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            Controls.Add(layout);
            Label description = new Label();
            description.Dock = DockStyle.Fill;
            description.Text = "เพิ่มเวลารอจาก 2 เป็น 15 นาทีในหน้าล็อกอินเดิมของ Real\nคุณยังกรอก CAPTCHA และรหัสยืนยัน 2 ชั้นตามปกติ";
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
            close.Text = "ปิด";
            close.DialogResult = DialogResult.Cancel;
            foreach (Button button in new Button[] { extend, restore, close })
            {
                button.Dock = DockStyle.Fill;
                button.FlatStyle = FlatStyle.Flat;
                button.BackColor = Color.FromArgb(51, 65, 85);
                button.ForeColor = Color.White;
                button.Margin = new Padding(3, 5, 3, 2);
            }
            extend.BackColor = Color.FromArgb(2, 132, 199);
            extend.Click += delegate { Apply(15); };
            restore.Click += delegate { Apply(2); };
            actions.Controls.Add(extend, 0, 0);
            actions.Controls.Add(restore, 1, 0);
            actions.Controls.Add(close, 2, 0);
            layout.Controls.Add(actions, 0, 2);
            Label note = new Label();
            note.Dock = DockStyle.Fill;
            note.TextAlign = ContentAlignment.MiddleLeft;
            note.Font = new Font(Font.FontFamily, 8.5f);
            note.Text = "ปิด Real ก่อนเปลี่ยนเวลา • ตรวจรุ่นไฟล์ก่อนใช้\nยังไม่ยืนยันการล็อกอินจริง: Windows Security บล็อกสำเนา Real ในการทดสอบ";
            layout.Controls.Add(note, 0, 3);
            CancelButton = close;
            Shown += delegate { RefreshStatus(); };
        }

        private void RefreshStatus()
        {
            try
            {
                RealLoginTimeoutStatus current = RealLoginTimeout.InspectInstalled();
                status.Text = "Real " + current.Version + "\nเวลารอปัจจุบัน: " + current.Minutes + " นาที";
                extend.Enabled = current.Minutes != 15;
                restore.Enabled = current.Minutes != 2;
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
                extend.Enabled = restore.Enabled = false;
            }
        }

        private void Apply(int minutes)
        {
            try
            {
                RealLoginTimeout.ApplyInstalled(minutes);
                RefreshStatus();
                MessageBox.Show(this, "ตั้งเวลารอเป็น " + minutes + " นาทีแล้ว\nเปิด Real แล้วเพิ่มบัญชีได้ตามเดิม", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "ยังเปลี่ยนเวลาไม่ได้\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshStatus();
            }
        }
    }
}
