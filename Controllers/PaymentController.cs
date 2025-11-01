using Api.Models;
using Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentController : ControllerBase
    {
        private readonly IFreeKassaService _freeKassaService;
        private readonly ILicenseService _licenseService;
        private readonly AppDbContext _context;
        private readonly ILogger<PaymentController> _logger;
        private readonly IConfiguration _configuration;

        public PaymentController(
            IFreeKassaService freeKassaService,
            ILicenseService licenseService,
            AppDbContext context,
            ILogger<PaymentController> logger,
            IConfiguration configuration)
        {
            _freeKassaService = freeKassaService;
            _licenseService = licenseService;
            _context = context;
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// ������� ������ � ���������� URL ��� ������
        /// </summary>
        [Authorize]
        [HttpPost("create")]
        public async Task<IActionResult> CreatePayment([FromBody] CreatePaymentRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var userIdClaim = User.FindFirstValue("id");
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                {
                    return Unauthorized("Invalid user ID");
                }

                // �������� email ������������ �� ��
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound("User not found");
                }

                var userEmail = user.Username + "@example.com"; // ���������� username ��� ������ ��� email

                // ���������� ���������� ID ������
                var orderId = $"ORDER_{Guid.NewGuid():N}";

                // ������� ������ � ������� � ��
                var payment = new Payment
                {
                    UserId = userId,
                    ApplicationName = request.ApplicationName,
                    PlanIndex = request.PlanIndex,
                    Amount = request.Amount,
                    OrderId = orderId,
                    Status = PaymentStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Payments.Add(payment);
                await _context.SaveChangesAsync();

                // ���������� URL ��� ������
                var paymentUrl = _freeKassaService.GeneratePaymentUrl(
                    orderId,
                    request.Amount,
                    userEmail,
                    $"{request.ApplicationName} - Plan {request.PlanIndex}"
                );

                _logger.LogInformation($"Created payment {orderId} for user {userId}, app: {request.ApplicationName}, plan: {request.PlanIndex}, amount: {request.Amount}");

                return Ok(new PaymentUrlResponse
                {
                    PaymentUrl = paymentUrl,
                    OrderId = orderId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating payment");
                return StatusCode(500, new { message = "������ ��� �������� �������", error = ex.Message });
            }
        }

        /// <summary>
        /// Webhook ��� ��������� ����������� �� FreeKassa
        /// </summary>
        [AllowAnonymous]
        [HttpPost("webhook")]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> Webhook([FromForm] string MERCHANT_ID, [FromForm] string AMOUNT, [FromForm] string MERCHANT_ORDER_ID, [FromForm] string SIGN)
        {
            try
            {
                _logger.LogInformation($"Received webhook for order {MERCHANT_ORDER_ID}, amount: {AMOUNT}, sign: {SIGN}");

                var secretWord2 = _configuration["FreeKassa:SecretWord2"];

                if (string.IsNullOrEmpty(secretWord2))
                {
                    _logger.LogError("FreeKassa:SecretWord2 is not configured");
                    return BadRequest("Configuration error");
                }

                // ��������� �������
                if (!_freeKassaService.VerifySignature(MERCHANT_ID, AMOUNT, secretWord2, MERCHANT_ORDER_ID, SIGN))
                {
                    _logger.LogWarning($"Invalid signature for order {MERCHANT_ORDER_ID}");
                    return BadRequest("Invalid signature");
                }

                // ������� ������ � ��
                var payment = await _context.Payments
                    .FirstOrDefaultAsync(p => p.OrderId == MERCHANT_ORDER_ID);

                if (payment == null)
                {
                    _logger.LogWarning($"Payment {MERCHANT_ORDER_ID} not found");
                    return NotFound("Payment not found");
                }

                // ���������, �� ��� �� ������ ��� ���������
                if (payment.Status == PaymentStatus.Paid)
                {
                    _logger.LogInformation($"Payment {MERCHANT_ORDER_ID} already processed");
                    return Ok("YES");
                }

                // ��������� ������ �������
                payment.Status = PaymentStatus.Paid;
                payment.PaidAt = DateTime.UtcNow;

                // ������� � ���������� ��������
                try
                {
                    var license = await _licenseService.CreateAndActivateLicenseAsync(
                        payment.UserId,
                        payment.ApplicationName,
                        payment.PlanIndex
                    );

                    _logger.LogInformation($"License {license.Key} activated for payment {MERCHANT_ORDER_ID}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error activating license for payment {MERCHANT_ORDER_ID}");
                    // �� ���������� ������ FreeKassa, ����� �� ���� ��������� �������
                    // �� �������� ������ ��� Failed
                    payment.Status = PaymentStatus.Failed;
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Payment {MERCHANT_ORDER_ID} processed successfully");

                // FreeKassa ������� ����� "YES"
                return Ok("YES");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// �������� ������ ������� �� ID ������
        /// </summary>
        [Authorize]
        [HttpGet("status/{orderId}")]
        public async Task<IActionResult> GetPaymentStatus(string orderId)
        {
            try
            {
                var payment = await _context.Payments
                    .FirstOrDefaultAsync(p => p.OrderId == orderId);

                if (payment == null)
                {
                    return NotFound(new { message = "������ �� ������" });
                }

                // ���������, ��� ������������ ����������� ���� ������
                var userIdClaim = User.FindFirstValue("id");
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                {
                    return Unauthorized("Invalid user ID");
                }

                if (payment.UserId != userId)
                {
                    return Forbid();
                }

                return Ok(new PaymentStatusResponse
                {
                    OrderId = payment.OrderId,
                    Status = payment.Status,
                    Amount = payment.Amount,
                    CreatedAt = payment.CreatedAt,
                    PaidAt = payment.PaidAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting payment status for order {orderId}");
                return StatusCode(500, new { message = "������ ��� ��������� ������� �������" });
            }
        }

        /// <summary>
        /// �������� ��� ������� �������� ������������
        /// </summary>
        [Authorize]
        [HttpGet("my")]
        public async Task<IActionResult> GetMyPayments()
        {
            try
            {
                var userIdClaim = User.FindFirstValue("id");
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                {
                    return Unauthorized("Invalid user ID");
                }

                var payments = await _context.Payments
                    .Where(p => p.UserId == userId)
                    .OrderByDescending(p => p.CreatedAt)
                    .Select(p => new PaymentStatusResponse
                    {
                        OrderId = p.OrderId,
                        Status = p.Status,
                        Amount = p.Amount,
                        CreatedAt = p.CreatedAt,
                        PaidAt = p.PaidAt
                    })
                    .ToListAsync();

                return Ok(payments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user payments");
                return StatusCode(500, new { message = "������ ��� ��������� ������ ��������" });
            }
        }

        /// <summary>
        /// ��� �������: �������� ��� �������
        /// </summary>
        [Authorize(Roles = "Admin")]
        [HttpGet("all")]
        public async Task<IActionResult> GetAllPayments()
        {
            try
            {
                var payments = await _context.Payments
                    .Include(p => p.User)
                    .OrderByDescending(p => p.CreatedAt)
                    .Select(p => new
                    {
                        p.Id,
                        p.OrderId,
                        p.UserId,
                        Username = p.User!.Username,
                        p.ApplicationName,
                        p.PlanIndex,
                        p.Amount,
                        p.Status,
                        p.CreatedAt,
                        p.PaidAt
                    })
                    .ToListAsync();

                return Ok(payments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all payments");
                return StatusCode(500, new { message = "������ ��� ��������� ������ ��������" });
            }
        }
    }
}
