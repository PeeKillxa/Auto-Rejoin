using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Reflection;
using NexusRobloxManager;

static class UiLayoutChecks
{
    public static void Run()
    {
        Application.EnableVisualStyles();
        string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "checks", "previews");
        Directory.CreateDirectory(output);
        foreach (Size size in new Size[] { new Size(940, 680), new Size(1100, 830), new Size(1920, 1000) })
        {
            using (MainForm form = new MainForm(true))
            {
                form.Size = size;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-30000, -30000);
                form.Show();
                Application.DoEvents();
                form.PerformLayout();
                using (Bitmap debug = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(debug, new Rectangle(Point.Empty, debug.Size)); debug.Save(Path.Combine(output, "debug.png")); }
                CheckChildren(form);
                Control footer = form.Controls.Find("WindowActions", true)[0];
                Control list = form.Controls.Find("WindowsList", true)[0];
                if (footer.Bottom > footer.Parent.ClientSize.Height || footer.Top < list.Bottom || list.Height < 60)
                    throw new Exception("Footer/list clipped at " + size);
                if (footer.Controls.Count != 5) throw new Exception("Missing window actions");
                ListView grid=(ListView)list;
                if(grid.Columns.Count!=7)throw new Exception("Per-account Rejoin column is missing");
                AccountSlot account=WatchdogEngine.Accounts[0];
                var sample=new RobloxWin { Pid=777,Username=account.Username,UserId=account.UserId,AccountRef=account,BotStatus="FARMING" };
                typeof(MainForm).GetField("windows",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(form,new List<RobloxWin>{sample});
                var refresh=typeof(MainForm).GetMethod("UpdateListView",BindingFlags.NonPublic|BindingFlags.Instance);
                var toggle=typeof(MainForm).GetMethod("SetSelectedRejoinEnabled",BindingFlags.NonPublic|BindingFlags.Instance);
                refresh.Invoke(form,null);grid.Items[0].Selected=true;
                toggle.Invoke(form,new object[]{false});
                if(account.RejoinEnabled || !grid.Items[0].SubItems[6].Text.Contains("OFF"))throw new Exception("Per-account OFF control failed");
                toggle.Invoke(form,new object[]{true});
                if(!account.RejoinEnabled || !grid.Items[0].SubItems[6].Text.Contains("ON"))throw new Exception("Per-account ON control failed");
                foreach (Control button in footer.Controls)
                    if (!footer.ClientRectangle.Contains(button.Bounds) || button.Height < 32)
                        throw new Exception("Footer action clipped at " + size);
                using (Bitmap image = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    image.Save(Path.Combine(output, "layout-" + size.Width + "x" + size.Height + ".png"));
                }
                // Resize the same controls back down to catch stale anchor offsets.
                form.Size = new Size(940, 680);
                form.PerformLayout();
                CheckChildren(form);
                if (footer.Bottom > footer.Parent.ClientSize.Height || footer.Top < list.Bottom)
                    throw new Exception("Footer clipped after resize");
            }
            Console.WriteLine("PASS UI layout, footer actions and resize at " + size.Width + "x" + size.Height);
        }
    }

    private static void CheckChildren(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child.Width <= 0 || child.Height <= 0)
                throw new Exception("Empty control: " + child.GetType().Name + " / " + child.Text);
            if (parent is TableLayoutPanel && !parent.ClientRectangle.Contains(child.Bounds))
                throw new Exception("Table control out of bounds: " + child.GetType().Name + " / " + child.Text + " " + child.Bounds + " parent " + parent.Name + " " + parent.GetType().Name + " " + parent.ClientRectangle);
            CheckChildren(child);
        }
    }
}
