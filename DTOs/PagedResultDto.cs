namespace HRManagement.DTOs
{
    /// <summary>One page of a list plus the total number of matching rows, for server-side paging.</summary>
    public class PagedResultDto<T>
    {
        public List<T> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}
