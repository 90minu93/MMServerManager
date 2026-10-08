# MMServer Manager v1.0

Bảng điều khiển một cửa sổ cho server offline **Kayito 0.97k** trên Windows.
Nó tự cấu hình mọi thứ: cơ sở dữ liệu MariaDB portable, IP trong các file cấu hình, encoder
tạo client, và bật / tắt các server đúng thứ tự.

Tác giả **90minutes** · https://www.youtube.com/@90minu93 · Giấy phép MIT

## Yêu cầu
- Windows 10 trở lên (64-bit).
- .NET Framework 4.8 (đã có sẵn từ Windows 10 bản 1903 trở lên).
- Visual C++ Redistributable 2015-2022 bản **x86 (32-bit)**: https://aka.ms/vs/17/release/vc_redist.x86.exe
  (server cần nó; nút **Kiểm tra hệ thống** sẽ báo nếu thiếu).
  Không có mạng? Repo bạn vừa tải có sẵn bản trong `Dependencies\C++ Redistributables 2017\VC_redist.x86.exe`; link Microsoft mới hơn nên ưu tiên.
- Repo server của Kayito do bạn tự tải từ GitHub: https://github.com/nicomuratona/MuEmu-0.97k-kayito
  (repo có `MuServer`, `Encoder` và `Client`). **Gói này không chứa file server hay client nào.**

## Cài đặt
1. Tải repo Kayito từ GitHub (Code > Download ZIP, hoặc `git clone`) rồi giải nén.
2. Tải `MMServerManager-v1.0.zip` ở trang Releases và giải nén **vào thư mục repo**
   (thư mục có `MuServer`, `Client` và `Encoder`), hoặc giải nén ở đâu đó rồi chép các file vào đó.
3. Tải **file ZIP MariaDB** (bản Windows, loại "ZIP file", phiên bản 10.11) từ https://mariadb.org/download
   rồi đặt cạnh `MMServerManager.exe`. Đừng đổi tên; tên file phải bắt đầu bằng `mariadb`.
4. Làm theo hướng dẫn của chính repo về các file client cần có trong `Client\`.

```
<thư mục repo>\
  MMServerManager.exe    từ bản release này
  mariadb-xx.zip         bạn tự tải ở mariadb.org (manager giải nén ở lần chạy đầu)
  Assets\                từ bản release này (icon, logo, header; tùy chọn)
  Docs\                  từ bản release này
  START-HERE.txt         từ bản release này
  MuServer\  Encoder\  Client\   từ repo Kayito
```

## Bắt đầu nhanh
1. Chạy `MMServerManager.exe`. Kiểm tra ô **Thư mục server** và **Thư mục client** đã trỏ đúng `MuServer` và `Client`.
2. Nhập IP mạng LAN của bạn (hoặc bấm **Dò IP**).
3. Bấm **Kiểm tra hệ thống**, rồi **Bật tất cả**. Lần đầu mất khoảng một phút (giải nén MariaDB và tạo cơ sở dữ liệu).
4. Bấm **Mở Client** và chơi. Tài khoản mẫu: `test1` đến `test5` (mật khẩu trùng tên tài khoản). Hãy đổi hoặc xóa chúng nếu người khác truy cập được server của bạn.

## Các nút
| Nút | Chức năng |
|---|---|
| Bật tất cả | Đặt IP, tạo lại client nếu IP đổi, rồi bật MariaDB, DataServer, JoinServer, ConnectServer, GameServer. Mỗi bước chờ cổng mở rồi mới sang bước sau. |
| Tắt tất cả | Đóng GameServer, ConnectServer, JoinServer, DataServer, rồi MariaDB (thứ tự ngược). Hộp thoại "bạn có chắc không?" của server được xác nhận tự động. |
| Tạo lại Client | Ghi IP, chạy encoder và chép `main.exe`, `Main.dll`, `Data\Local\ClientInfo.bmd` vào `Client\`. Dùng sau khi bạn sửa `MainInfo.ini`. |
| Mở Client | Chạy `Client\main.exe`. |
| Kiểm tra hệ thống | Kiểm tra .NET, Visual C++ x86 và các thư mục. |

Đèn trạng thái cho biết cổng nào đang mở. Nhật ký cũng được lưu vào `manager.log`.

## Manager thay đổi những gì
- `ConnectServer\ServerList.dat` và `MainInfo.ini`: **chỉ sửa IP**. Mọi thứ khác giữ nguyên như bản gốc; bản gốc đầu tiên được lưu thành `*.orig`.
- `DataServer` và `JoinServer`: được tạo từ `MuServer\MySQL\...` nếu chưa có. File `.ini` của chúng được điền thông tin cơ sở dữ liệu cục bộ (mật khẩu ngẫu nhiên cho user `mu`, lưu ở `MuServer\DB\db-credentials.txt`; đừng chia sẻ file này).
- Cơ sở dữ liệu nằm ở `MuServer\DB\data`, MariaDB ở `MuServer\DB\mariadb`. Nó chỉ lắng nghe `127.0.0.1:3307`.

## Cổng
| Thành phần | Cổng |
|---|---|
| MariaDB | 3307 (chỉ nội bộ) |
| DataServer | 55980 |
| JoinServer | 55990 |
| ConnectServer | 44405 |
| GameServer | 55901 |

Người chơi ở máy khác chỉ cần TCP 44405 và 55901. Hãy đưa họ thư mục `Client` được tạo **sau khi** bạn đặt IP.

## Xử lý sự cố
- *Cổng đang bị chương trình khác dùng*: có một server khác đang chạy. Hãy tắt nó trước.
- *Thiếu Visual C++ Redistributable*: cài bản x86 từ link ở trên.
- *main.exe đang chạy*: đóng game trước khi Tạo lại Client.
- *main.exe / Main.dll không có ở Client lẫn Encoder\Client*: chép chúng từ gói Encoder gốc vào `Encoder\Client`.
- Kẹt ở màn hình chọn server: IP trong `ServerList.dat` / `MainInfo.ini` không khớp máy bạn. Sửa IP rồi bấm **Tạo lại Client**.
- Khi nhờ hỗ trợ, hãy gửi file `manager.log`.

## Tùy biến
- Ngôn ngữ: ô chọn ở góc phải trên (English / Tiếng Việt).
- Giao diện: đặt `logo.png` (vuông; nền trong suốt hoặc nền đen đặc), `header.png` (ảnh ngang, tỉ lệ khoảng 10:1, hình nằm bên phải) và `icon.ico` vào `Assets\`.

## Giấy phép và ghi công
Giấy phép MIT, xem `LICENSE`. Các thành phần bên thứ ba: xem `THIRD-PARTY-NOTICES.md`.
Mã nguồn server và client: MuEmu 0.97k của Kayito (https://github.com/nicomuratona/MuEmu-0.97k-kayito).
Đây là công cụ không chính thức. Mọi nhãn hiệu thuộc về chủ sở hữu tương ứng; công cụ không liên kết với bất kỳ nhà phát hành game nào.
