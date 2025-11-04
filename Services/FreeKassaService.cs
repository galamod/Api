using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace Api.Services
{
    public interface IFreeKassaService
    {
        string GeneratePaymentUrl(string orderId, decimal amount, string email, string description);
        bool VerifySignature(string merchantId, string amount, string secretWord2, string orderId, string sign);
    }

    public class FreeKassaService : IFreeKassaService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<FreeKassaService> _logger;

        public FreeKassaService(IConfiguration configuration, ILogger<FreeKassaService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public string GeneratePaymentUrl(string orderId, decimal amount, string email, string description)
        {
            var merchantId = (_configuration["FreeKassa:MerchantId"] ?? string.Empty).Trim();
            var secretWord1 = (_configuration["FreeKassa:SecretWord1"] ?? string.Empty).Trim();
            var currency = (_configuration["FreeKassa:Currency"] ?? "RUB").Trim();
            var baseUrl = (_configuration["FreeKassa:BaseUrl"] ?? "https://pay.freekassa.net/").TrimEnd('/') + "/";

            if (string.IsNullOrEmpty(merchantId) || string.IsNullOrEmpty(secretWord1))
            {
                throw new InvalidOperationException("FreeKassa credentials are not configured");
            }

            // Форматируем сумму инвариантно (всегда точка и 2 знака)
            var amountStr = amount.ToString("F2", CultureInfo.InvariantCulture);

            // Вариант 1 (Классический, чаще используется): MD5(m:oa:secret_word_1:o)
            var signatureStringClassic = $"{merchantId}:{amountStr}:{secretWord1}:{orderId}";
            var signatureClassic = ComputeMD5(signatureStringClassic);

            // Вариант 2 (некоторым магазинам требуется валюта): MD5(m:oa:secret_word_1:o:currency)
            var signatureStringWithCurrency = $"{merchantId}:{amountStr}:{secretWord1}:{orderId}:{currency}";
            var signatureWithCurrency = ComputeMD5(signatureStringWithCurrency);

            // По умолчанию используем классический вариант (без currency) — совпадает с примерами поддержки
            var signature = signatureClassic;
            var usedSignatureFormula = "m:oa:secret_word_1:o";

            _logger.LogInformation("=== FreeKassa Payment URL Generation ===");
            _logger.LogInformation("Order ID: {OrderId}", orderId);
            _logger.LogInformation("Merchant ID: {MerchantId}", merchantId);
            _logger.LogInformation("Amount: {Amount}", amountStr);
            _logger.LogInformation("Currency: {Currency}", currency);
            _logger.LogInformation("Signature formula (classic): {Formula}", "m:oa:secret_word_1:o");
            _logger.LogInformation("Signature string (classic): {SigStr}", signatureStringClassic);
            _logger.LogInformation("Signature (classic MD5): {Sig}", signatureClassic);
            _logger.LogInformation("Signature formula (with currency): {Formula}", "m:oa:secret_word_1:o:currency");
            _logger.LogInformation("Signature string (with currency): {SigStr}", signatureStringWithCurrency);
            _logger.LogInformation("Signature (with currency MD5): {Sig}", signatureWithCurrency);
            _logger.LogInformation("Using signature formula: {Used}", usedSignatureFormula);

            var queryParams = new List<string>
            {
                $"m={merchantId}",
                $"oa={amountStr}",
                $"o={orderId}",
                $"s={signature}",
                $"currency={currency}"
            };

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

        public bool VerifySignature(string merchantId, string amount, string secretWord2, string orderId, string sign)
        {
            if (string.IsNullOrEmpty(secretWord2))
            {
                _logger.LogError("SecretWord2 is not configured");
                return false;
            }

            var currency = (_configuration["FreeKassa:Currency"] ?? "RUB").Trim();

            // Инвариантное представление суммы на случай, если провайдер шлёт с точкой
            // (но в вебхуке мы не знаем формат, поэтому используем как пришло и дублируем вариант с инвариантом)
            var amountInvariant = amount;

            // Вариант 1: MD5(m:oa:secret_word_2:o) — классический
            var signatureString = $"{merchantId}:{amount}:{secretWord2}:{orderId}";
            var expectedSignature = ComputeMD5(signatureString);

            // Вариант 1b: классический, но с инвариантной суммой
            var signatureStringInvariant = $"{merchantId}:{amountInvariant}:{secretWord2}:{orderId}";
            var expectedSignatureInvariant = ComputeMD5(signatureStringInvariant);

            // Вариант 2: MD5(m:oa:secret_word_2:o:currency)
            var signatureStringWithCurrency = $"{merchantId}:{amount}:{secretWord2}:{orderId}:{currency}";
            var expectedSignatureWithCurrency = ComputeMD5(signatureStringWithCurrency);

            _logger.LogInformation("Verifying signature for order {OrderId}", orderId);
            _logger.LogInformation("Expected classic: {Sig}", expectedSignature);
            _logger.LogInformation("Expected classic (invariant amount): {Sig}", expectedSignatureInvariant);
            _logger.LogInformation("Expected with currency: {Sig}", expectedSignatureWithCurrency);
            _logger.LogInformation("Received signature: {Sign}", sign);

            var isValid = string.Equals(expectedSignature, sign, StringComparison.OrdinalIgnoreCase)
                          || string.Equals(expectedSignatureInvariant, sign, StringComparison.OrdinalIgnoreCase)
                          || string.Equals(expectedSignatureWithCurrency, sign, StringComparison.OrdinalIgnoreCase);

            if (!isValid)
            {
                _logger.LogWarning("Signature mismatch for order {OrderId}", orderId);
            }

            return isValid;
        }

        private static string ComputeMD5(string input)
        {
            using var md5 = MD5.Create();
            var inputBytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = md5.ComputeHash(inputBytes);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
    }
}
