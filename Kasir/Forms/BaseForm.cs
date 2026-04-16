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
        private System.Windows.Forms.Timer _feedbackTimer;

        public BaseForm()
        {
            // Terminal theme
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.BackColor = ThemeConstants.BgPrimary;
            this.ForeColor = ThemeConstants.FgPrimary;
            this.Font = ThemeConstants.FontMain;
            this.KeyPreview = true;
            this.StartPosition = FormStartPosition.CenterScreen;

            // Status bar
            statusStrip = new StatusStrip
            {
                BackColor = ThemeConstants.BgHeader,
                SizingGrip = false
            };

            lblAction = new ToolStripStatusLabel
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = ThemeConstants.FgWhite,
                Text = ""
            };

            lblClock = new ToolStripStatusLabel
            {
                ForeColor = ThemeConstants.FgWhite,
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
            lblAction.ForeColor = ThemeConstants.FgWhite;
        }

        protected void ShowSuccess(string message)
        {
            lblAction.Text = message;
            lblAction.ForeColor = ThemeConstants.FgSuccess;
            StartFeedbackClear(3000);
        }

        protected void ShowError(string message)
        {
            lblAction.Text = message;
            lblAction.ForeColor = ThemeConstants.FgError;
        }

        protected void ShowWarning(string message)
        {
            lblAction.Text = message;
            lblAction.ForeColor = ThemeConstants.FgWarning;
        }

        protected void ShowBusy(string message)
        {
            lblAction.Text = message + "...";
            lblAction.ForeColor = ThemeConstants.FgWarning;
        }

        private void StartFeedbackClear(int delayMs)
        {
            if (_feedbackTimer == null)
            {
                _feedbackTimer = new System.Windows.Forms.Timer();
                _feedbackTimer.Tick += (s, e) =>
                {
                    _feedbackTimer.Stop();
                    lblAction.ForeColor = ThemeConstants.FgWhite;
                };
            }
            _feedbackTimer.Stop();
            _feedbackTimer.Interval = delayMs;
            _feedbackTimer.Start();
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
            if (_feedbackTimer != null)
            {
                _feedbackTimer.Stop();
                _feedbackTimer.Dispose();
            }
            base.OnFormClosed(e);
        }

        protected static DataGridViewCellStyle CreateGridStyle()
        {
            return new DataGridViewCellStyle
            {
                BackColor = ThemeConstants.BgPrimary,
                ForeColor = ThemeConstants.FgPrimary,
                SelectionBackColor = ThemeConstants.BgSelection,
                SelectionForeColor = ThemeConstants.FgWhite,
                Font = ThemeConstants.FontGrid
            };
        }

        protected static DataGridViewCellStyle CreateGridHeaderStyle()
        {
            return new DataGridViewCellStyle
            {
                BackColor = ThemeConstants.BgHeader,
                ForeColor = ThemeConstants.FgWhite,
                Font = ThemeConstants.FontGridHeader
            };
        }

        protected static void ApplyGridTheme(DataGridView dgv)
        {
            dgv.BackgroundColor = ThemeConstants.BgPrimary;
            dgv.GridColor = ThemeConstants.GridLine;
            dgv.DefaultCellStyle = CreateGridStyle();
            dgv.ColumnHeadersDefaultCellStyle = CreateGridHeaderStyle();
            dgv.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = ThemeConstants.BgGridAlt,
                ForeColor = ThemeConstants.FgPrimary,
                SelectionBackColor = ThemeConstants.BgSelection,
                SelectionForeColor = ThemeConstants.FgWhite,
                Font = ThemeConstants.FontGrid
            };
            dgv.EnableHeadersVisualStyles = false;
            dgv.RowHeadersVisible = false;
            dgv.BorderStyle = BorderStyle.None;
            dgv.AllowUserToAddRows = false;
            dgv.AllowUserToDeleteRows = false;
            dgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgv.RowTemplate.Height = ThemeConstants.RowHeight;
            dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        }

        protected static void ApplyFocusIndicator(TextBox txt)
        {
            txt.GotFocus += (s, e) =>
            {
                txt.BackColor = Color.FromArgb(30, 35, 30);
            };
            txt.LostFocus += (s, e) =>
            {
                txt.BackColor = ThemeConstants.BgInput;
            };
        }

        protected static void ApplyFocusIndicator(ComboBox cbo)
        {
            cbo.GotFocus += (s, e) =>
            {
                cbo.BackColor = Color.FromArgb(30, 35, 30);
            };
            cbo.LostFocus += (s, e) =>
            {
                cbo.BackColor = ThemeConstants.BgInput;
            };
        }
    }
}
