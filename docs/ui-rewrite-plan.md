# Kế hoạch: viết lại giao diện bằng WPF + WPF-UI (Fluent)

Trạng thái: **đã chốt, chưa bắt đầu** (2026-11-17).

## Mục tiêu

- Giao diện kiểu Windows 11 (Fluent, Mica, sáng/tối theo hệ thống) như Lenovo Legion Toolkit.
- Hai ngôn ngữ **English (mặc định) / Tiếng Việt**, đổi trong Settings.
- Giữ nguyên toàn bộ logic hiện có: `Tb16p.Core` không đổi, `TuningEngine` / `SensorLogger` / `StartupTask` / `Log` chuyển nguyên sang dự án mới.

## Kiến trúc

- Dự án mới `src/Tb16p.Ui` (WPF, .NET 8, `net8.0-windows`), thay `src/Tb16p.App` (WinForms). Giữ WinForms tới khi bản WPF chạy ổn, rồi xóa.
- Thư viện: `WPF-UI` (lepoco/wpfui) cho NavigationView, Card, ToggleSwitch, NumberBox, Mica, theme; `CommunityToolkit.Mvvm` cho ViewModel.
- Khay hệ thống: `NotifyIcon` của WPF-UI (`Wpf.Ui.Tray`) hoặc giữ `System.Windows.Forms.NotifyIcon` (đã chạy ổn).
- Đường cong quạt: viết lại `FanCurveEditor` thành control WPF (Canvas + Thumb), giữ y nguyên luật hiện tại: kéo tự do 1 °C / 100 rpm, không giảm, nhấp đúp thêm, chuột phải xóa, 2–16 điểm, vạch trực tiếp, lớp phủ "Quạt tối đa".
- Ngôn ngữ: file resource `Strings.en.resx` / `Strings.vi.resx` + markup extension `{loc:Tr Key}`; đổi ngôn ngữ không cần khởi động lại (raise PropertyChanged cho toàn bộ chuỗi).

## Bố cục cửa sổ

**Header cố định (mọi tab):** nguồn điện (Charging / Battery) · profile đang chạy · lần áp cuối hoặc lỗi · trạng thái quạt (EC / đường cong N rpm / TỐI ĐA) · CPU · CB1 · CB5 °C · Quạt 1 / Quạt 2 rpm. Màu cảnh báo khi lỗi, cam khi quạt tối đa.

**Thanh điều hướng trái (NavigationView):**

1. **General / Tổng quan**
   - Profile khi cắm sạc, profile khi dùng pin (ComboBox).
   - Chế độ sạc: Conservation / Normal / Rapid / Không quản lý (dạng 4 thẻ chọn).
   - Nút nhanh: Áp lại ngay, Tạm dừng, Quạt tối đa (thử).
   - Thẻ tóm tắt profile đang chạy (Slow/Fast/STAPM, CO bật/tắt, quạt EC hay đường cong).
2. **Profiles**
   - Danh sách profile bên trái (Thêm / Nhân bản / Xóa / đổi tên).
   - Bên phải 2 tab con: **Power & Curve Optimizer** và **Fan** (giống hiện tại).
   - Nút Lưu & áp dụng / Hủy thay đổi, cảnh báo khi rời trang chưa lưu.
3. **Settings / Cài đặt**
   - Ngôn ngữ: English / Tiếng Việt.
   - Chạy cùng Windows.
   - Đóng cửa sổ: thu nhỏ xuống khay (mặc định) / thoát hẳn.
   - Giao diện: theo hệ thống / sáng / tối.
   - Nâng cao: ghi lại giới hạn mỗi N giây, ghi lại Curve Optimizer mỗi N giây.
     → Đặt ở **Settings** vì đây là cơ chế chống EC ghi đè, áp dụng chung cho mọi profile, không phải thuộc tính của profile.
   - Ghi nhật ký cảm biến, Mở thư mục dữ liệu, Xem nhật ký.
4. **Support / Ủng hộ**
   - Lời giới thiệu ngắn, mã QR chuyển khoản (ảnh do chủ dự án cung cấp), nút link quốc tế (Ko-fi / Buy Me a Coffee / PayPal / GitHub Sponsors).
   - Ghi công: UXTU, RyzenAdj, Lenovo Legion Toolkit, WinRing0.
   - Phiên bản app, link GitHub, cảnh báo "dùng với rủi ro của bạn".
5. (Chân thanh điều hướng) **About** có thể gộp vào Support.

## Cài đặt mới trong `AppSettings`

- `Language` (`en` | `vi`, mặc định `en`)
- `CloseToTray` (bool, mặc định `true`)
- `Theme` (`System` | `Light` | `Dark`)

## Các bước

1. Tạo `src/Tb16p.Ui`, thêm WPF-UI + CommunityToolkit.Mvvm, chuyển `TuningEngine`, `SensorLogger`, `StartupTask`, `Log` sang (hoặc tách ra `src/Tb16p.Service` dùng chung).
2. Khung cửa sổ: Mica, NavigationView, header trạng thái, khay hệ thống, đóng → khay.
3. Hệ thống ngôn ngữ + toàn bộ chuỗi EN/VI.
4. Trang General.
5. Trang Profiles + editor điện/CO + control đường cong quạt WPF.
6. Trang Settings.
7. Trang Support (chờ QR / link).
8. `publish.ps1` trỏ sang dự án mới, test toàn bộ trên máy, xóa `src/Tb16p.App`, cập nhật README (ảnh chụp màn hình, mục Ủng hộ), thêm `.github/FUNDING.yml`.

## Thông tin ủng hộ (đã có)

- VietQR (Vietcombank): `assets/support/vietqr.png`
- PayPal: https://paypal.me/nmhthien (nút Donate của PayPal không mở cho tài khoản Việt Nam, dùng PayPal.Me)

## Cần chủ dự án cung cấp

- Quyết định tên app (giữ "Thinkbook16PController" hay đổi tên riêng, tránh thương hiệu Lenovo khi nhận donate).
- (Tùy chọn) icon app.
