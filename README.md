# Thinkbook16PController

Công cụ chỉnh giới hạn điện, Curve Optimizer và chế độ sạc pin cho **Lenovo ThinkBook 16p G6 ADR (21U0, Ryzen 9 8945HX)**, bản nội địa Trung Quốc không có phần mềm tinh chỉnh của Lenovo.

> ⚠️ Chỉ dành cho CPU **AMD Dragon Range** (family 19h model 61h). App tự kiểm tra và từ chối chạy trên CPU khác. Dùng với rủi ro của bạn.

## Vì sao cần

- Các mode Fn+Q chỉ có Eco / tự điều chỉnh / Performance. Performance đẩy CPU lên 9x °C.
- Giới hạn đặt bằng UXTU (RyzenAdj) bị **EC của máy âm thầm ghi đè** về bảng giới hạn của firmware sau vài phút.
- Rút sạc thì mất cả giới hạn lẫn Curve Optimizer.
- Rút cáp pin (khi vệ sinh máy) thì Conservation mode bị tắt.

## Tính năng

- Profile gồm STAPM / Fast / Slow / ngưỡng nhiệt CPU và Curve Optimizer từng nhân (2 CCD × 8 nhân).
- Tự đổi profile khi cắm hoặc rút sạc, áp lại sau khi máy thức dậy từ sleep.
- Ghi lại giới hạn mỗi vài giây (mặc định 2 s), Curve Optimizer mỗi 30 s. Bảng PM của Dragon Range không đọc được nên app không phát hiện được lúc EC ghi đè, vì vậy ghi lại theo chu kỳ.
- Chế độ sạc Conservation / Normal / Rapid Charge, tự bật lại nếu bị tắt.
- Cửa sổ quản lý (chuột trái vào biểu tượng khay) hiển thị nhiệt độ CPU và tốc độ 2 quạt.
- Chạy cùng Windows qua Task Scheduler với quyền cao nhất, không hiện UAC mỗi lần bật máy.

Chưa có: chỉnh đường cong quạt. Trên máy này `Fan_Set_Table` của GameZone nhận lệnh nhưng không có tác dụng (xem phần Ghi chú kỹ thuật).

## Cài đặt

Yêu cầu: Windows 10/11 x64, .NET 8 SDK.

1. Thêm thư mục project vào **Windows Security → Virus & threat protection → Exclusions**. Defender chặn driver WinRing0 nếu không làm bước này.
2. Thoát UXTU / Lenovo Legion Toolkit (và tắt chế độ tự khởi động của chúng) để các app không ghi đè lẫn nhau.
3. Build vào thư mục `app\`:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\publish.ps1
   ```
4. Chạy `app\Thinkbook16PController.exe` (cần quyền admin), rồi bật **Chạy cùng Windows** trong cửa sổ quản lý.

Cài đặt lưu ở `C:\ProgramData\Thinkbook16PController\settings.json`, nhật ký ở `log.txt` cùng thư mục.

Công cụ dòng lệnh để thử nghiệm: `src\Tb16p.Cli` (`tb16p info | limits | co | battery`).

## Cấu trúc

| Thư mục | Nội dung |
|---|---|
| `src/Tb16p.Core` | Giao tiếp SMU qua WinRing0, lệnh Dragon Range, Curve Optimizer, chế độ sạc qua `\\.\EnergyDrv`, cảm biến GameZone, cài đặt |
| `src/Tb16p.App` | App ở khay hệ thống + cửa sổ quản lý (WinForms) |
| `src/Tb16p.Cli` | Công cụ dòng lệnh để thử nghiệm |
| `tests/` | Các script PowerShell đã dùng để khảo sát máy |
| `vendor/WinRing0` | Driver WinRing0 |

## Ghi chú kỹ thuật (ThinkBook 16p G6 ADR)

- Mode Fn+Q đi qua ITS/DYTC của Lenovo Dispatcher (`HKLM\SYSTEM\CurrentControlSet\Services\LenovoProcessManagement\Performance\PowerSlider`, `ITS_CurrentSetting`: 0 = auto, 1 = eco, 3 = performance). Dispatcher **không** đặt PLx trên AMD. Thủ phạm ghi đè giới hạn là EC/firmware.
- BIOS có giao diện GameZone (`ACPI\PNP0C14\GMZN`) giống dòng Legion. Các getter nhiệt độ và quạt hoạt động, nhưng `SetSmartFanMode` / `Fan_Set_Table` không có tác dụng (`Fan_Set_Table` phải nhận đúng 64 byte).
- Bảng giới hạn firmware (Quiet / Balanced / Performance): dài hạn 55/70/80 W, đỉnh 75/102/155 W, ngưỡng nhiệt 100 °C.
- RyzenAdj 0.19 crash trên máy này (không đọc được bảng PM của Dragon Range), nên app tự cài giao thức SMU mailbox.

## Ghi công

- [Universal x86 Tuning Utility](https://github.com/JamesCJ60/Universal-x86-Tuning-Utility) (GPL-3.0): địa chỉ SMU mailbox, mã lệnh Dragon Range và cách mã hóa Curve Optimizer từng nhân.
- [RyzenAdj](https://github.com/FlyGoat/RyzenAdj) (LGPL-3.0): giao thức SMU mailbox; bản phân phối WinRing0.
- [Lenovo Legion Toolkit](https://github.com/BartoszCichecki/LenovoLegionToolkit): IOCTL chế độ sạc pin qua `\\.\EnergyDrv`.
- WinRing0 / OpenLibSys của hiyohiyo.

## Giấy phép

GPL-3.0, xem [LICENSE](LICENSE).
