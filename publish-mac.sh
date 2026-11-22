#!/bin/bash
#
# Đóng gói KeyGo Hán Việt thành một gói .app hoàn chỉnh cho macOS.
#
# Kết quả nằm ở ./dist/ :
#
#     KeyGo Hán Việt.app        kéo thả vào /Applications là chạy được
#
# Máy đích KHÔNG cần cài .NET: bản mặc định đã gộp sẵn runtime vào trong gói.
# Thư mục từ điển nằm ở Contents/Resources/Data bên trong gói, mở ra sửa được
# bằng cách bấm chuột phải vào gói rồi chọn "Show Package Contents".
#
# Cách dùng:
#     ./publish-mac.sh                          # theo đúng loại máy đang chạy
#     ./publish-mac.sh -r osx-x64               # bản cho máy Intel
#     ./publish-mac.sh -r osx-arm64             # bản cho máy Apple Silicon
#     ./publish-mac.sh --framework-dependent    # bản nhẹ, máy đích phải có .NET 10
#
set -euo pipefail

# ------------------------------------------------------------------ tham số

RUNTIME=""
SELF_CONTAINED="true"

while [ $# -gt 0 ]; do
    case "$1" in
        -r|--runtime)
            RUNTIME="${2:-}"
            shift 2
            ;;
        --framework-dependent)
            SELF_CONTAINED="false"
            shift
            ;;
        -h|--help)
            sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            echo "Tham số không hiểu: $1" >&2
            exit 2
            ;;
    esac
done

if [ "$(uname -s)" != "Darwin" ]; then
    echo "Script này chỉ chạy được trên macOS: nó cần sips, iconutil và codesign." >&2
    echo "Trên Windows/Linux vẫn biên dịch thử được bằng:" >&2
    echo "    dotnet publish src/KeyGoHanViet.Mac -c Release -r osx-arm64 --self-contained true" >&2
    exit 1
fi

# Không truyền -r thì tự nhận loại máy đang chạy.
if [ -z "$RUNTIME" ]; then
    case "$(uname -m)" in
        arm64) RUNTIME="osx-arm64" ;;
        x86_64) RUNTIME="osx-x64" ;;
        *)
            echo "Không nhận ra kiến trúc máy: $(uname -m). Hãy chỉ rõ bằng -r osx-arm64 hoặc -r osx-x64." >&2
            exit 1
            ;;
    esac
fi

ROOT="$(cd "$(dirname "$0")" && pwd)"
PROJECT="$ROOT/src/KeyGoHanViet.Mac/KeyGoHanViet.Mac.csproj"
DIST="$ROOT/dist"
APP="$DIST/KeyGo Hán Việt.app"
STAGE="$DIST/.stage"

BUNDLE_ID="com.cuongpzo.keygohanviet"
# Số phiên bản lấy từ Directory.Build.props - nguồn duy nhất cho mọi bản.
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props" | head -1)"
VERSION="${VERSION:-0.0.0}"

[ -f "$PROJECT" ] || { echo "Không tìm thấy dự án: $PROJECT" >&2; exit 1; }

# --------------------------------------------------------------- biên dịch

# Chương trình sống ở thanh trình đơn và giữ khoá tệp - phải đóng trước khi ghi đè.
if pgrep -x KeyGoHanViet >/dev/null 2>&1; then
    echo "Đang đóng bản KeyGo Hán Việt đang chạy…"
    pkill -x KeyGoHanViet || true
    sleep 1
fi

rm -rf "$STAGE" "$APP"
mkdir -p "$STAGE"

if [ "$SELF_CONTAINED" = "true" ]; then
    echo "Đóng gói bản độc lập (không cần cài .NET) cho $RUNTIME …"
else
    echo "Đóng gói bản phụ thuộc runtime cho $RUNTIME …"
fi

dotnet publish "$PROJECT" \
    -c Release \
    -r "$RUNTIME" \
    --self-contained "$SELF_CONTAINED" \
    -p:DebugType=none \
    -o "$STAGE" \
    --nologo

# ------------------------------------------------------- dựng cấu trúc .app

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# Từ điển tách sang Resources theo đúng quy ước của Apple; AppPaths tự tìm
# thấy ở đó (xem FindBundledDataDir trong KeyGoHanViet.Shared/AppPaths.cs).
if [ -d "$STAGE/Data" ]; then
    mv "$STAGE/Data" "$APP/Contents/Resources/Data"
fi

