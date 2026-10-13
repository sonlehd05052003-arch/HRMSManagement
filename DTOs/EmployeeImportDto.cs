namespace HRManagement.DTOs
{
    public class EmployeeImportResultDto
    {
        /// <summary>True when the file was only checked</summary>
        public bool DryRun { get; set; }
        public int TotalRows { get; set; }
        public int ValidRows { get; set; }
        /// <summary>Employees added; 0 for a check or when any row has an error</summary>
        public int Imported { get; set; }
        public List<EmployeeImportRowErrorDto> Errors { get; set; } = new();
    }

    public class EmployeeImportRowErrorDto
    {
        /// <summary>Row number in the Excel sheet</summary>
        public int Row { get; set; }
        public string? EmployeeCode { get; set; }
        public string? Email { get; set; }
        public List<string> Messages { get; set; } = new();
    }
}
