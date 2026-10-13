using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using HRManagement.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Services.Audits
{
    /// <summary>
    /// Writes an AuditLogs row for every insert, update or delete of the sensitive tables below (pay, attendance, contracts,
    /// leave balances, employees, roles, settings), with who did it, from where, and the values before and after.
    /// Services do not have to remember to call anything.
    /// </summary>
    public class AuditSaveChangesInterceptor : SaveChangesInterceptor
    {
        private static readonly HashSet<Type> AuditedTypes = new()
        {
            typeof(PayrollPeriod), typeof(PayrollRecord), typeof(PayrollAllowance), typeof(PayrollDeduction),
            typeof(PayrollPolicy), typeof(AttendanceRecord), typeof(LeaveBalance), typeof(Employee),
            typeof(Role), typeof(SystemSetting), typeof(EmployeeContract)
        };

        private sealed record PendingAudit(EntityEntry Entry, int CompanyId, string Action, string? OldValues, string? NewValues);

        // Entries are collected before the save and written after it, once inserted rows have their generated ids
        private readonly ConditionalWeakTable<DbContext, List<PendingAudit>> _pending = new();
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AuditSaveChangesInterceptor> _logger;

        public AuditSaveChangesInterceptor(IHttpContextAccessor httpContextAccessor, ILogger<AuditSaveChangesInterceptor> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Collect(eventData.Context);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Collect(eventData.Context);
            return ValueTask.FromResult(result);
        }

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            if (TakeLogs(eventData.Context) is { } context)
            {
                try { context.SaveChanges(); }
                catch (Exception ex) { _logger.LogError(ex, "Could not write audit log"); }
            }
            return result;
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (TakeLogs(eventData.Context) is { } context)
            {
                // The change itself is already saved; a failed audit write must not report the request as failed
                try { await context.SaveChangesAsync(cancellationToken); }
                catch (Exception ex) { _logger.LogError(ex, "Could not write audit log"); }
            }
            return result;
        }

        public override void SaveChangesFailed(DbContextErrorEventData eventData) => _pending.Remove(eventData.Context!);

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            _pending.Remove(eventData.Context!);
            return Task.CompletedTask;
        }

        private void Collect(DbContext? context)
        {
            if (context == null)
                return;

            var audits = new List<PendingAudit>();
            foreach (var entry in context.ChangeTracker.Entries())
            {
                if (!AuditedTypes.Contains(entry.Metadata.ClrType) || entry.Entity is not ICompanyScoped scoped)
                    continue;

                switch (entry.State)
                {
                    case EntityState.Added:
                        // Values are read after the save, when the generated id is known
                        audits.Add(new PendingAudit(entry, scoped.CompanyId, "INSERT", null, null));
                        break;
                    case EntityState.Deleted:
                        audits.Add(new PendingAudit(entry, scoped.CompanyId, "DELETE", Serialize(entry, p => p.OriginalValue), null));
                        break;
                    case EntityState.Modified:
                        var changed = entry.Properties.Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)).ToList();
                        if (changed.Count == 0)
                            continue;
                        audits.Add(new PendingAudit(entry, scoped.CompanyId, "UPDATE",
                            Serialize(changed, p => p.OriginalValue), Serialize(changed, p => p.CurrentValue)));
                        break;
                }
            }

            if (audits.Count > 0)
                _pending.AddOrUpdate(context, audits);
        }

        /// <summary>Adds the collected rows to the context and returns it, or null when there is nothing to write.</summary>
        private DbContext? TakeLogs(DbContext? context)
        {
            if (context == null || !_pending.TryGetValue(context, out var audits))
                return null;
            _pending.Remove(context);

            var http = _httpContextAccessor.HttpContext;
            int? userId = int.TryParse(http?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
            var ip = http?.Connection.RemoteIpAddress?.ToString();
            var now = AppTime.Now;

            foreach (var audit in audits)
            {
                context.Add(new AuditLog
                {
                    CompanyId = audit.CompanyId,
                    TableName = audit.Entry.Metadata.GetTableName() ?? audit.Entry.Metadata.ClrType.Name,
                    Action = audit.Action,
                    RecordId = PrimaryKey(audit.Entry),
                    UserId = userId,
                    OldValues = audit.OldValues,
                    NewValues = audit.Action == "INSERT" ? Serialize(audit.Entry, p => p.CurrentValue) : audit.NewValues,
                    ActionDate = now,
                    Ipaddress = ip
                });
            }
            return context;
        }

        private static int PrimaryKey(EntityEntry entry)
        {
            var key = entry.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault();
            return key != null && entry.Property(key.Name).CurrentValue is int value ? value : 0;
        }

        private static string Serialize(EntityEntry entry, Func<PropertyEntry, object?> value) =>
            Serialize(entry.Properties, value);

        private static string Serialize(IEnumerable<PropertyEntry> properties, Func<PropertyEntry, object?> value) =>
            JsonSerializer.Serialize(properties
                .Where(p => p.Metadata.ClrType != typeof(byte[]))
                .ToDictionary(p => p.Metadata.Name, value));
    }
}
