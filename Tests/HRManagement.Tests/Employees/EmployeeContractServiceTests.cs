using HRManagement.DTOs;
using HRManagement.Models;
using HRManagement.Services.Employees;
using HRManagement.Services.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Tests.Employees
{
    public class EmployeeContractServiceTests
    {
        private readonly DbContextOptions<HrmsDbContext> _options = new DbContextOptionsBuilder<HrmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        private readonly DateOnly _today = DateOnly.FromDateTime(AppTime.Today);
        private readonly int _employeeId;

        public EmployeeContractServiceTests()
        {
            using var db = NewContext();
            var employee = new Employee
            {
                EmployeeCode = "E1", FirstName = "An", LastName = "Nguyễn", FullName = "An Nguyễn", Email = "an@test",
                EmploymentStatus = "Active", EmploymentType = "Full-Time", JoinDate = _today.AddYears(-1)
            };
            db.Employees.Add(employee);
            db.SaveChanges();
            _employeeId = employee.EmployeeId;
        }

        private HrmsDbContext NewContext()
        {
            var tenant = new TenantContext(new HttpContextAccessor());
            tenant.Use(1);
            return new HrmsDbContext(_options, tenant);
        }

        private EmployeeContractWriteDto Contract(string type, DateOnly start, DateOnly? end) => new()
        {
            EmployeeId = _employeeId, ContractType = type, StartDate = start, EndDate = end, SalaryAmount = 15_000_000
        };

        [Fact]
        public async Task Numbers_are_generated_per_year_in_sequence()
        {
            using var db = NewContext();
            var service = new EmployeeContractService(db);

            var first = await service.CreateAsync(Contract(ContractTypes.Probation, new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 28)), null);
            var second = await service.CreateAsync(Contract(ContractTypes.FixedTerm, new DateOnly(2026, 3, 1), new DateOnly(2027, 2, 28)), null);

            Assert.Equal("HD-2026-0001", first.ContractNumber);
            Assert.Equal("HD-2026-0002", second.ContractNumber);
        }

        [Fact]
        public async Task Two_active_contracts_cannot_overlap()
        {
            using var db = NewContext();
            var service = new EmployeeContractService(db);
            await service.CreateAsync(Contract(ContractTypes.FixedTerm, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), null);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(Contract(ContractTypes.FixedTerm, new DateOnly(2026, 6, 1), new DateOnly(2027, 5, 31)), null));
        }

        [Fact]
        public async Task A_terminated_contract_no_longer_blocks_a_new_one()
        {
            using var db = NewContext();
            var service = new EmployeeContractService(db);
            var old = await service.CreateAsync(Contract(ContractTypes.Indefinite, new DateOnly(2025, 1, 1), null), null);

            var terminated = await service.TerminateAsync(old.ContractId, new DateOnly(2026, 5, 31));
            var next = await service.CreateAsync(Contract(ContractTypes.FixedTerm, new DateOnly(2026, 6, 1), new DateOnly(2027, 5, 31)), null);

            Assert.Equal(ContractStatuses.Terminated, terminated.Status);
            Assert.Equal(new DateOnly(2026, 5, 31), terminated.EndDate);
            Assert.Equal(ContractStatuses.Active, next.Status);
        }

        [Fact]
        public async Task Fixed_term_needs_an_end_date_and_indefinite_drops_it()
        {
            using var db = NewContext();
            var service = new EmployeeContractService(db);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.CreateAsync(Contract(ContractTypes.FixedTerm, _today, null), null));
            var indefinite = await service.CreateAsync(Contract(ContractTypes.Indefinite, _today, _today.AddYears(1)), null);

            Assert.Null(indefinite.EndDate);
        }

        [Fact]
        public async Task An_active_contract_past_its_end_date_shows_as_expired_and_expiring_ones_are_listed()
        {
            using var db = NewContext();
            var service = new EmployeeContractService(db);
            await service.CreateAsync(Contract(ContractTypes.Probation, _today.AddMonths(-3), _today.AddDays(-1)), null);
            await service.CreateAsync(Contract(ContractTypes.FixedTerm, _today, _today.AddDays(20)), null);

            var all = await service.GetByEmployeeAsync(_employeeId);
            var expiring = await service.GetExpiringAsync(30);

            Assert.Equal(ContractStatuses.Expired, all.Single(c => c.ContractType == ContractTypes.Probation).Status);
            var soon = Assert.Single(expiring);
            Assert.Equal(20, soon.DaysRemaining);
        }
    }
}
