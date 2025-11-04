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

        private string MaskSecret(string? secret)
        {
            if (string.IsNullOrEmpty(secret) || secret.Length < 4)
                return "***";
            
            return $"{secret.Substring(0, 2)}...{secret.Substring(secret.Length - 2)}";
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
            _logger.LogInformation("Secret1 (masked): {Secret}", MaskSecret(secret1Raw));
            _logger.LogInformation("Secret1 length: {Length}", secret1Raw?.Length ?? 0);
            _logger.LogInformation("Secret1 raw bytes: {Bytes}", string.Join(",", System.Text.Encoding.UTF8.GetBytes(secret1Raw ?? "").Take(5)));

            // Пробуем разные формулы подписи (FreeKassa может использовать разные варианты)
            
            // Вариант 1: MD5(m:oa:secret1:o) - классическая формула
            var sig1String = $"{_options.MerchantId}:{amountStr}:{secret1Raw}:{orderId}";
            var sig1 = MD5Helper.Create(sig1String);

            // Вариант 2: MD5(m:oa:secret1:currency:o) - с валютой в середине
            var sig2String = $"{_options.MerchantId}:{amountStr}:{secret1Raw}:{currency}:{orderId}";
            var sig2 = MD5Helper.Create(sig2String);

            // Вариант 3: MD5(m:oa:secret1:o:currency) - валюта в конце
            var sig3String = $"{_options.MerchantId}:{amountStr}:{secret1Raw}:{orderId}:{currency}";
            var sig3 = MD5Helper.Create(sig3String);

            _logger.LogInformation("Trying 3 signature formulas:");
            _logger.LogInformation("Formula 1: m:oa:secret1:o");
            _logger.LogInformation("  MD5: {Sig}", sig1);
            
            _logger.LogInformation("Formula 2: m:oa:secret1:currency:o");
            _logger.LogInformation("  MD5: {Sig}", sig2);
            
            _logger.LogInformation("Formula 3: m:oa:secret1:o:currency");
            _logger.LogInformation("  MD5: {Sig}", sig3);

            // По умолчанию используем формулу 2 (с currency в середине) - это наиболее распространенный вариант
            var signature = sig2;
            var usedFormula = "m:oa:secret1:currency:o";

            _logger.LogInformation("Using formula: {Formula}", usedFormula);
            _logger.LogInformation("Selected signature: {Sig}", signature);

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
            
            // Генерируем тестовые URL для всех трёх формул
            var testUrl1 = $"{baseUrl}?m={_options.MerchantId}&oa={amountStr}&o={orderId}&s={sig1}&currency={currency}";
            var testUrl2 = $"{baseUrl}?m={_options.MerchantId}&oa={amountStr}&o={orderId}&s={sig2}&currency={currency}";
            var testUrl3 = $"{baseUrl}?m={_options.MerchantId}&oa={amountStr}&o={orderId}&s={sig3}&currency={currency}";
            
            _logger.LogWarning("===================================");
            _logger.LogWarning("?? If signature is invalid, try these URLs manually:");
            _logger.LogWarning("Formula 1 URL: {Url}", testUrl1);
            _logger.LogWarning("Formula 2 URL: {Url}", testUrl2);
            _logger.LogWarning("Formula 3 URL: {Url}", testUrl3);
            _logger.LogWarning("===================================");

            return paymentUrl;
        }

        public bool VerifyWebhookSignature(string merchantId, string amount, string orderId, string sign)
        {
            // Получаем секрет напрямую из конфигурации
            var secret2Raw = _configuration["FreeKassa:SecretWord2"] ?? _options.Secret2;

            _logger.LogInformation("=== FreeKassa Webhook Signature Verification ===");
            _logger.LogInformation("Order ID: {OrderId}", orderId);
            _logger.LogInformation("Merchant ID: {MerchantId}", merchantId);
            _logger.LogInformation("Amount: {Amount}", amount);
            _logger.LogInformation("Secret2 (masked): {Secret}", MaskSecret(secret2Raw));
            _logger.LogInformation("Received signature: {Received}", sign);

            // Пробуем разные формулы для webhook
            
            // Вариант 1: MD5(m:oa:secret2:o)
            var sig1String = $"{merchantId}:{amount}:{secret2Raw}:{orderId}";
            var sig1 = MD5Helper.Create(sig1String);

            // Вариант 2: MD5(m:oa:secret2:currency:o)
            var currency = _configuration["FreeKassa:Currency"] ?? "RUB";
            var sig2String = $"{merchantId}:{amount}:{secret2Raw}:{currency}:{orderId}";
            var sig2 = MD5Helper.Create(sig2String);

            _logger.LogInformation("Expected signature (formula 1 - m:oa:secret2:o): {Sig}", sig1);
            _logger.LogInformation("Expected signature (formula 2 - m:oa:secret2:currency:o): {Sig}", sig2);

            var isValid = string.Equals(sig1, sign, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(sig2, sign, StringComparison.OrdinalIgnoreCase);
            
            _logger.LogInformation("Signature valid: {IsValid}", isValid);
            _logger.LogInformation("===================================");

            return isValid;
        }
    }
}
