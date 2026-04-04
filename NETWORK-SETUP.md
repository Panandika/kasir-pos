# Panduan Setup Jaringan Multi-Register

## Arsitektur

```
Register 01 (HUB)              Register 02              Register 03
┌──────────────┐            ┌──────────────┐         ┌──────────────┐
│  kasir.db    │            │  kasir.db    │         │  kasir.db    │
│  (master)    │            │  (lokal)     │         │  (lokal)     │
└──────┬───────┘            └──────┬───────┘         └──────┬───────┘
       │                           │                        │
       └───────── \\KASIR01\kasir\sync\ ────────────────────┘
                  (shared folder via SMB)
```

- Setiap register punya database SQLite sendiri (lokal)
- Register 01 = HUB (sumber data master: produk, supplier, harga)
- Sinkronisasi via file JSON di folder bersama (bukan database langsung via jaringan)
- Data ditandatangani dengan HMAC-SHA256 untuk keamanan

## Langkah Setup

### 1. Setup Register 01 (HUB)

#### Buat Shared Folder

1. Buat folder: `C:\kasir\sync\`
2. Klik kanan folder `sync` → Properties → Sharing → Advanced Sharing
3. Centang "Share this folder"
4. Share name: `sync`
5. Permissions: Everyone → Full Control (atau buat user khusus)
6. Klik OK

Folder bersama sekarang bisa diakses di: `\\KASIR01\kasir\sync\`

> **Catatan:** Ganti `KASIR01` dengan nama komputer Register 01.
> Cek nama komputer: buka CMD → ketik `echo %COMPUTERNAME%`

#### Buat Sub-folder

```
C:\kasir\sync\
├── outbox\      ← file JSON keluar
├── archive\     ← file JSON yang sudah diproses
└── ack\         ← acknowledgment
```

Buka CMD di Register 01:
```cmd
mkdir C:\kasir\sync\outbox
mkdir C:\kasir\sync\archive
mkdir C:\kasir\sync\ack
```

#### Setting Config di Aplikasi

Buka aplikasi sebagai admin, lalu set config berikut di database (atau melalui menu Utility):

| Key | Value | Keterangan |
|-----|-------|------------|
| `register_id` | `01` | ID register ini |
| `sync_hub_share` | `C:\kasir\sync` | Path lokal (karena ini HUB) |
| `sync_hmac_key` | `ganti-dengan-kunci-rahasia-anda` | Kunci HMAC — harus sama di semua register |

### 2. Setup Register 02, 03, dst.

#### Pastikan Bisa Akses Folder Bersama

1. Buka File Explorer
2. Ketik di address bar: `\\KASIR01\kasir\sync\`
3. Harus bisa melihat folder `outbox`, `archive`, `ack`
4. Jika tidak bisa, periksa:
   - Kedua PC terhubung ke jaringan yang sama
   - Firewall tidak memblokir SMB (port 445)
   - Sharing sudah aktif di Register 01

#### Setting Config di Aplikasi

| Key | Value | Keterangan |
|-----|-------|------------|
| `register_id` | `02` (atau `03`, dst.) | ID unik untuk register ini |
| `sync_hub_share` | `\\KASIR01\kasir\sync` | Path jaringan ke folder bersama |
| `sync_hmac_key` | `ganti-dengan-kunci-rahasia-anda` | **Harus sama** dengan Register 01 |

### 3. Copy Database Awal

Sebelum register baru mulai dipakai:

1. Copy `kasir.db` dari Register 01 ke register baru
2. Letakkan di folder `data\` di samping `Kasir.exe`
3. Jalankan aplikasi — database sudah berisi data master

### 4. Cara Sinkronisasi

- **Otomatis:** Push setiap 15 detik, Pull setiap 60 detik
- **Manual:** Tekan **F12** di menu utama
- Status sync terlihat di status bar bawah

### 5. Arah Sinkronisasi

| Data | Arah | Keterangan |
|------|------|------------|
| Produk, Supplier, Harga | HUB → Register | Satu arah, tidak ada konflik |
| Departemen, Diskon, Member | HUB → Register | Satu arah |
| Penjualan (Sales) | Register → HUB | Setiap register kirim transaksinya |
| Pembelian, Pembayaran | HUB → Register | Biasanya diinput di HUB |
| Transfer Stok | Dua arah | Partisi berdasarkan register_id |

## Troubleshooting

| Masalah | Solusi |
|---------|--------|
| "sync_hub_share not configured" | Set config `sync_hub_share` di database |
| Tidak bisa akses `\\KASIR01\...` | Periksa jaringan, firewall, sharing permissions |
| Sync error: HMAC mismatch | Pastikan `sync_hmac_key` sama di semua register |
| Data produk tidak muncul di register 02 | Tekan F12 di register 02 untuk sync manual |
| "Network path not found" | Pastikan Register 01 menyala dan folder di-share |

## Keamanan

- **Ganti `sync_hmac_key`** dari default — gunakan string acak minimal 32 karakter
- Jangan gunakan `default-hmac-key-change-me` di produksi
- Folder sync hanya perlu diakses oleh PC register, bukan dari luar
- File JSON ditandatangani — file yang diubah/dipalsukan akan ditolak

## Catatan Teknis

- SQLite **tidak boleh diakses langsung via jaringan** (bisa korup)
- Setiap register punya database sendiri — aman dari korupsi jaringan
- Jika jaringan mati, register tetap bisa beroperasi offline
- Saat jaringan kembali, sync otomatis menyusul
