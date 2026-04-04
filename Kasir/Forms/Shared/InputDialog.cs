using System.Drawing;
using System.Windows.Forms;

namespace Kasir.Forms.Shared
{
    public class InputDialog : Form
    {
        private TextBox[] _textBoxes;

        public string[] Values { get; private set; }

        public InputDialog(string title, string[] labels, string[] defaults)
        {
            this.Text = title;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(20, 20, 20);
            this.ForeColor = Color.FromArgb(0, 255, 0);
            this.Font = new Font("Consolas", 11f);

            int fieldCount = labels.Length;
            this.Size = new Size(450, 80 + (fieldCount * 55) + 50);

            _textBoxes = new TextBox[fieldCount];
            Values = new string[fieldCount];

            for (int i = 0; i < fieldCount; i++)
            {
                var lbl = new Label
                {
                    Text = labels[i] + ":",
                    Location = new Point(15, 15 + (i * 55)),
                    AutoSize = true,
                    ForeColor = Color.Gray
                };
                this.Controls.Add(lbl);

                _textBoxes[i] = new TextBox
                {
                    Location = new Point(15, 35 + (i * 55)),
                    Width = 400,
                    BackColor = Color.FromArgb(30, 30, 30),
                    ForeColor = Color.FromArgb(0, 255, 0),
                    Font = new Font("Consolas", 12f)
                };

                if (defaults != null && i < defaults.Length && defaults[i] != null)
                {
                    _textBoxes[i].Text = defaults[i];
                }

                this.Controls.Add(_textBoxes[i]);
            }

            int btnY = 15 + (fieldCount * 55) + 10;

            var btnOk = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(255, btnY),
                Size = new Size(75, 30),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(0, 80, 0),
                FlatStyle = FlatStyle.Flat
            };

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(340, btnY),
                Size = new Size(75, 30),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(80, 0, 0),
                FlatStyle = FlatStyle.Flat
            };

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;
            this.Controls.AddRange(new Control[] { btnOk, btnCancel });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (this.DialogResult == DialogResult.OK)
            {
                Values = new string[_textBoxes.Length];
                for (int i = 0; i < _textBoxes.Length; i++)
                {
                    Values[i] = _textBoxes[i].Text.Trim();
                }
            }
            base.OnFormClosing(e);
        }

        public static string ShowSingleInput(IWin32Window owner, string title, string label, string defaultValue)
        {
            using (var dlg = new InputDialog(title, new[] { label }, new[] { defaultValue }))
            {
                if (dlg.ShowDialog(owner) == DialogResult.OK)
                {
                    return dlg.Values[0];
                }
                return null;
            }
        }
    }
}
