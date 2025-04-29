using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Api
{
    [Route("licenses")]
    [ApiController]
    public class LicensesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public LicensesController(AppDbContext context)
        {
            _context = context;
        }

        [Authorize]
        [HttpGet("all")]
        public async Task<IActionResult> GetAllLicenses()
        {
            var licenses = await _context.Licenses
                .Select(l => new
                {
                    l.Id,
                    l.Key,
                    l.ApplicationName,
                    l.UserId,
                    l.CreatedAt,
                    l.ExpirationDate
                })
                .ToListAsync();

            return Ok(licenses);
        }

        [Authorize]
        [HttpPost("assign")]
        public async Task<IActionResult> AssignLicense(AddLicense model)
        {
            var user = await _context.Users.FindAsync(model.UserId);
            if (user == null)
                return NotFound("User not found");

            var existing = await _context.Licenses.AnyAsync(l => l.Key == model.Key);
            if (existing)
                return BadRequest("License key already in use");

            var license = new License
            {
                Key = model.Key,
                ApplicationName = model.ApplicationName,
                UserId = user.Id,
                ExpirationDate = model.ExpirationDate
            };

            _context.Licenses.Add(license);
            await _context.SaveChangesAsync();
            return Ok();
        }

        [Authorize]
        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetUserLicenses(Guid userId)
        {
            var licenses = await _context.Licenses
                .Where(l => l.UserId == userId)
                .Select(l => new
                {
                    l.Key,
                    l.ApplicationName,
                    l.CreatedAt,
                    l.ExpirationDate
                })
                .ToListAsync();

            return Ok(licenses);
        }

        [Authorize]
        [HttpGet("check")]
        public async Task<IActionResult> CheckLicense(Guid userId, string appName)
        {
            var hasLicense = await _context.Licenses.AnyAsync(l =>
                l.UserId == userId &&
                l.ApplicationName == appName &&
                (l.ExpirationDate == null || l.ExpirationDate > DateTime.UtcNow));

            return Ok(new { hasLicense });
        }

        [Authorize]
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateLicense(Guid id, UpdateLicense model)
        {
            var license = await _context.Licenses.FindAsync(id);
            if (license == null)
                return NotFound("License not found");

            license.ApplicationName = model.ApplicationName;
            license.ExpirationDate = model.ExpirationDate;

            await _context.SaveChangesAsync();
            return Ok(new
            {
                license.Id,
                license.ApplicationName,
                license.ExpirationDate
            });
        }

        [Authorize]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteLicense(Guid id)
        {
            var license = await _context.Licenses.FindAsync(id);
            if (license == null)
                return NotFound("License not found");

            _context.Licenses.Remove(license);
            await _context.SaveChangesAsync();
            return Ok("License deleted");
        }

    }

    public class AddLicense
    {
        [Required]
        public Guid UserId { get; set; }

        [Required]
        public string Key { get; set; }

        [Required]
        public string ApplicationName { get; set; }

        public DateTime? ExpirationDate { get; set; }
    }

    public class UpdateLicense
    {
        [Required]
        public string ApplicationName { get; set; }

        public DateTime? ExpirationDate { get; set; }
    }

    public class License
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Key { get; set; }  // уникальный ключ
        public string ApplicationName { get; set; }  // Название приложения
        public Guid UserId { get; set; }
        public User User { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ExpirationDate { get; set; } // Можно использовать для ограниченных по времени ключей
    }
}
