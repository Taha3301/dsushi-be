using System.Threading.Tasks;

namespace SushiBE.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(string to, string subject, string body);
    }
}