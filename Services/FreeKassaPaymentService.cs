using System.Globalization;
using System.Web;
using System.Security.Cryptography;
using System.Text;
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

        // Альтернативный MD5 в нижнем регистре
        private string ComputeMD5Lower(string input)
        {
            using var md5 = MD5.Create();
            var inputBytes = Encoding.ASCII.GetBytes(input);
            var hashBytes = md5.ComputeHash(inputBytes);
            var sb = new StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("x2")); // x2 = lowercase
            }
            return sb.ToString();
        }

        public string GeneratePaymentUrl(string orderId, decimal amount, string? email = null, string? description = null)
        {
            // Используем актуальный URL FreeKassa
            const string baseUrl = "https://pay.freekassa.net/";
            
            var currency = _configuration["FreeKassa:Currency"] ?? "RUB";
            
            // Форматируем сумму с двумя знаками после запятой
            var amountStr = amount.ToString("F2", CultureInfo.InvariantCulture);

            // Получаем секрет напрямую из конфигурации
            var secret1Raw = _configuration["FreeKassa:SecretWord1"] ?? _options.Secret1;

            _logger.LogInformation("=== FreeKassa Payment URL Generation ===");
            _logger.LogInformation("Merchant ID: {MerchantId}", _options.MerchantId);
            _logger.LogInformation("Amount: {Amount}", amountStr);
            _logger.LogInformation("Currency: {Currency}", currency);
            _logger.LogInformation("Order ID: {OrderId}", orderId);

            // ✅ ПРАВИЛЬНАЯ ФОРМУЛА: MD5(m:oa:secret:currency:o) в lowercase
            var signatureString = $"{_options.MerchantId}:{amountStr}:{secret1Raw}:{currency}:{orderId}";
            var signature = ComputeMD5Lower(signatureString);

            _logger.LogInformation("Signature formula: m:oa:secret:currency:o");
            _logger.LogInformation("Signature string: {Str}", signatureString);
            _logger.LogInformation("MD5 (lowercase): {Sig}", signature);

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
            _logger.LogInformation("✅ Using WORKING formula: m:oa:secret:currency:o (lowercase MD5)");
            _logger.LogInformation("===================================");

            return paymentUrl;
        }

        public bool VerifyWebhookSignature(string merchantId, string amount, string orderId, string sign)
        {
            // Получаем секрет напрямую из конфигурации
            var secret2Raw = _configuration["FreeKassa:SecretWord2"] ?? _options.Secret2;
            var currency = _configuration["FreeKassa:Currency"] ?? "RUB";

            _logger.LogInformation("=== FreeKassa Webhook Signature Verification ===");
            _logger.LogInformation("Order ID: {OrderId}", orderId);
            _logger.LogInformation("Merchant ID: {MerchantId}", merchantId);
            _logger.LogInformation("Amount: {Amount}", amount);
            _logger.LogInformation("Received signature: {Received}", sign);

            // ✅ Используем правильную формулу: m:oa:secret:currency:o (lowercase)
            var signatureString = $"{merchantId}:{amount}:{secret2Raw}:{currency}:{orderId}";
            var expectedSignature = ComputeMD5Lower(signatureString);

            _logger.LogInformation("Expected signature formula: m:oa:secret2:currency:o");
            _logger.LogInformation("Expected signature (lowercase): {Sig}", expectedSignature);

            // Также проверяем вариант без currency (на всякий случай)
            var signatureStringAlt = $"{merchantId}:{amount}:{secret2Raw}:{orderId}";
            var expectedSignatureAlt = ComputeMD5Lower(signatureStringAlt);
            _logger.LogInformation("Alternative expected (without currency): {Sig}", expectedSignatureAlt);

            var isValid = string.Equals(expectedSignature, sign, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(expectedSignatureAlt, sign, StringComparison.OrdinalIgnoreCase);
            
            _logger.LogInformation("Signature valid: {IsValid}", isValid);
            _logger.LogInformation("===================================");

            return isValid;
        }
    }
}
