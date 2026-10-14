# HRManagement - Hệ Thống Backend Quản Trị Nhân Sự (HRMS API)

Hệ thống API backend cho ứng dụng Quản lý Nhân sự (HRMS), được xây dựng trên nền tảng **ASP.NET Core 8 Web API**. Hệ thống cung cấp đầy đủ các nghiệp vụ quản trị nhân sự từ quản lý thông tin nhân viên, chấm công bằng khuôn mặt (AI Face Verification), quản lý ca làm việc, quy trình duyệt phép/tăng ca, đánh giá hiệu suất, tính lương tự động, xuất báo cáo và phân tích dữ liệu nhân lực.

---

## 🚀 Tính Năng Nổi Bật

1. **Quản Lý Nhân Viên & Hồ Sơ (Employee Management)**
   - CRUD thông tin nhân viên, hợp đồng và hồ sơ đính kèm.
   - Tích hợp **Cloudinary API** để lưu trữ tài liệu và ảnh đại diện an toàn trên đám mây.

2. **Chấm Công Bằng Khuôn Mặt (AI Face Verification)**
   - Đăng ký và xác thực khuôn mặt trực tiếp khi chấm công.
   - Sử dụng mô hình AI **YuNet** (phát hiện khuôn mặt) và **SFace** (trích xuất & đối sánh đặc trưng khuôn mặt) chạy trên **ONNX Runtime** kết hợp với thư viện xử lý ảnh **OpenCV (OpenCvSharp4)**.

3. **Quản Lý Ca Làm Việc & Lịch Phân Ca (Shifts & Assignments)**
   - Định nghĩa các loại ca làm việc linh hoạt (Ca hành chính, ca gãy, ca đêm...).
   - Phân ca linh hoạt cho nhân viên theo ngày hoặc chu kỳ.

4. **Yêu Cầu & Phê Duyệt Đa Cấp (Leaves, Overtimes & Approvals)**
   - Đăng ký nghỉ phép (nghỉ phép có lương, không lương) và tự động tính toán số dư phép (Leave Balance).
   - Đăng ký tăng ca (Overtime) và tích hợp cơ chế tự động gộp giờ tăng ca được duyệt trực tiếp vào dữ liệu chấm công hàng ngày trong bộ nhớ (In-memory Merge).
   - Định cấu hình quy trình phê duyệt đa cấp (Approval Routes) theo phòng ban và cấp bậc quản lý.

5. **Tính Lương Tự Động (Payroll Calculation)**
   - Quản lý các chu kỳ tính lương (Payroll Periods).
   - Tự động tính toán lương dựa trên công thực tế, phụ cấp, giờ tăng ca và thuế thu nhập cá nhân (PIT).
   - Xuất phiếu lương PDF chuyên nghiệp sử dụng **QuestPDF** và xuất bảng lương tổng hợp ra file Excel sử dụng **ClosedXML**.

6. **Đánh Giá Hiệu Suất (Performance Evaluation)**
   - Thiết lập các tiêu chí đánh giá và biểu mẫu mẫu (Evaluation Templates).
   - Tạo các chu kỳ đánh giá (Evaluation Cycles) có giới hạn thời gian (Time-gating).
   - Hỗ trợ quy trình đánh giá 2 chiều: Nhân viên tự đánh giá (Self-Evaluation) và Quản lý đánh giá (Manager Evaluation).

7. **Báo Cáo & Phân Tích (Analytics & Audit Logs)**
   - Bảng phân tích biến động nhân sự, tỷ lệ đi muộn/về sớm và hiệu suất phòng ban.
   - Ghi nhận nhật ký hoạt động (Audit Logs) đảm bảo tính minh bạch và bảo mật.

8. **Bảo Mật Hệ Thống (Security & Session Management)**
   - Xác thực người dùng bằng **JWT Bearer Tokens**.
   - Cơ chế ngăn chặn đăng nhập đồng thời (Concurrent Login Prevention) bằng cách so khớp thời gian đăng nhập gần nhất (`LastLogin`) trong token với cơ sở dữ liệu.

---

## 🛠️ Công Nghệ Sử Dụng

