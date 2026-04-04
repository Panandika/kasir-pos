using System;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Auth;
using Kasir.Data;

namespace Kasir.Forms
{
    public class LoginForm : BaseForm
    {
        private TextBox txtUsername;
        private TextBox txtPassword;
        private Label lblMessage;
        private AuthService _auth;

        public LoginForm()
        {
            InitializeLayout();
            SetAction("Login — Enter username and password");
        }

        private void InitializeLayout()
        {
            var pnlCenter = new Panel
            {
                Size = new Size(400, 300),
                BackColor = Color.FromArgb(10, 10, 10)
            };

            var lblTitle = new Label
            {
                Text = "KASIR POS",
                Font = new Font("Consolas", 24f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 255, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 60
            };

            var lblUser = new Label
            {
                Text = "Username:",
                ForeColor = Color.Gray,
                Location = new Point(30, 80),
                AutoSize = true
            };

            txtUsername = new TextBox
            {
                Location = new Point(30, 105),
                Width = 340,
                BackColor = Color.FromArgb(20, 20, 20),
                ForeColor = Color.FromArgb(0, 255, 0),
                Font = new Font("Consolas", 16f),
                CharacterCasing = CharacterCasing.Upper
            };

            var lblPass = new Label
            {
                Text = "Password:",
                ForeColor = Color.Gray,
                Location = new Point(30, 145),
                AutoSize = true
            };

            txtPassword = new TextBox
            {
                Location = new Point(30, 170),
                Width = 340,
                BackColor = Color.FromArgb(20, 20, 20),
                ForeColor = Color.FromArgb(0, 255, 0),
                Font = new Font("Consolas", 16f),
                PasswordChar = '*',
                UseSystemPasswordChar = false
            };
            txtPassword.KeyDown += TxtPassword_KeyDown;

            lblMessage = new Label
            {
                Location = new Point(30, 210),
                Size = new Size(340, 40),
                ForeColor = Color.Red,
                Text = "",
                TextAlign = ContentAlignment.MiddleCenter
            };

            var btnLogin = new Button
            {
                Text = "LOGIN",
                Location = new Point(30, 255),
                Size = new Size(340, 35),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(0, 80, 0),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Consolas", 12f, FontStyle.Bold)
            };
            btnLogin.Click += BtnLogin_Click;

            pnlCenter.Controls.AddRange(new Control[] {
                lblTitle, lblUser, txtUsername, lblPass, txtPassword, lblMessage, btnLogin
            });

            // Center the panel
            this.Load += (s, e) =>
            {
                pnlCenter.Location = new Point(
                    (this.ClientSize.Width - pnlCenter.Width) / 2,
                    (this.ClientSize.Height - pnlCenter.Height) / 2);
            };

            this.Controls.Add(pnlCenter);
        }

        private void TxtPassword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                BtnLogin_Click(sender, e);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void BtnLogin_Click(object sender, EventArgs e)
        {
            if (_auth == null)
            {
                _auth = new AuthService(DbConnection.GetConnection());
            }

            var result = _auth.Login(txtUsername.Text, txtPassword.Text);

            if (result.Success)
            {
                lblMessage.Text = "";
                this.Hide();

                var mainMenu = new MainMenuForm(_auth);
                mainMenu.FormClosed += (s2, e2) =>
                {
                    _auth.Logout();
                    txtPassword.Text = "";
                    lblMessage.Text = "";
                    this.Show();
                    txtUsername.Focus();
                };
                mainMenu.Show();
            }
            else
            {
                lblMessage.Text = result.ErrorMessage;
                txtPassword.Text = "";
                txtPassword.Focus();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                if (MessageBox.Show("Exit Kasir POS?", "Confirm",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    Application.Exit();
                }
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
