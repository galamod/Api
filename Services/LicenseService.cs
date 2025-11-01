using Microsoft.EntityFrameworkCore;

namespace Api.Services
{
    public interface ILicenseService
    {
        Task<License> CreateAndActivateLicenseAsync(Guid userId, string applicationName, int planIndex);
        Task<bool> HasActiveLicenseAsync(Guid userId, string applicationName);
    }

    public class LicenseService : ILicenseService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<LicenseService> _logger;

        public LicenseService(AppDbContext context, ILogger<LicenseService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<License> CreateAndActivateLicenseAsync(Guid userId, string applicationName, int planIndex)
        {
            // ќпредел€ем срок действи€ в зависимости от планаindex
            DateTime? expirationDate = planIndex switch
            {
                0 => DateTime.UtcNow.AddDays(7),      // 1 недел€
                1 => DateTime.UtcNow.AddMonths(1),    // 1 мес€ц
                2 => DateTime.UtcNow.AddMonths(3),    // 3 мес€ца
                3 => DateTime.UtcNow.AddMonths(6),    // 6 мес€цев
                4 => DateTime.UtcNow.AddYears(1),     // 1 год
                5 => null,                            // Ќавсегда
                _ => throw new ArgumentException($"Invalid plan index: {planIndex}")
            };

            // √енерируем уникальный ключ лицензии
            var licenseKey = GenerateLicenseKey();

            var license = new License
            {
                Key = licenseKey,
                UserId = userId,
                ApplicationName = applicationName,
                CreatedAt = DateTime.UtcNow,
                ExpirationDate = expirationDate
            };

            _context.Licenses.Add(license);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"Created license {licenseKey} for user {userId}, app: {applicationName}, plan: {planIndex}, expires: {expirationDate?.ToString() ?? "Never"}");

            return license;
        }

        public async Task<bool> HasActiveLicenseAsync(Guid userId, string applicationName)
        {
            var now = DateTime.UtcNow;
            return await _context.Licenses.AnyAsync(l =>
                l.UserId == userId &&
                (l.ApplicationName == null || l.ApplicationName == applicationName) &&
                (l.ExpirationDate == null || l.ExpirationDate > now));
        }

        private static string GenerateLicenseKey()
        {
            // √енерируем ключ формата: XXXX-XXXX-XXXX-XXXX
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            var parts = new string[4];

            for (int i = 0; i < 4; i++)
            {
                parts[i] = new string(Enumerable.Range(0, 4)
                    .Select(_ => chars[random.Next(chars.Length)])
                    .ToArray());
            }

            return string.Join("-", parts);
        }
    }
}
