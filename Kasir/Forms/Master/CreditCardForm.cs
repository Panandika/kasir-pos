using System;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Forms.Shared;
using Kasir.Models;

namespace Kasir.Forms.Master
{
    public class CreditCardForm : BaseForm
    {
        private DataGridView dgvCards;
        private CreditCardRepository _cardRepo;
        private int _currentUserId;

        public CreditCardForm(int userId = 1)
        {
            _currentUserId = userId;
            _cardRepo = new CreditCardRepository(DbConnection.GetConnection());
            InitializeLayout();
            SetAction("Credit Card — Ins: Tambah, Enter: Ubah, Del: Hapus, Esc: Keluar");
            LoadData();
        }

        private void InitializeLayout()
        {
            dgvCards = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true };
            ApplyGridTheme(dgvCards);

            dgvCards.Columns.Add("Code", "Kode");
            dgvCards.Columns.Add("Name", "Nama Kartu");
            dgvCards.Columns.Add("Fee", "Fee %");
            dgvCards.Columns.Add("Account", "Akun");

            dgvCards.Columns["Code"].FillWeight = 100;
            dgvCards.Columns["Name"].FillWeight = 250;
            dgvCards.Columns["Fee"].FillWeight = 80;
            dgvCards.Columns["Account"].FillWeight = 150;

            this.Controls.Add(dgvCards);
        }

        private void LoadData()
        {
            dgvCards.Rows.Clear();
            var cards = _cardRepo.GetAll();
            foreach (var card in cards)
            {
                dgvCards.Rows.Add(
                    card.CardCode,
                    card.Name,
                    (card.FeePct / 100.0).ToString("F2") + "%",
                    card.AccountCode);
                dgvCards.Rows[dgvCards.Rows.Count - 1].Tag = card;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Insert:
                    AddCard();
                    return true;
                case Keys.Enter:
                    EditCard();
                    return true;
                case Keys.Delete:
                    DeleteCard();
                    return true;
                case Keys.Escape:
                    this.Close();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void AddCard()
        {
            using (var dlg = new InputDialog("Add Card Type",
                new[] { "Card Code", "Card Name", "Fee % (e.g. 2.50)", "Account Code" },
                new[] { "", "", "0", "" }))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    decimal fee;
                    if (!decimal.TryParse(dlg.Values[2], out fee)) fee = 0;

                    var card = new CreditCard
                    {
                        CardCode = dlg.Values[0],
                        Name = dlg.Values[1],
                        FeePct = (int)(fee * 100m),
                        AccountCode = dlg.Values[3],
                        ChangedBy = _currentUserId
                    };

                    _cardRepo.Insert(card);
                    LoadData();
                }
            }
        }

        private void EditCard()
        {
            if (dgvCards.CurrentRow == null) return;
            var card = dgvCards.CurrentRow.Tag as CreditCard;
            if (card == null) return;

            using (var dlg = new InputDialog("Edit Card Type",
                new[] { "Card Name", "Fee %", "Account Code" },
                new[] { card.Name, (card.FeePct / 100.0).ToString("F2"), card.AccountCode }))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    decimal fee;
                    if (!decimal.TryParse(dlg.Values[1], out fee)) fee = 0;

                    card.Name = dlg.Values[0];
                    card.FeePct = (int)(fee * 100m);
                    card.AccountCode = dlg.Values[2];
                    card.ChangedBy = _currentUserId;

                    _cardRepo.Update(card);
                    LoadData();
                }
            }
        }

        private void DeleteCard()
        {
            if (dgvCards.CurrentRow == null) return;
            var card = dgvCards.CurrentRow.Tag as CreditCard;
            if (card == null) return;

            if (MessageBox.Show(string.Format("Delete card {0}?", card.Name),
                "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                _cardRepo.Delete(card.Id);
                LoadData();
            }
        }
    }
}
