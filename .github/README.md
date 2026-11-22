# KeyGo Hán Việt cho macOS và iPhone / iPad

Bộ gõ Hán Việt chạy trên thanh trình đơn macOS (Avalonia, .NET 10), kèm bản
bàn phím tuỳ chỉnh cho iPhone / iPad.

**iPhone / iPad:** xem [HUONG_DAN_IPHONE.md](../HUONG_DAN_IPHONE.md) — tải `.ipa` từ
workflow **Build iOS**, ký và cài bằng Sideloadly.

## macOS

## Tải bản build sẵn

1. Mở tab **Actions** → chọn lần chạy **Build macOS** mới nhất (dấu ✓ xanh).
2. Kéo xuống mục **Artifacts**, tải:
   - `KeyGoHanViet-macOS-arm64` cho máy Apple Silicon (M1 trở lên)
   - `KeyGoHanViet-macOS-x64` cho máy Intel
3. Giải nén (GitHub gói thêm một lớp zip) → ra `KeyGo Hán Việt.app`.
4. Gỡ cờ cách ly rồi chép vào Applications:

   ```bash
   xattr -cr "KeyGo Hán Việt.app"
   cp -R "KeyGo Hán Việt.app" /Applications/
   ```

5. Lần chạy đầu: **Cài đặt Hệ thống ▸ Quyền riêng tư & Bảo mật ▸ Trợ năng** → bật KeyGo Hán Việt.

Gói chỉ ký tạm (ad-hoc), không qua notarize của Apple, nên bắt buộc làm bước 4.

## Build trên máy Mac

```bash
./publish-mac.sh                 # theo loại máy đang chạy
./publish-mac.sh -r osx-x64      # bản Intel
```

## Cấu trúc kho

- `src/KeyGoHanViet.Mac` — ứng dụng macOS
- `src/KeyGoHanViet.iOS` — app chứa cho iPhone / iPad (hướng dẫn + ô gõ thử)
- `src/KeyGoHanViet.iOS.Keyboard` — bàn phím tuỳ chỉnh iOS (Keyboard Extension)
- `src/KeyGoHanViet.Shared` — lõi dùng chung với bản Windows
- `src/KeyGoHanViet/Data`, `*.ico` — dữ liệu mặc định và icon K/Q dùng chung
- `hannomPS.*.yaml`, `ChinesePhienAmWords.txt` — từ điển
- `Directory.Build.props` — số phiên bản duy nhất
