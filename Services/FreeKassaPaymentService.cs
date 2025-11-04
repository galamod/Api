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

        private string MaskSecret(string? secret)
        {
            if (string.IsNullOrEmpty(secret) || secret.Length < 4)
                return "***";
            
            return $"{secret.Substring(0, 2)}...{secret.Substring(secret.Length - 2)}";
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
            _logger.LogInformation("Secret1 (masked): {Secret}", MaskSecret(secret1Raw));
            _logger.LogInformation("Secret1 length: {Length}", secret1Raw?.Length ?? 0);

            // Основная формула согласно документации: MD5(shopId:amount:secret:order_id)
            var signatureString = $"{_options.MerchantId}:{amountStr}:{secret1Raw}:{orderId}";
            
            // Генерируем в обоих регистрах
            var signatureUpper = MD5Helper.Create(signatureString); // Верхний регистр (X2)
            var signatureLower = ComputeMD5Lower(signatureString);  // Нижний регистр (x2)

            _logger.LogInformation("Signature string: {Str}", signatureString);
            _logger.LogInformation("MD5 (UPPERCASE): {Sig}", signatureUpper);
            _logger.LogInformation("MD5 (lowercase): {Sig}", signatureLower);

            // По умолчанию используем нижний регистр (более распространённый)
            var signature = signatureLower;

            _logger.LogInformation("Using lowercase MD5 by default");

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
            
            // Генерируем тестовые URL для обоих регистров
            var urlLower = $"{baseUrl}?m={_options.MerchantId}&oa={amountStr}&o={orderId}&s={signatureLower}&currency={currency}";
            var urlUpper = $"{baseUrl}?m={_options.MerchantId}&oa={amountStr}&o={orderId}&s={signatureUpper}&currency={currency}";
            
            _logger.LogWarning("==================================.");
            _logger.LogWarning("?? CRITICAL: Try BOTH URLs (lowercase vs UPPERCASE MD5):");
            _logger.LogWarning("URL with lowercase MD5: {Url}", urlLower);
            _logger.LogWarning("URL with UPPERCASE MD5: {Url}", urlUpper);
            _logger.LogWarning("===================================");

            // Также генерируем варианты с другими формулами
            var sig2String = $"{_options.MerchantId}:{amountStr}:{secret1Raw}:{currency}:{orderId}";
            var sig2Lower = ComputeMD5Lower(sig2String);
            var sig2Upper = MD5Helper.Create(sig2String);

            var url2Lower = $"{baseUrl}?m={_options.MerchantId}&oa={amountStr}&o={orderId}&s={sig2Lower}&currency={currency}";
            var url2Upper = $"{baseUrl}?m={_options.MerchantId}&oa={amountStr}&o={orderId}&s={sig2Upper}&currency={currency}";

            _logger.LogWarning("Alternative formula with currency:");
            _logger.LogWarning("URL with lowercase MD5 (formula 2): {Url}", url2Lower);
            _logger.LogWarning("URL with UPPERCASE MD5 (formula 2): {Url}", url2Upper);
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

            // Пробуем основную формулу в обоих регистрах
            var signatureString = $"{merchantId}:{amount}:{secret2Raw}:{orderId}";
            
            var sigUpper = MD5Helper.Create(signatureString);
            var sigLower = ComputeMD5Lower(signatureString);

            _logger.LogInformation("Expected signature (UPPERCASE): {Sig}", sigUpper);
            _logger.LogInformation("Expected signature (lowercase): {Sig}", sigLower);

            // Также пробуем с currency
            var currency = _configuration["FreeKassa:Currency"] ?? "RUB";
            var sig2String = $"{merchantId}:{amount}:{secret2Raw}:{currency}:{orderId}";
            var sig2Upper = MD5Helper.Create(sig2String);
            var sig2Lower = ComputeMD5Lower(sig2String);

            _logger.LogInformation("Expected signature with currency (UPPERCASE): {Sig}", sig2Upper);
            _logger.LogInformation("Expected signature with currency (lowercase): {Sig}", sig2Lower);

            var isValid = string.Equals(sigUpper, sign, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(sigLower, sign, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(sig2Upper, sign, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(sig2Lower, sign, StringComparison.OrdinalIgnoreCase);
            
            _logger.LogInformation("Signature valid: {IsValid}", isValid);
            _logger.LogInformation("===================================");

            return isValid;
        }
    }
}
