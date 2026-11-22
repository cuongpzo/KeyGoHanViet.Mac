# Cài KeyGo Hán Việt lên iPhone (iPhone 17 Pro Max, iOS 26)

Trên iPhone, KeyGo Hán Việt là một **bàn phím tuỳ chỉnh**: bạn chuyển sang nó
bằng phím 🌐 như chuyển sang bàn phím tiếng Anh / emoji. iOS không cho chương
trình nào "móc" vào bàn phím hệ thống như trên Windows / macOS, nên đây là cách
duy nhất.

Lõi gõ (TELEX tự do thứ tự, từ điển hannomPS, bảng âm đọc bổ sung, gõ tắt, ghi
nhớ lượt chọn) dùng chung với bản máy tính, nên `hans` → 漢, `dofod` → 圖 y hệt.

---

## Bước 1 - Tải tệp .ipa từ GitHub

1. Mở <https://github.com/cuongpzo/KeyGoHanViet.Mac/actions/workflows/build-ios.yml>
2. Bấm vào lần chạy mới nhất có dấu ✓ xanh.
3. Kéo xuống mục **Artifacts** → tải **KeyGoHanViet-iOS-unsigned-ipa** (phải đăng nhập GitHub).
4. Giải nén tệp zip vừa tải → được `KeyGoHanViet-<phiên bản>-iOS-unsigned.ipa`.

Muốn build lại bằng tay: trang trên → **Run workflow** → **Run workflow**.
Mỗi lần `git push` lên `main` cũng tự build.

## Bước 2 - Chuẩn bị trên Windows

1. Cài **iTunes bản tải từ apple.com** (không dùng bản Microsoft Store) và
   **iCloud for Windows** bản từ apple.com - Sideloadly cần driver của hai thứ này.
   Link: <https://www.apple.com/itunes/download/win64> và <https://support.apple.com/HT204283>.
2. Tải và cài **Sideloadly**: <https://sideloadly.io>.
3. Cắm iPhone bằng cáp, mở khoá máy, bấm **Tin cậy** (Trust) máy tính này.

## Bước 3 - Ký và cài bằng Sideloadly

1. Mở Sideloadly. Ô **iDevice** phải hiện tên iPhone của bạn.
2. Kéo thả tệp `.ipa` vào biểu tượng IPA bên trái.
3. Ô **Apple account**: nhập Apple ID (nên dùng một Apple ID phụ, không phải tài khoản chính).
4. Bấm **Start**, nhập mật khẩu Apple ID khi được hỏi (và mã xác minh 2 bước nếu có).
   Sideloadly tự ký cả app lẫn bàn phím bên trong.
5. Chờ tới dòng **Done**.

## Bước 4 - Cho phép chạy trên iPhone

1. **Bật Chế độ nhà phát triển** (bắt buộc từ iOS 16):
   Cài đặt ▸ Quyền riêng tư & Bảo mật ▸ **Chế độ nhà phát triển** ▸ Bật → máy khởi động lại
   → sau khi mở máy chọn **Bật**.
   (Nếu không thấy mục này: cài app bằng Sideloadly một lần trước, mục sẽ hiện ra.)
2. **Tin cậy chứng chỉ**: Cài đặt ▸ Cài đặt chung ▸ **VPN & Quản lý thiết bị** ▸ chọn
   Apple ID của bạn ▸ **Tin cậy**.

## Bước 5 - Bật bàn phím

1. Mở app **KeyGo Hán Việt** → bấm **Mở Cài đặt** → **Bàn phím** → bật **KeyGo Hán Việt**.
   (Hoặc: Cài đặt ▸ Cài đặt chung ▸ Bàn phím ▸ Bàn phím ▸ Thêm bàn phím mới ▸ KeyGo Hán Việt.)
2. KHÔNG cần bật "Cho phép truy cập đầy đủ" - bàn phím không dùng mạng.
3. Ở ô gõ bất kỳ, **giữ phím 🌐** → chọn **KeyGo Hán Việt**.

## Cách gõ

| Thao tác | Kết quả |
|---|---|
| Gõ TELEX: `hans`, `dofod`, `hansvieetj` | Thanh trên hiện âm Quốc ngữ + chữ Hán gợi ý |
| Chạm một chữ trên thanh gợi ý | Chèn chữ đó |
| **Dấu cách** khi đang gõ dở | Chèn chữ đầu tiên (đang tô nền) |
| Phím **↵** khi đang gõ dở | Chèn âm Quốc ngữ có dấu (`vieetj` → việt) |
| Bảng **123**, số **1-9** khi đang gõ dở | Chọn chữ thứ 1-9 |
| Dấu câu khi đang gõ dở | Chèn chữ đầu tiên rồi chèn dấu |
| Phím **漢 / En** | Bật / tắt chế độ Hán Nôm (En = gõ chữ cái thường) |
| Giữ **⌫** | Xoá liên tục |

## Lưu ý quan trọng

- **Apple ID miễn phí: app hết hạn sau 7 ngày.** Hết hạn thì bàn phím biến mất khỏi danh
  sách. Cắm máy, mở Sideloadly, cài lại tệp `.ipa` cũ là xong (dữ liệu lượt chọn vẫn giữ).
  Tài khoản Apple Developer ($99/năm) thì hạn 1 năm.
- Apple ID miễn phí cài tối đa 3 app tự ký cùng lúc.
- Một số chữ Hán hiếm (khối mở rộng B trở đi, ví dụ 𠊛) có thể hiện ô vuông vì
  phông của iOS không có - chèn vào vẫn đúng mã, chỉ là không thấy hình.
- Ô mật khẩu luôn dùng bàn phím hệ thống - đó là quy định của iOS.
- Nếu bàn phím bật lên rồi tự quay về bàn phím cũ: iOS đã đóng nó vì thiếu RAM
  (bàn phím tuỳ chỉnh chỉ được ~60-70 MB). Báo lại để tối ưu từ điển.
