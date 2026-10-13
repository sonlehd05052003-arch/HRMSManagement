using HRManagement.Models;
using HRManagement.Services.Notifications;
using HRManagement.Services.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Tests.Notifications
{
    public class NotificationServiceTests
    {
        private const int Me = 10, SomeoneElse = 20;

        private readonly DbContextOptions<HrmsDbContext> _options = new DbContextOptionsBuilder<HrmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        private HrmsDbContext NewContext()
        {
            var tenant = new TenantContext(new HttpContextAccessor());
            tenant.Use(1);
            return new HrmsDbContext(_options, tenant);
        }

        private void Seed(int forMe, int forOther)
        {
            using var db = NewContext();
            var service = new NotificationService(db);
            for (var i = 0; i < forMe; i++)
                service.Notify(Me, NotificationTypes.LeaveResult, $"Mine {i}", "m", "LeaveRequest", i);
            for (var i = 0; i < forOther; i++)
                service.Notify(SomeoneElse, NotificationTypes.LeaveApproval, $"Theirs {i}", "m");
            db.SaveChanges();
        }

        [Fact]
        public async Task A_user_only_sees_their_own_notifications_newest_first()
        {
            Seed(forMe: 3, forOther: 2);
            using var db = NewContext();

            var page = await new NotificationService(db).GetMineAsync(Me, unreadOnly: false, page: 1, pageSize: 10);

            Assert.Equal(3, page.Total);
            Assert.All(page.Items, n => Assert.StartsWith("Mine", n.Title));
            Assert.Equal(page.Items.OrderByDescending(n => n.NotificationId).Select(n => n.NotificationId),
                page.Items.Select(n => n.NotificationId));
        }

        [Fact]
        public async Task Paging_returns_the_requested_slice_and_the_full_total()
        {
            Seed(forMe: 12, forOther: 0);
            using var db = NewContext();

            var second = await new NotificationService(db).GetMineAsync(Me, unreadOnly: false, page: 2, pageSize: 5);

            Assert.Equal(12, second.Total);
            Assert.Equal(5, second.Items.Count);
        }

        [Fact]
        public async Task Marking_read_updates_the_unread_count()
        {
            Seed(forMe: 2, forOther: 0);
            using var db = NewContext();
            var service = new NotificationService(db);
            var first = (await service.GetMineAsync(Me, false, 1, 10)).Items[0];

            Assert.True(await service.MarkReadAsync(Me, first.NotificationId));

            Assert.Equal(1, await service.CountUnreadAsync(Me));
            Assert.Single((await service.GetMineAsync(Me, unreadOnly: true, 1, 10)).Items);
        }

        [Fact]
        public async Task A_user_cannot_mark_someone_elses_notification()
        {
            Seed(forMe: 0, forOther: 1);
            using var db = NewContext();
            var service = new NotificationService(db);
            var theirs = (await service.GetMineAsync(SomeoneElse, false, 1, 10)).Items[0];

            Assert.False(await service.MarkReadAsync(Me, theirs.NotificationId));
            Assert.Equal(1, await service.CountUnreadAsync(SomeoneElse));
        }
    }
}
