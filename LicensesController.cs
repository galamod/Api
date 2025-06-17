using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

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
        [HttpGet("my")] // получить лицензии текущего пользователя
        public async Task<IActionResult> GetMyLicenses()
        {
            var userId = User.FindFirstValue("id");
            if (userId == null) return Unauthorized();

            var guid = Guid.Parse(userId);
            var licenses = await _context.Licenses
                .Where(l => l.UserId == guid)
                .Select(l => new
                {
                    l.Id,
                    l.Key,
                    l.Application,
                    l.CreatedAt,
                    l.ExpiresAt
                })
                .ToListAsync();

            return Ok(licenses);
        }

        [Authorize]
        [HttpPost("activate")]
        public async Task<IActionResult> ActivateLicense([FromBody] string key)
        {
            var userId = User.FindFirstValue("id");
            if (userId == null) return Unauthorized();
            var guid = Guid.Parse(userId);

            var license = await _context.Licenses
                .FirstOrDefaultAsync(x => x.Key == key && x.UserId == null);

            if (license == null)
                return BadRequest("Ключ недействителен или уже активирован");

            license.UserId = guid;
            _context.Licenses.Update(license);
            await _context.SaveChangesAsync();

            return Ok("Ключ успешно активирован");
        }

        [Authorize]
        [HttpGet("validate")] // ?app=название
        public async Task<IActionResult> ValidateLicense([FromQuery] string? app)
        {
            var userId = User.FindFirstValue("id");
            if (userId == null) return Unauthorized();
            var guid = Guid.Parse(userId);
            var now = DateTime.UtcNow;

            var isValid = await _context.Licenses.AnyAsync(l =>
                l.UserId == guid &&
                (l.Application == null || l.Application == app) &&
                (l.ExpiresAt == null || l.ExpiresAt > now));

            return Ok(new { valid = isValid });
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var licenses = await _context.Licenses
                .Include(l => l.User)
                .Select(l => new
                {
                    l.Id,
                    l.Key,
                    l.Application,
                    l.CreatedAt,
                    l.ExpiresAt,
                    User = l.User == null ? null : new
                    {
                        l.User.Id,
                        l.User.Username
                    }
                })
                .ToListAsync();

            return Ok(licenses);
        }

        [Authorize]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] License updated)
        {
            var license = await _context.Licenses.FindAsync(id);
            if (license == null) return NotFound();

            license.Application = updated.Application;
            license.ExpiresAt = updated.ExpiresAt;

            await _context.SaveChangesAsync();
            return Ok();
        }

        [Authorize]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var license = await _context.Licenses.FindAsync(id);
            if (license == null) return NotFound();

            _context.Licenses.Remove(license);
            await _context.SaveChangesAsync();
            return Ok();
        }
    }
}
