# Đường cong quạt gốc của EC (đo được)

Đo từ nhật ký cảm biến ngày 2026-11-17 18:11 → 2026-11-18 00:54 (~12.000 mẫu, mỗi 2 giây), cắm sạc, Fn+Q Performance, quạt do EC tự quyết. Đã bỏ hai khoảng app điều khiển quạt (thử quạt tối đa 18:12–18:13, thử đường cong 18:58–18:59). Nhiệt CPU trong log từ 56 đến 89 °C, chủ yếu là lúc chơi game.

## Kết quả

EC không nội suy mà chạy theo **nấc cố định**, theo nhiệt CPU (cảm biến 4). CB5 luôn thấp hơn CPU nên nhiệt điều khiển của app (cao nhất của CPU và CB5) chính là nhiệt CPU.

| Nhiệt CPU khi lên nấc | Quạt 1 (rpm) | Quạt 2 (rpm) | Xuống nấc khi |
|---|---|---|---|
| ≤ 72 °C | 2100 | 2200 | |
| ~76 °C | 3100 | 3100 | ~61 °C |
| ~79 °C | 3400 | 3300 | 75–77 °C |
| ~82 °C | 3700 | 3600 | 77–79 °C |
| ~85 °C | 3900 | 4000 | 80–82 °C |

- Trễ khoảng 3 °C giữa lên và xuống nấc, giống luật trễ của app.
- Không thấy nấc nào dưới 2100 rpm (máy không nghỉ trong lúc ghi), cũng không có dữ liệu trên 89 °C.
- Thực tế nhẹ tay hơn hẳn bảng `LENOVO_FAN_TABLE_DATA` trong BIOS (bảng BIOS ghi 79 °C → 5500 rpm).

## Mẫu "Lenovo gốc (đo được)"

`FanCurve.LenovoStock()`: (72, 2100) (76, 3100) (79, 3400) (82, 3700) (85, 3900) (89, 3900) (95, 5500). Đoạn 89 → 95 °C chỉ để nối vào ngưỡng khẩn cấp 95 °C, không có dữ liệu đo.

Mô phỏng app chạy mẫu này (kèm trễ 3 °C) trên chính nhiệt độ trong log: lệch trung vị 100 rpm so với quạt 1 của EC, 99% mẫu lệch không quá 300 rpm.

Profile mới chuyển sang "Đường cong tùy chỉnh" sẽ bắt đầu từ mẫu này, nên chỉnh từ đúng hành vi gốc của máy.
