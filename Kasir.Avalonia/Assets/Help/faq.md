---
tags: [bantuan, faq, kasir]
---

# Bantuan Kasir — FAQ

## Cara terapkan diskon item

Pilih baris item di tabel Penjualan, lalu tekan **F8** untuk siklus diskon
0% → 5% → 10%. Untuk diskon nominal manual, edit langsung kolom **Harga**
pada baris item tersebut.

Diskon transaksi (untuk semua item dalam nota) diterapkan di panel
Rangkuman di sebelah kanan, kolom **Diskon Total**.

## Cara void item

Pilih baris item yang ingin dibatalkan, tekan **F4** untuk menghapus baris.
Konfirmasi dengan menekan **Enter** pada dialog. Void hanya bisa dilakukan
sebelum nota dibayar (status nota masih DRAFT).

Jika nota sudah dibayar, gunakan **Retur** dari menu Penjualan untuk
mencatat pengembalian barang.

## Cara reprint struk

Buka menu **Riwayat Penjualan** dari menu Penjualan, cari nota berdasarkan
nomor INV atau tanggal, pilih nota, lalu tekan **F12** untuk cetak ulang.
Struk akan dicetak ke printer default register ini.

## Printer macet atau tidak mencetak

1. Periksa lampu indikator printer: hijau = siap, merah = error.
2. Pastikan kabel USB/serial terpasang dan printer menyala.
3. Cek kertas thermal masih ada dan dipasang ke arah benar.
4. Buka **Admin → Konfigurasi Printer**, klik **Tes Cetak**.
5. Jika tetap gagal, lapor ke IT lewat tombol Bantuan (`Ctrl+/`).

## Scanner barcode tidak terbaca

Scanner barcode bekerja seperti keyboard — pastikan fokus berada di kolom
**Pindai Barcode** sebelum scan. Tekan `F2` untuk pindahkan fokus ke kolom
tersebut dengan cepat.

Jika scanner tidak merespon sama sekali, cabut dan pasang ulang kabel USB,
lalu coba scan barcode panjang seperti EAN-13 (13 digit) untuk tes.

## Laci kasir tidak terbuka

Laci kasir terhubung ke printer. Buka otomatis saat **Pembayaran Tunai**
selesai. Jika perlu buka manual, gunakan menu **Admin → Buka Laci**
(memerlukan PIN supervisor).

Kalau printer baik-baik saja tapi laci tidak buka, periksa kabel RJ-11
yang menghubungkan laci ke printer. Lapor IT jika kabel sudah benar.

## Cara apply member / poin loyalty

Tekan **F6** di layar Penjualan, masukkan nomor HP atau kode member,
tekan **Enter**. Diskon member otomatis diterapkan dan stiker (poin)
dihitung pada akhir transaksi.

Setiap **Rp 10.000** = 1 stiker. Stiker bisa ditukar di akhir transaksi
berikutnya (tidak akumulatif lintas hari, harus diklaim hari yang sama).

## Cara tutup shift

Setelah register selesai dipakai, tekan **Ctrl+Shift+T** atau menu
**Penjualan → Tutup Shift**. Hitung uang fisik di laci, masukkan total ke
kolom **Uang Tunai Akhir**. Sistem otomatis menampilkan selisih (over/short)
dengan kas yang diperhitungkan.

## Sync ke server gagal

Sync ke server (Supabase) berjalan otomatis di latar belakang. Jika
indikator sync di status bar berubah kuning atau merah:

- Kuning = tertunda sementara, akan retry otomatis dalam 30 detik
- Merah = sudah lebih dari 5 menit tertunda, periksa koneksi internet

Transaksi tetap bisa dijalankan offline; data akan tersinkron saat koneksi
pulih. Tidak perlu menahan kasir.

## Cara cek harga tanpa scan

Tekan **F2** untuk buka pencarian produk, ketik nama atau kode barang.
Hasil pencarian menampilkan harga ritel, harga grosir, dan stok terkini.
Tekan **Esc** untuk tutup tanpa menambahkan ke nota.