# Mọi thứ còn lại - tệp thực thi, các .dll và .dylib - vào Contents/MacOS.
mv "$STAGE"/* "$APP/Contents/MacOS/"
rmdir "$STAGE"

chmod +x "$APP/Contents/MacOS/KeyGoHanViet"

# ---------------------------------------------------------------- biểu tượng

ICON_SRC="$ROOT/src/KeyGoHanViet.Mac/AppIcon.png"
if [ -f "$ICON_SRC" ]; then
    ICONSET="$DIST/AppIcon.iconset"
    rm -rf "$ICONSET"
    mkdir -p "$ICONSET"

    # Ảnh gốc là 256×256, nên chỉ dựng tới khung 256 - phóng to lên 512 chỉ làm
    # biểu tượng nhoè chứ không thêm chi tiết nào.
    sips -z 16 16   "$ICON_SRC" --out "$ICONSET/icon_16x16.png"      >/dev/null
    sips -z 32 32   "$ICON_SRC" --out "$ICONSET/icon_16x16@2x.png"   >/dev/null
    sips -z 32 32   "$ICON_SRC" --out "$ICONSET/icon_32x32.png"      >/dev/null
    sips -z 64 64   "$ICON_SRC" --out "$ICONSET/icon_32x32@2x.png"   >/dev/null
    sips -z 128 128 "$ICON_SRC" --out "$ICONSET/icon_128x128.png"    >/dev/null
    sips -z 256 256 "$ICON_SRC" --out "$ICONSET/icon_128x128@2x.png" >/dev/null
    sips -z 256 256 "$ICON_SRC" --out "$ICONSET/icon_256x256.png"    >/dev/null

    iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
    rm -rf "$ICONSET"
else
    echo "Chú ý: không thấy $ICON_SRC — gói sẽ dùng biểu tượng mặc định của macOS."
fi

# ----------------------------------------------------------------- Info.plist

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>              <string>KeyGo Hán Việt</string>
    <key>CFBundleDisplayName</key>       <string>KeyGo Hán Việt</string>
    <key>CFBundleIdentifier</key>        <string>$BUNDLE_ID</string>
    <key>CFBundleExecutable</key>        <string>KeyGoHanViet</string>
    <key>CFBundleIconFile</key>          <string>AppIcon</string>
    <key>CFBundlePackageType</key>       <string>APPL</string>
    <key>CFBundleShortVersionString</key><string>$VERSION</string>
    <key>CFBundleVersion</key>           <string>$VERSION</string>
    <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
    <key>LSMinimumSystemVersion</key>    <string>11.0</string>
    <key>NSHighResolutionCapable</key>   <true/>
    <key>NSHumanReadableCopyright</key>  <string>Từ điển hannomPS (Rime) — Keepout2010. Ứng dụng — Cuongpzo.</string>

    <!--
        Bộ gõ sống ở thanh trình đơn trên cùng, không có cửa sổ chính và không
        cần biểu tượng dưới Dock. Bỏ khoá này đi thì macOS coi đây là ứng dụng
        thường và luôn hiện một biểu tượng thừa ở Dock.
    -->
    <key>LSUIElement</key>               <true/>
</dict>
</plist>
PLIST

# -------------------------------------------------------------------- ký gói

# Gỡ cờ cách ly do trình duyệt gắn vào, nếu không macOS sẽ chặn ngay lần mở đầu.
xattr -cr "$APP" 2>/dev/null || true

# Ký tạm (ad-hoc). Bắt buộc phải ký thì hệ thống quyền Trợ năng mới nhớ được
# chương trình; gói chưa ký sẽ bị hỏi lại quyền sau mỗi lần chạy.
if codesign --force --deep --sign - --timestamp=none "$APP" 2>/dev/null; then
    echo "Đã ký tạm (ad-hoc)."
else
    echo "Chú ý: không ký được gói. Quyền Trợ năng có thể bị hỏi lại mỗi lần chạy."
fi

# ------------------------------------------------------------------- kết quả

echo ""
echo "Kết quả: $APP"
echo "  kiến trúc      : $RUNTIME"
echo "  dung lượng     : $(du -sh "$APP" | cut -f1)"
echo "  số tệp         : $(find "$APP" -type f | wc -l | tr -d ' ')"
echo ""
echo "Chạy thử ngay      :  open \"$APP\""
echo "Cài vào máy        :  cp -R \"$APP\" /Applications/"
echo ""
echo "Lần chạy đầu, macOS sẽ chưa cho đọc bàn phím. Vào"
echo "  Cài đặt Hệ thống ▸ Quyền riêng tư & Bảo mật ▸ Trợ năng"
echo "rồi bật KeyGo Hán Việt trong danh sách."
