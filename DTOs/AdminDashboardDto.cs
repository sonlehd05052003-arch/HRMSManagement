using System;
using System.Collections.Generic;

namespace HRManagement.DTOs
{
    public class AdminDashboardDto
    {
        public StatisticsDto Statistics { get; set; } = new();
        public List<RecentActivityDto> RecentActivities { get; set; } = new();
        public List<NotificationDto> Alerts { get; set; } = new();
        public List<ScheduledTaskDto> ScheduledTasks { get; set; } = new();
    }

    public class StatisticsDto
    {
        public int TotalUsers { get; set; }
        public int ActiveUsers { get; set; }
        public int NewUsers { get; set; }
        public int TotalEmployees { get; set; }
        public int TotalDepartments { get; set; }
        public int TotalLeaveRequests { get; set; }
        public int PendingApprovals { get; set; }
        public double AttendanceRate { get; set; }
        public double OvertimeHours { get; set; }
        public string SystemUptime { get; set; } = "99.9%";
    }

    public class RecentActivityDto
    {
        public string Description { get; set; } = null!;
        public DateTime Timestamp { get; set; }
    }

    public class NotificationDto
    {
        public string Message { get; set; } = null!;
        public string Level { get; set; } = "Info"; // Info, Warning, Error
    }

    public class ScheduledTaskDto
    {
        public string Name { get; set; } = null!;
        public string Status { get; set; } = null!; // Completed, Pending, Running
    }
}
