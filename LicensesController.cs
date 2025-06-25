using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        [HttpPost("activate")]
        public async Task<IActionResult> Activate([FromBody] ActivateLicenseDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (string.IsNullOrEmpty(dto.LicenseKey))
                return BadRequest("Ключ лицензии не может быть пустым.");

            try
            {
                var userId = Guid.Parse(User.FindFirstValue("id")!);

                var license = await _context.Licenses.FirstOrDefaultAsync(x => x.Key == dto.LicenseKey);
                if (license == null)
                    return NotFound("Лицензия не найдена");

                if (license.UserId != null)
                    return BadRequest("Лицензия уже активирована");

                license.UserId = userId;
                await _context.SaveChangesAsync();

                return Ok("Лицензия успешно активирована");
            }
            catch (Exception ex)
            {
                // Логируем и возвращаем 500
                // logger.LogError(ex, "Ошибка активации лицензии");
                return StatusCode(500, $"Ошибка сервера: {ex.Message}");
            }
        }

        [Authorize]
        [HttpGet("my")]
        public async Task<IActionResult> GetMyLicenses()
        {
            var userId = Guid.Parse(User.FindFirstValue("id")!);
            var licenses = await _context.Licenses
                .Where(x => x.UserId == userId)
                .ToListAsync();
            return Ok(licenses);
        }

        [Authorize]
        [HttpGet("check")]
        public async Task<IActionResult> CheckAccess([FromQuery] string application)
        {
            var userId = Guid.Parse(User.FindFirstValue("id")!);

            var now = DateTime.UtcNow;
            var hasLicense = await _context.Licenses.AnyAsync(x =>
                x.UserId == userId &&
                (x.ApplicationName == null || x.ApplicationName == application) &&
                (x.ExpirationDate == null || x.ExpirationDate > now));

            return Ok(new { accessGranted = hasLicense });
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var licenses = await _context.Licenses
                .Include(x => x.User)
                .Select(x => new LicenseDto
                {
                    Id = x.Id,
                    Key = x.Key,
                    ApplicationName = x.ApplicationName,
                    ExpirationDate = x.ExpirationDate,
                    UserId = x.UserId,
                    UserUsername = x.User != null ? x.User.Username : null
                })
                .ToListAsync();

            return Ok(licenses);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("admin/create-for-user")]
        public async Task<IActionResult> CreateForUser([FromBody] CreateLicenseForUserDto dto)
        {
            if (dto == null)
                return BadRequest("DTO не передан");

            var user = await _context.Users.FindAsync(dto.UserId);
            if (user == null)
                return NotFound("Пользователь не найден");

            var license = new License
            {
                Key = Guid.NewGuid().ToString("N").ToUpper(),
                ApplicationName = dto.ApplicationName,
                ExpirationDate = dto.ExpirationDate?.ToUniversalTime(), // <- здесь
                UserId = dto.UserId,
                CreatedAt = DateTime.UtcNow // если есть поле CreatedAt
            };

            _context.Licenses.Add(license);
            await _context.SaveChangesAsync();

            return Ok(license);
        }

        [Authorize(Roles = "Admin")]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, GenerateLicenseDto dto)
        {
            var license = await _context.Licenses.FindAsync(id);
            if (license == null) return NotFound();

            license.ApplicationName = dto.ApplicationName;
            license.ExpirationDate = dto.ExpirationDate;
            await _context.SaveChangesAsync();

            return Ok(license);
        }

        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var license = await _context.Licenses.FindAsync(id);
            if (license == null) return NotFound();

            _context.Licenses.Remove(license);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [Authorize(Roles = "Admin")]
        [HttpGet("with-licenses")]
        public async Task<IActionResult> GetUsersWithLicenses()
        {
            var users = await _context.Users
                .Include(u => u.Licenses)
                .Select(u => new {
                    u.Id,
                    u.FirstName,
                    u.LastName,
                    u.Username,
                    Licenses = u.Licenses.Select(l => new {
                        l.Id,
                        l.Key,
                        l.ApplicationName,
                        l.ExpirationDate
                    })
                })
                .ToListAsync();

            return Ok(users);
        }
    }
}
