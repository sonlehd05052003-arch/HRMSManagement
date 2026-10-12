namespace HRManagement.Seeder
{
    /// <summary>Vietnamese-looking names and org structure for generated companies.</summary>
    public static class FakeData
    {
        public static readonly string[] FamilyNames =
        {
            "Nguyễn", "Trần", "Lê", "Phạm", "Hoàng", "Huỳnh", "Phan", "Vũ", "Võ", "Đặng",
            "Bùi", "Đỗ", "Hồ", "Ngô", "Dương", "Lý", "Trương", "Đinh", "Mai", "Tạ"
        };

        public static readonly string[] MaleMiddleNames = { "Văn", "Hữu", "Đức", "Minh", "Quốc", "Thành", "Công", "Gia" };
        public static readonly string[] FemaleMiddleNames = { "Thị", "Ngọc", "Thu", "Thanh", "Mỹ", "Bảo", "Kim", "Phương" };

        public static readonly string[] MaleGivenNames =
        {
            "An", "Bình", "Cường", "Dũng", "Đạt", "Hải", "Hiếu", "Hùng", "Khánh", "Long",
            "Minh", "Nam", "Phong", "Quân", "Sơn", "Tài", "Thắng", "Toàn", "Trung", "Tuấn", "Việt", "Vinh"
        };

        public static readonly string[] FemaleGivenNames =
        {
            "Anh", "Chi", "Dung", "Giang", "Hà", "Hạnh", "Hoa", "Hương", "Lan", "Linh",
            "Mai", "My", "Nga", "Ngân", "Nhung", "Oanh", "Phương", "Quỳnh", "Thảo", "Trang", "Uyên", "Yến"
        };

        public static readonly (string Code, string Name)[] Departments =
        {
            ("KD", "Phòng Kinh doanh"), ("KT", "Phòng Kỹ thuật"), ("NS", "Phòng Nhân sự"), ("TC", "Phòng Tài chính - Kế toán"),
            ("MKT", "Phòng Marketing"), ("VH", "Phòng Vận hành"), ("CSKH", "Phòng Chăm sóc khách hàng"), ("PC", "Phòng Pháp chế")
        };

        /// <summary>Index into this array is used by the seeder: 0 director, 1 head, 2 deputy, 3+ staff.</summary>
        public static readonly (string Code, string Name, int Level, bool IsTopLevel)[] Positions =
        {
            ("GD", "Giám đốc", 1, true), ("TP", "Trưởng phòng", 2, false), ("PP", "Phó phòng", 3, false),
            ("CV", "Chuyên viên", 4, false), ("NV", "Nhân viên", 5, false), ("TTS", "Thực tập sinh", 6, false)
        };

        public static readonly string[] Cities = { "Hà Nội", "TP. Hồ Chí Minh", "Đà Nẵng", "Hải Phòng", "Cần Thơ", "Huế", "Nha Trang" };

        public static readonly string[] LeaveReasons =
        {
            "Việc gia đình", "Khám sức khỏe", "Nghỉ mát cùng gia đình", "Về quê", "Ốm", "Đưa con đi học", "Giải quyết việc cá nhân"
        };

        public static readonly string[] OvertimeTasks =
        {
            "Hoàn thành báo cáo tháng", "Hỗ trợ triển khai hệ thống", "Xử lý đơn hàng tồn", "Chuẩn bị tài liệu cho khách hàng", "Kiểm kê cuối kỳ"
        };
    }
}