* **Framework chính:** .NET 8.0 (ASP.NET Core Web API)
* **Cơ sở dữ liệu:** Microsoft SQL Server
* **ORM:** Entity Framework Core 8.0 (Code First)
* **Bảo mật & Mã hóa:** BCrypt.Net-Next, JWT (JSON Web Tokens)
* **Trí tuệ nhân tạo (Computer Vision):** ONNX Runtime (`Microsoft.ML.OnnxRuntime`), OpenCV (`OpenCvSharp4` & `OpenCvSharp4.Windows`)
* **Lưu trữ đám mây:** Cloudinary API (`CloudinaryDotNet`)
* **Xuất bản tài liệu:** QuestPDF (PDF generation), ClosedXML (Excel integration)
* **Mapping DTO:** AutoMapper
* **Hẹn giờ & Tác vụ ngầm:** Hosted Services (`HRProcedureBackgroundService`, `PayrollAttendanceReviewService`)
* **Tài liệu API:** Swagger/OpenAPI

---

## 📂 Cấu Trúc Thư Mục Chính

```text
HRManagement/
├── Controllers/            # Điểm tiếp nhận yêu cầu HTTP API
├── DTOs/                   # Lớp đối tượng truyền tải dữ liệu (Data Transfer Objects)
├── Models/                 # Lớp thực thể ánh xạ database (Entity Models)
├── DataAcess/              # Database Context và triển khai Repository Pattern
│   ├── Implementations/    # Hiện thực hóa các phương thức Repository
│   └── Interfaces/         # Các định nghĩa Interface truy xuất dữ liệu
├── Services/               # Lớp xử lý logic nghiệp vụ (Core Business Logic)
│   ├── Attendances/        # Nghiệp vụ chấm công
│   ├── FaceVerifications/  # Xử lý ảnh và mô hình AI nhận diện khuôn mặt
│   ├── Payroll/            # Tính toán lương, thuế và bảo hiểm
│   └── ...                 # Các nghiệp vụ khác (Employees, Leaves, Tasks, Evaluations...)
├── AIModels/               # Nơi lưu trữ file model ONNX (YuNet, SFace)
├── Configuration/          # Cấu hình cài đặt hệ thống (Cloudinary, JWT...)
├── Mappers/                # Định nghĩa các cấu hình AutoMapper Profiles
├── Migrations/             # Quản lý lịch sử thay đổi lược đồ database (EF Migrations)
└── Program.cs              # Khởi tạo và thiết lập Middleware, Dependency Injection
```

---

## 🧠 Thử Thách Kỹ Thuật & Bài Học Kinh Nghiệm (Từ góc nhìn của Junior Developer)

Trong quá trình phát triển hệ thống backend này, tôi đã gặp nhiều khó khăn lớn và đã học hỏi được rất nhiều bài học thực tế vượt ra ngoài sách vở:

### 1. Hiện thực hóa chấm công khuôn mặt bằng C# (AI Integration)
* **Thử thách:** Hầu hết tài liệu và thư viện AI nhận diện khuôn mặt đều sử dụng Python. Khi chuyển dịch sang môi trường .NET Core, tôi phải tự tìm hiểu cách tải mô hình ONNX (`YuNet` và `SFace`) bằng `Microsoft.ML.OnnxRuntime`, xử lý ảnh qua ma trận màu của `OpenCvSharp4` và tính toán khoảng cách cosine của vector đặc trưng 128 chiều.
* **Giải pháp & Bài học:** Tôi đã hiểu được cách chuẩn hóa dữ liệu ảnh đầu vào (resizing, BGR sang RGB), quản lý tài nguyên bộ nhớ Unmanaged Code của OpenCV và thực hiện so khớp vector một cách tối ưu.

### 2. Tối ưu hóa hiệu năng nghiệp vụ lương & chấm công phức tạp
* **Thử thách:** Khi khối lượng dữ liệu chấm công và tăng ca (Overtime) lớn, việc lạm dụng AutoMapper hoặc truy vấn SQL liên tục trong vòng lặp (N+1 query problem) khiến API chạy cực kỳ chậm và tốn tài nguyên.
* **Giải pháp & Bài học:** Tôi đã refactor lại phần `PayrollService`, loại bỏ AutoMapper ở các luồng tính toán cốt lõi, thay thế bằng các phương thức Mapper thủ công tối ưu (helper methods). Tôi cũng chuyển sang dùng cơ chế tải dữ liệu trước (Eager Loading với `Include`) kết hợp xử lý gộp dữ liệu chấm công và tăng ca trong bộ nhớ (In-memory Merge). Kết quả là thời gian phản hồi API giảm đáng kể (từ vài giây xuống dưới 200ms).

