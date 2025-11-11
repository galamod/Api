using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Api.Controllers
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

            if (string.IsNullOrEmpty(dto.Key))
                return BadRequest("Ключ лицензии не может быть пустым.");

            try
            {
                var userId = Guid.Parse(User.FindFirstValue("id")!);

                var license = await _context.Licenses.FirstOrDefaultAsync(x => x.Key == dto.Key);
                if (license == null)
                    return NotFound("Лицензия не найдена");

                // ❗ Убираем запрет, если лицензия уже активирована этим же пользователем
                if (license.UserId != null && license.UserId != userId)
                    return BadRequest("Лицензия уже активирована другим пользователем.");

                // Привязываем к пользователю (если ещё не была привязана)
                if (license.UserId == null)
                {
                    license.UserId = userId;
                    await _context.SaveChangesAsync();
                }

                return Ok("Лицензия активирована успешно.");
            }
            catch (Exception ex)
            {
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

        /// <summary>
        /// Получить детальную информацию о лицензии пользователя для конкретного приложения
        /// </summary>
        [Authorize]
        [HttpGet("info")]
        public async Task<IActionResult> GetLicenseInfo([FromQuery] string application)
        {
            if (string.IsNullOrWhiteSpace(application))
                return BadRequest("Название приложения не может быть пустым");

            var userId = Guid.Parse(User.FindFirstValue("id")!);
            var now = DateTime.UtcNow;

            // Ищем активную лицензию для данного приложения или универсальную лицензию
            var license = await _context.Licenses
                .Where(x => x.UserId == userId &&
                           (x.ApplicationName == null || x.ApplicationName == application))
                .OrderByDescending(x => x.CreatedAt) // Берём самую новую
                .FirstOrDefaultAsync();

            if (license == null)
            {
                return Ok(new LicenseInfoDto
                {
                    HasLicense = false,
                    ApplicationName = application,
                    Status = "Нет активной лицензии"
                });
            }

            // Проверяем, не истекла ли лицензия
            var isExpired = license.ExpirationDate.HasValue && license.ExpirationDate.Value <= now;
            var isActive = !isExpired;

            // Вычисляем количество оставшихся дней
            int? daysRemaining = null;
            if (license.ExpirationDate.HasValue && !isExpired)
            {
                daysRemaining = (int)(license.ExpirationDate.Value - now).TotalDays;
            }

            // Определяем статус
            string status;
            if (!license.ExpirationDate.HasValue)
            {
                status = "Бессрочная лицензия";
            }
            else if (isExpired)
            {
                status = "Лицензия истекла";
            }
            else if (daysRemaining <= 7)
            {
                status = $"Истекает через {daysRemaining} дн.";
            }
            else if (daysRemaining <= 30)
            {
                status = $"Активна ({daysRemaining} дн. осталось)";
            }
            else
            {
                status = "Активна";
            }

            return Ok(new LicenseInfoDto
            {
                HasLicense = true,
                LicenseKey = license.Key,
                ApplicationName = license.ApplicationName ?? "Все приложения",
                StartDate = license.CreatedAt,
                ExpirationDate = license.ExpirationDate,
                IsActive = isActive,
                IsExpired = isExpired,
                DaysRemaining = daysRemaining,
                Status = status
            });
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
