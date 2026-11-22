using Foundation;
using UIKit;

namespace KeyGoHanViet.iOS;

/// <summary>Màn hình duy nhất của app chứa: hướng dẫn bật bàn phím + ô gõ thử.</summary>
public sealed class MainViewController : UIViewController
{
    private const string Steps =
        "1. Bấm nút \"Mở Cài đặt\" bên dưới → Bàn phím → bật \"KeyGo Hán Việt\".\n" +
        "   (Hoặc: Cài đặt ▸ Cài đặt chung ▸ Bàn phím ▸ Bàn phím ▸ Thêm bàn phím mới.)\n\n" +
        "2. Chạm vào ô gõ thử, giữ phím 🌐 (quả địa cầu) rồi chọn KeyGo Hán Việt.\n\n" +
        "3. Gõ TELEX như trên máy tính:\n" +
        "   hans → 漢   ·   dofod → 圖   ·   hans vieetj → 漢越\n\n" +
        "   • Chạm chữ trên thanh gợi ý để chèn; dấu cách chèn chữ đầu tiên.\n" +
        "   • Phím ↵ chèn âm Quốc ngữ có dấu (gõ tiếng Việt).\n" +
        "   • Số 1-9 (bảng 123) chọn chữ theo thứ tự khi đang gõ dở.\n" +
        "   • Phím 漢 / En: bật / tắt chế độ Hán Nôm.\n\n" +
        "Không cần \"Cho phép truy cập đầy đủ\": bàn phím không dùng mạng.";

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        // Số phiên bản lấy từ Directory.Build.props qua ApplicationDisplayVersion.
        Title = "KeyGo Hán Việt " +
                NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleShortVersionString");
        View!.BackgroundColor = UIColor.SystemBackground;

        var steps = new UILabel
        {
            Text = Steps,
            Lines = 0,
            Font = UIFont.PreferredBody,
            TextColor = UIColor.Label,
        };

        var settings = UIButton.FromType(UIButtonType.System);
        settings.SetTitle("Mở Cài đặt", UIControlState.Normal);
        settings.TitleLabel!.Font = UIFont.BoldSystemFontOfSize(18);
        settings.TouchUpInside += (_, _) =>
            UIApplication.SharedApplication.OpenUrl(new NSUrl(UIApplication.OpenSettingsUrlString),
                                                    new UIApplicationOpenUrlOptions(), null);

        var test = new UITextView
        {
            Font = UIFont.SystemFontOfSize(22),
            BackgroundColor = UIColor.SecondarySystemBackground,
            Text = "",
        };
        test.Layer.CornerRadius = 10;

        var hint = new UILabel
        {
            Text = "Gõ thử ở đây:",
            Font = UIFont.PreferredHeadline,
            TextColor = UIColor.Label,
        };

        var stack = new UIStackView([steps, settings, hint, test])
        {
            Axis = UILayoutConstraintAxis.Vertical,
            Spacing = 14,
            Alignment = UIStackViewAlignment.Fill,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        stack.SetCustomSpacing(24, settings);
        View.AddSubview(stack);

        var guide = View.SafeAreaLayoutGuide;
        NSLayoutConstraint.ActivateConstraints(
        [
            stack.TopAnchor.ConstraintEqualTo(guide.TopAnchor, 16),
            stack.LeadingAnchor.ConstraintEqualTo(guide.LeadingAnchor, 16),
            stack.TrailingAnchor.ConstraintEqualTo(guide.TrailingAnchor, -16),
            test.HeightAnchor.ConstraintEqualTo(140),
        ]);

        // Chạm ra ngoài thì ẩn bàn phím.
        View.AddGestureRecognizer(new UITapGestureRecognizer(() => View.EndEditing(true))
        {
            CancelsTouchesInView = false,
        });
    }
}
