using System;
using System.Drawing;
using System.Windows.Forms;

namespace Kasir.Forms
{
    public class BaseForm : Form
    {
        protected StatusStrip statusStrip;
        protected ToolStripStatusLabel lblAction;
        protected ToolStripStatusLabel lblClock;
        private System.Windows.Forms.Timer clockTimer;

        public BaseForm()
        {
            // Terminal theme
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.BackColor = Color.Black;
            this.ForeColor = Color.FromArgb(0, 255, 0);
            this.Font = new Font("Consolas", 14f);
            this.KeyPreview = true;
            this.StartPosition = FormStartPosition.CenterScreen;

            // Status bar
            statusStrip = new StatusStrip
            {
                BackColor = Color.FromArgb(0, 40, 0),
                SizingGrip = false
            };

            lblAction = new ToolStripStatusLabel
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.White,
                Text = ""
            };

            lblClock = new ToolStripStatusLabel
            {
                ForeColor = Color.White,
                Text = DateTime.Now.ToString("HH:mm:ss")
            };

            statusStrip.Items.AddRange(new ToolStripItem[] { lblAction, lblClock });
            this.Controls.Add(statusStrip);

            // Clock timer
            clockTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            clockTimer.Tick += ClockTimer_Tick;
            clockTimer.Start();
        }

        private void ClockTimer_Tick(object sender, EventArgs e)
        {
            lblClock.Text = DateTime.Now.ToString("HH:mm:ss");
        }

        protected void SetAction(string text)
        {
            lblAction.Text = text;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                this.Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (clockTimer != null)
            {
                clockTimer.Stop();
                clockTimer.Dispose();
            }
            base.OnFormClosed(e);
        }

        protected static DataGridViewCellStyle CreateGridStyle()
        {
            return new DataGridViewCellStyle
            {
                BackColor = Color.Black,
                ForeColor = Color.FromArgb(0, 255, 0),
                SelectionBackColor = Color.FromArgb(0, 80, 0),
                SelectionForeColor = Color.White,
                Font = new Font("Consolas", 11f)
            };
        }

        protected static DataGridViewCellStyle CreateGridHeaderStyle()
        {
            return new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(0, 40, 0),
                ForeColor = Color.White,
                Font = new Font("Consolas", 11f, FontStyle.Bold)
            };
        }

        protected static void ApplyGridTheme(DataGridView dgv)
        {
            dgv.BackgroundColor = Color.Black;
            dgv.GridColor = Color.FromArgb(0, 80, 0);
            dgv.DefaultCellStyle = CreateGridStyle();
            dgv.ColumnHeadersDefaultCellStyle = CreateGridHeaderStyle();
            dgv.EnableHeadersVisualStyles = false;
            dgv.RowHeadersVisible = false;
            dgv.BorderStyle = BorderStyle.None;
            dgv.AllowUserToAddRows = false;
            dgv.AllowUserToDeleteRows = false;
            dgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        }
    }
}
