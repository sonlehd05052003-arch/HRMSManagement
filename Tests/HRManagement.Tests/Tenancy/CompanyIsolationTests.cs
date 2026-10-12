using HRManagement.Models;
using HRManagement.Services.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.Tests.Tenancy
{
    /// <summary>
    /// HrmsDbContext must never let one company read another's rows, and stamps new rows with the current company.
    /// </summary>
    public class CompanyIsolationTests
    {
        private readonly DbContextOptions<HrmsDbContext> _options = new DbContextOptionsBuilder<HrmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        private HrmsDbContext ContextFor(int? companyId)
        {
            var tenant = new TenantContext(new HttpContextAccessor());
            tenant.Use(companyId);
            return new HrmsDbContext(_options, tenant);
        }

        private static Department NewDepartment(string code) =>
            new() { DepartmentCode = code, DepartmentName = code, IsActive = true };

        private void Seed()
        {
            using (var companyA = ContextFor(1))
            {
                companyA.Departments.AddRange(NewDepartment("A-1"), NewDepartment("A-2"));
                companyA.SaveChanges();
            }
            using (var companyB = ContextFor(2))
            {
                companyB.Departments.Add(NewDepartment("B-1"));
                companyB.SaveChanges();
            }
        }

        [Fact]
        public void A_company_only_sees_its_own_rows()
        {
            Seed();

            using var companyA = ContextFor(1);
            using var companyB = ContextFor(2);

            Assert.Equal(new[] { "A-1", "A-2" }, companyA.Departments.Select(d => d.DepartmentCode).OrderBy(c => c).ToArray());
            Assert.Equal(new[] { "B-1" }, companyB.Departments.Select(d => d.DepartmentCode).ToArray());
        }

        [Fact]
        public void Looking_up_another_companys_row_by_id_finds_nothing()
        {
            Seed();
            int foreignId;
            using (var companyB = ContextFor(2))
                foreignId = companyB.Departments.Single().DepartmentId;

            using var companyA = ContextFor(1);

            Assert.Null(companyA.Departments.FirstOrDefault(d => d.DepartmentId == foreignId));
        }

        [Fact]
        public void Without_a_company_nothing_is_visible()
        {
            Seed();

            using var anonymous = ContextFor(null);

            Assert.Empty(anonymous.Departments);
        }

        [Fact]
        public void New_rows_get_the_current_company()
        {
            using (var companyB = ContextFor(2))
            {
                companyB.Departments.Add(NewDepartment("B-new"));
                companyB.SaveChanges();
            }

            using var all = ContextFor(2);
            Assert.Equal(2, all.Departments.IgnoreQueryFilters().Single(d => d.DepartmentCode == "B-new").CompanyId);
        }

        [Fact]
        public void Saving_a_new_row_without_a_company_is_refused()
        {
            using var anonymous = ContextFor(null);
            anonymous.Departments.Add(NewDepartment("orphan"));

            Assert.Throws<InvalidOperationException>(() => anonymous.SaveChanges());
        }
    }
}
