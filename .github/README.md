# KeyGo Hán Việt cho macOS và iPhone / iPad

Bộ gõ Hán Việt chạy trên thanh trình đơn macOS (Avalonia, .NET 10), kèm bản
bàn phím tuỳ chỉnh cho iPhone / iPad.

## Tải về (bản mới nhất, không cần đăng nhập)

Trang Release: <https://github.com/cuongpzo/KeyGoHanViet.Mac/releases/tag/latest>

| Thiết bị | Link tải |
|---|---|
| iPhone / iPad (cùng một tệp) | [KeyGoHanViet-iOS-unsigned.ipa](https://github.com/cuongpzo/KeyGoHanViet.Mac/releases/download/latest/KeyGoHanViet-iOS-unsigned.ipa) |
| Mac Apple Silicon (M1 trở lên) | [KeyGoHanViet-macOS-arm64.zip](https://github.com/cuongpzo/KeyGoHanViet.Mac/releases/download/latest/KeyGoHanViet-macOS-arm64.zip) |
| Mac Intel | [KeyGoHanViet-macOS-x64.zip](https://github.com/cuongpzo/KeyGoHanViet.Mac/releases/download/latest/KeyGoHanViet-macOS-x64.zip) |

Mỗi lần push lên `main`, workflow **Build** (`.github/workflows/build.yml`) build lại cả ba
và thay tệp ở các link trên.

**iPhone / iPad:** tệp `.ipa` chưa ký - ký và cài bằng Sideloadly, xem
[HUONG_DAN_IPHONE.md](../HUONG_DAN_IPHONE.md).

## Cài trên macOS

1. Tải tệp zip đúng loại máy ở bảng trên (Apple ▸ Giới thiệu về máy Mac: "Chip Apple M…" là
   Apple Silicon, "Bộ xử lý Intel…" là Intel).
2. Giải nén → ra `KeyGo Hán Việt.app`.
3. Mở Terminal tại thư mục đó, gỡ cờ cách ly rồi chép vào Applications:

   ```bash
   xattr -cr "KeyGo Hán Việt.app"
   cp -R "KeyGo Hán Việt.app" /Applications/
   ```

4. Lần chạy đầu: **Cài đặt Hệ thống ▸ Quyền riêng tư & Bảo mật ▸ Trợ năng** → bật KeyGo Hán Việt.

Gói chỉ ký tạm (ad-hoc), không qua notarize của Apple, nên bắt buộc làm bước 3.
Bản macOS không hết hạn như bản iPhone.

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
