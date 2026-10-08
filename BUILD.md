# Building MMServer Manager from source

You only need Windows: the build uses the C# compiler that comes with .NET Framework 4.

1. Put `Program.cs` and `build.bat` in one folder. Optional: `Assets\icon.ico` (it becomes the icon of the exe).
2. Run `build.bat`. It creates `MMServerManager.exe` in the same folder.
3. Name and version: edit `Product` and `Version` in the `Credits` class at the top of `Program.cs`.

The code is written in C# 5 on purpose, so no Visual Studio is needed.

# Tự build từ mã nguồn

Chỉ cần Windows: `build.bat` dùng trình biên dịch C# có sẵn trong .NET Framework 4.

1. Đặt `Program.cs` và `build.bat` chung một thư mục. Tùy chọn: `Assets\icon.ico` (thành icon của file exe).
2. Chạy `build.bat`, nó tạo `MMServerManager.exe` trong cùng thư mục.
3. Tên và phiên bản: sửa `Product` và `Version` trong lớp `Credits` ở đầu `Program.cs`.

Mã được viết theo C# 5 có chủ đích để không cần Visual Studio.
