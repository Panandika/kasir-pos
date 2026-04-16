using System;
using System.Configuration;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Kasir.Auth;
using Kasir.Data;
using Kasir.Utils;

namespace Kasir.Forms
{
    public class LoginForm : BaseForm
    {
        private TextBox txtUsername;
        private TextBox txtPassword;
        private Label lblMessage;
        private Label _lblCapsLock;
        private AuthService _auth;

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int keyCode);
        private const int VK_CAPITAL = 0x14;

        public LoginForm()
        {
            InitializeLayout();
            SetAction("Login — Enter username and password");
        }

        private void InitializeLayout()
        {
            string storeName = ConfigurationManager.AppSettings["StoreName"] ?? "TOKO SINAR MAKMUR";

            var pnlCenter = new Panel
            {
                Size = new Size(420, 380),
                BackColor = ThemeConstants.BgDialog,
                BorderStyle = BorderStyle.FixedSingle
            };

            // Store name
            var lblStore = new Label
            {
                Text = storeName,
                Font = ThemeConstants.FontMenu,
                ForeColor = ThemeConstants.FgMuted,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 30
            };

            var lblTitle = new Label
            {
                Text = "KASIR POS",
                Font = ThemeConstants.FontTitle,
                ForeColor = ThemeConstants.FgPrimary,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 50
            };

            // Version
            var lblVersion = new Label
            {
                Text = "v" + AppVersion.Current,
                Font = ThemeConstants.FontSmall,
                ForeColor = ThemeConstants.FgLabel,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 20
            };

            var lblUser = new Label
            {
                Text = "Username:",
                ForeColor = ThemeConstants.FgLabel,
                Location = new Point(30, 120),
                AutoSize = true
            };

            txtUsername = new TextBox
            {
                Location = new Point(30, 145),
                Width = 355,
                BackColor = ThemeConstants.BgInput,
                ForeColor = ThemeConstants.FgPrimary,
                Font = ThemeConstants.FontInput,
                CharacterCasing = CharacterCasing.Upper,
                TabIndex = 0
            };

            var lblPass = new Label
            {
                Text = "Password:",
                ForeColor = ThemeConstants.FgLabel,
                Location = new Point(30, 185),
                AutoSize = true
            };

            txtPassword = new TextBox
            {
                Location = new Point(30, 210),
                Width = 355,
                BackColor = ThemeConstants.BgInput,
                ForeColor = ThemeConstants.FgPrimary,
                Font = ThemeConstants.FontInput,
                PasswordChar = '*',
                UseSystemPasswordChar = false,
                TabIndex = 1
            };
            txtPassword.KeyDown += TxtPassword_KeyDown;

            // Caps Lock warning
            _lblCapsLock = new Label
            {
                Text = "CAPS LOCK AKTIF",
                Location = new Point(30, 245),
                AutoSize = true,
                ForeColor = ThemeConstants.FgWarning,
                Font = ThemeConstants.FontSmall,
                Visible = false
            };

            lblMessage = new Label
            {
                Location = new Point(30, 265),
                Size = new Size(355, 30),
                ForeColor = ThemeConstants.FgError,
                Text = "",
                TextAlign = ContentAlignment.MiddleCenter
            };

            var btnLogin = new Button
            {
                Text = "LOGIN",
                Location = new Point(30, 300),
                Size = new Size(355, 40),
                ForeColor = ThemeConstants.FgWhite,
                BackColor = ThemeConstants.BtnPrimary,
                FlatStyle = FlatStyle.Flat,
                Font = ThemeConstants.FontInputSmall,
                TabIndex = 2
            };
            btnLogin.Click += BtnLogin_Click;

            ApplyFocusIndicator(txtUsername);
            ApplyFocusIndicator(txtPassword);

            pnlCenter.Controls.AddRange(new Control[] {
                lblStore, lblTitle, lblVersion,
                lblUser, txtUsername, lblPass, txtPassword,
                _lblCapsLock, lblMessage, btnLogin
            });

            // Center the panel
            this.Load += (s, e) =>
            {
                pnlCenter.Location = new Point(
                    (this.ClientSize.Width - pnlCenter.Width) / 2,
                    (this.ClientSize.Height - pnlCenter.Height) / 2);
                UpdateCapsLockState();
            };

            // Monitor Caps Lock state
            txtPassword.GotFocus += (s, e) => UpdateCapsLockState();
            txtPassword.KeyUp += (s, e) => UpdateCapsLockState();
            txtUsername.KeyUp += (s, e) => UpdateCapsLockState();

            this.Controls.Add(pnlCenter);
        }

        private void UpdateCapsLockState()
        {
            bool capsOn = (GetKeyState(VK_CAPITAL) & 1) == 1;
            _lblCapsLock.Visible = capsOn;
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