### 3. Giải quyết bài toán bảo mật: Chặn đăng nhập đồng thời (Concurrent Login)
* **Thử thách:** Yêu cầu đặt ra là khi một tài khoản đăng nhập ở thiết bị mới, thiết bị cũ phải tự động bị đăng xuất nhằm đảm bảo an toàn thông tin nhân sự.
* **Giải pháp & Bài học:** Tôi đã tìm hiểu sâu hơn về hoạt động của JWT Middleware trong ASP.NET Core. Bằng cách can thiệp vào sự kiện `OnTokenValidated`, tôi so khớp trường `LastLogin` lưu trong Token của client gửi lên với giá trị `LastLogin` thực tế tại cơ sở dữ liệu. Nếu có sự sai lệch (tức là đã có phiên mới hơn được mở), token cũ sẽ bị từ chối ngay tại lớp middleware bảo mật.

---

## ⚙️ Hướng Dẫn Cài Đặt & Chạy Dự Án

### 1. Yêu Cầu Hệ Thống
* Cài đặt **.NET 8.0 SDK** ([Tải về tại đây](https://dotnet.microsoft.com/download/dotnet/8.0))
* Cài đặt **Microsoft SQL Server** (hoặc LocalDB)
* Bộ mô hình ONNX để nhận diện khuôn mặt đặt trong thư mục `AIModels/`:
  - `face_detection_yunet_2023mar.onnx`
  - `face_recognition_sface_2021dec.onnx`

### 2. Cấu Hình Ứng Dụng (`appsettings.json`)
Cập nhật các chuỗi kết nối và thông tin tài khoản bên thứ ba trong file `appsettings.json` tại thư mục gốc của dự án:
```json
{
  "ConnectionStrings": {
    "MyCnn": "server=<Tên_Server_Của_Bạn>;database=HRMS_DB;uid=sa;pwd=<Mật_Khẩu>;TrustServerCertificate=True"
  },
  "Jwt": {
    "Key": "<Khóa_Bảo_Mật_JWT_Đủ_Dài>",
    "Issuer": "https://api.peoplecore.tech",
    "Audience": "https://app.peoplecore.tech"
  },
  "Email": {
    "Smtp": "smtp.gmail.com",
    "Port": "587",
    "Username": "<Email_Của_Bạn>",
    "Password": "<Mật_Khẩu_Ứng_Dụng_Gmail>",
    "From": "HR System <Email_Của_Bạn>"
  },
  "Cloudinary": {
    "CloudName": "<Cloud_Name_Cloudinary>",
    "ApiKey": "<Api_Key>",
    "ApiSecret": "<Api_Secret>",
    "FolderName": "hrms/employee-documents"
  }
}
```

### 3. Cập Nhật Cơ Sở Dữ Liệu
Chạy lệnh migrations để tự động sinh cấu trúc bảng trên SQL Server:
```bash
# Cài đặt công cụ EF Core nếu chưa có
dotnet tool install --global dotnet-ef

# Cập nhật cơ sở dữ liệu
dotnet ef database update
```

Một database mới được tạo đầy đủ bảng và dữ liệu mẫu (quyền, menu, công ty mẫu số 1). Tài khoản SuperAdmin được tạo
khi API khởi động, theo mục `SuperAdmin` trong `appsettings.json`.

> **Database tạo trước ngày 14/10/2026** (đã chạy các migration cũ, tới `AddHolidays`): lịch sử migration đã được gộp
> lại thành `InitialSchema` + `SeedTemplateData`. Chạy một lần script sau trước khi `dotnet ef database update`;
> script chỉ sửa bảng `__EFMigrationsHistory`, không đụng tới dữ liệu:
> ```bash
> sqlcmd -S localhost -E -d HRMS_DB -i Tools/squash-migration-history.sql
> ```

### 4. Khởi Chạy Ứng Dụng
Sử dụng CLI để chạy dự án:
```bash
dotnet run
```
Sau khi khởi chạy thành công, tài liệu API Swagger sẽ có thể truy cập được tại địa chỉ:
`http://localhost:<Cổng_Của_Bạn>/swagger/index.html` (chỉ hiển thị ở môi trường `Development`).
