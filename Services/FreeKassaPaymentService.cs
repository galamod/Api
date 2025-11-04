using System.Globalization;
using System.Web;
using Api.FreeKassa;

namespace Api.Services
{
    /// <summary>
    /// Расширенный сервис для работы с FreeKassa с поддержкой всех параметров
    /// </summary>
    public interface IFreeKassaPaymentService
    {
        string GeneratePaymentUrl(string orderId, decimal amount, string? email = null, string? description = null);
        bool VerifyWebhookSignature(string merchantId, string amount, string orderId, string sign);
    }

    public class FreeKassaPaymentService : IFreeKassaPaymentService
    {
        private readonly FreeKassaOptions _options;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FreeKassaPaymentService> _logger;

        public FreeKassaPaymentService(
            FreeKassaOptions options,
            IConfiguration configuration,
            ILogger<FreeKassaPaymentService> logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _configuration = configuration;
            _logger = logger;
        }

        public string GeneratePaymentUrl(string orderId, decimal amount, string? email = null, string? description = null)
        {
            // Используем актуальный URL FreeKassa
            const string baseUrl = "https://pay.freekassa.net/";
            
            var currency = _configuration["FreeKassa:Currency"] ?? "RUB";
            
            // Форматируем сумму с двумя знаками после запятой
            var amountStr = amount.ToString("F2", CultureInfo.InvariantCulture);

            // Формула подписи для генерации ссылки: MD5(m:oa:secret1:o)
            // НЕ включаем currency в подпись для генерации ссылки!
            var signatureString = $"{_options.MerchantId}:{amountStr}:{_options.Secret1}:{orderId}";
            var signature = MD5Helper.Create(signatureString);

            _logger.LogInformation("=== FreeKassa Payment URL Generation ===");
            _logger.LogInformation("Merchant ID: {MerchantId}", _options.MerchantId);
            _logger.LogInformation("Amount: {Amount}", amountStr);
            _logger.LogInformation("Currency: {Currency}", currency);
            _logger.LogInformation("Order ID: {OrderId}", orderId);
            _logger.LogInformation("Signature formula: m:oa:secret1:o");
            _logger.LogInformation("Signature string: {SigStr}", signatureString);
            _logger.LogInformation("Signature (MD5): {Sig}", signature);

            // Формируем параметры запроса
            var queryParams = new List<string>
            {
                $"m={_options.MerchantId}",
                $"oa={amountStr}",
                $"o={orderId}",
                $"s={signature}",
                $"currency={currency}"
            };

            // Добавляем опциональные параметры
            if (!string.IsNullOrEmpty(email) && email.Contains("@"))
            {
                queryParams.Add($"em={HttpUtility.UrlEncode(email)}");
            }

            queryParams.Add("lang=ru");

            if (!string.IsNullOrEmpty(description))
            {
                queryParams.Add($"us_order_desc={HttpUtility.UrlEncode(description)}");
            }

            var queryString = string.Join("&", queryParams);
            var paymentUrl = $"{baseUrl}?{queryString}";

            _logger.LogInformation("Generated payment URL: {Url}", paymentUrl);
            _logger.LogInformation("===================================");

            return paymentUrl;
        }

        public bool VerifyWebhookSignature(string merchantId, string amount, string orderId, string sign)
        {
            // Формула подписи для webhook: MD5(m:oa:secret2:o)
            var signatureString = $"{merchantId}:{amount}:{_options.Secret2}:{orderId}";
            var expectedSign = MD5Helper.Create(signatureString);

            _logger.LogInformation("=== FreeKassa Webhook Signature Verification ===");
            _logger.LogInformation("Order ID: {OrderId}", orderId);
            _logger.LogInformation("Merchant ID: {MerchantId}", merchantId);
            _logger.LogInformation("Amount: {Amount}", amount);
            _logger.LogInformation("Signature formula: m:oa:secret2:o");
            _logger.LogInformation("Signature string: {SigStr}", signatureString);
            _logger.LogInformation("Expected signature: {Expected}", expectedSign);
            _logger.LogInformation("Received signature: {Received}", sign);

            var isValid = string.Equals(expectedSign, sign, StringComparison.OrdinalIgnoreCase);
            
            _logger.LogInformation("Signature valid: {IsValid}", isValid);
            _logger.LogInformation("===================================");

            return isValid;
        }
    }
}
