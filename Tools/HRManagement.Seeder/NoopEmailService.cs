using HRManagement.Services.Emails;

namespace HRManagement.Seeder
{
    /// <summary>CompanyService emails each new admin; generated companies have fake addresses, so nothing is sent.</summary>
    public class NoopEmailService : IEmailService
    {
        public Task SendAsync(string to, string subject, string body) => Task.CompletedTask;

        public Task SendWithAttachmentAsync(string to, string subject, string body, byte[] fileBytes, string fileName, string contentType)
            => Task.CompletedTask;
    }
}
