using System.Data;
using HRManagement.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Seeder
{
    /// <summary>
    /// SqlBulkCopy for the big tables (attendance, shift assignments), with table and column names read from the
    /// EF model so they always match the schema. Identity and computed columns are left to SQL Server.
    /// </summary>
    public static class BulkInserter
    {
        public static async Task InsertAsync<T>(HrmsDbContext db, IReadOnlyCollection<T> rows) where T : class
        {
            if (rows.Count == 0)
                return;

            var entityType = db.Model.FindEntityType(typeof(T))
                ?? throw new InvalidOperationException($"{typeof(T).Name} is not part of the model.");
            var table = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());

            var columns = entityType.GetProperties()
                .Where(p => p.PropertyInfo != null)
                .Where(p => p.GetComputedColumnSql(table) == null)
                .Where(p => !(p.IsPrimaryKey() && p.ValueGenerated == ValueGenerated.OnAdd))
                .Select(p => (Property: p, Column: p.GetColumnName(table)!))
                .ToList();

            using var data = new DataTable();
            foreach (var (property, column) in columns)
                data.Columns.Add(column, StoreType(property.ClrType));

            foreach (var row in rows)
            {
                var values = new object[columns.Count];
                for (var i = 0; i < columns.Count; i++)
                    values[i] = ToStore(columns[i].Property.PropertyInfo!.GetValue(row));
                data.Rows.Add(values);
            }

            var connection = (SqlConnection)db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            // CheckConstraints keeps foreign keys trusted; without it SQL Server marks them as not verified
            using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, null)
            {
                DestinationTableName = table.Schema == null ? $"[{table.Name}]" : $"[{table.Schema}].[{table.Name}]",
                BatchSize = 5000,
                BulkCopyTimeout = 0
            };
            foreach (var (_, column) in columns)
                bulk.ColumnMappings.Add(column, column);

            await bulk.WriteToServerAsync(data);
        }

        private static Type StoreType(Type clrType)
        {
            var type = Nullable.GetUnderlyingType(clrType) ?? clrType;
            if (type == typeof(DateOnly)) return typeof(DateTime);
            if (type == typeof(TimeOnly)) return typeof(TimeSpan);
            return type;
        }

        private static object ToStore(object? value) => value switch
        {
            null => DBNull.Value,
            DateOnly date => date.ToDateTime(TimeOnly.MinValue),
            TimeOnly time => time.ToTimeSpan(),
            _ => value
        };
    }
}
