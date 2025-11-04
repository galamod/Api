using Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Api.Services
{
    public class PaymentCheckBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PaymentCheckBackgroundService> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5); // Проверка каждые 5 минут
        private readonly TimeSpan _paymentTimeout = TimeSpan.FromHours(24); // Отменяем платежи старше 24 часов

        public PaymentCheckBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<PaymentCheckBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Payment Check Background Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckPendingPayments(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Payment Check Background Service");
                }

                // Ждём до следующей проверки
                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("Payment Check Background Service stopped");
        }

        private async Task CheckPendingPayments(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var freeKassaService = scope.ServiceProvider.GetRequiredService<IFreeKassaPaymentService>();
            var licenseService = scope.ServiceProvider.GetRequiredService<ILicenseService>();

            // Получаем все платежи со статусом Pending, которые младше 24 часов
            var pendingPayments = await context.Payments
                .Where(p => p.Status == PaymentStatus.Pending)
                .Where(p => p.CreatedAt > DateTime.UtcNow.Add(-_paymentTimeout))
                .ToListAsync(cancellationToken);

            _logger.LogInformation($"Found {pendingPayments.Count} pending payments to check");

            foreach (var payment in pendingPayments)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    // Проверяем статус через API FreeKassa
                    var checkResult = await freeKassaService.CheckPaymentStatusAsync(payment.OrderId);

                    if (checkResult.IsPaid)
                    {
                        _logger.LogInformation($"Payment {payment.OrderId} is paid, activating license");

                        // Обновляем статус платежа
                        payment.Status = PaymentStatus.Paid;
                        payment.PaidAt = DateTime.UtcNow;

                        // Активируем лицензию
                        try
                        {
                            var license = await licenseService.CreateAndActivateLicenseAsync(
                                payment.UserId,
                                payment.ApplicationName,
                                payment.PlanIndex
                            );

                            _logger.LogInformation($"License {license.Key} activated for payment {payment.OrderId}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error activating license for payment {payment.OrderId}");
                            payment.Status = PaymentStatus.Failed;
                        }

                        await context.SaveChangesAsync(cancellationToken);
                    }
                    else if (checkResult.Status == "error")
                    {
                        _logger.LogWarning($"Error checking payment {payment.OrderId}: {checkResult.ErrorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing payment {payment.OrderId}");
                }

                // Небольшая задержка между проверками
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }

            // Отменяем старые платежи
            var expiredPayments = await context.Payments
                .Where(p => p.Status == PaymentStatus.Pending)
                .Where(p => p.CreatedAt <= DateTime.UtcNow.Add(-_paymentTimeout))
                .ToListAsync(cancellationToken);

            foreach (var payment in expiredPayments)
            {
                payment.Status = PaymentStatus.Cancelled;
                _logger.LogInformation($"Payment {payment.OrderId} expired and cancelled");
            }

            if (expiredPayments.Any())
            {
                await context.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
