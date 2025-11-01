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
            var merchantId = _configuration["FreeKassa:MerchantId"];
            var secretWord1 = _configuration["FreeKassa:SecretWord1"];

            if (string.IsNullOrEmpty(merchantId) || string.IsNullOrEmpty(secretWord1))
            {
                throw new InvalidOperationException("FreeKassa credentials are not configured");
            }

            // Генерируем подпись согласно документации FreeKassa
            // Формула: MD5(m:oa:secret_word_1:o)
            // где m - ID магазина, oa - сумма, secret_word_1 - секретное слово, o - номер заказа
            var signatureString = $"{merchantId}:{amount:F2}:{secretWord1}:{orderId}";
            var signature = ComputeMD5(signatureString);

            _logger.LogInformation($"=== FreeKassa Payment URL Generation ===");
            _logger.LogInformation($"Order ID: {orderId}");
            _logger.LogInformation($"Merchant ID: {merchantId}");
            _logger.LogInformation($"Amount: {amount:F2}");
            _logger.LogInformation($"Secret Word 1 (first 4 chars): {(secretWord1?.Length >= 4 ? secretWord1.Substring(0, 4) : secretWord1)}***");
            _logger.LogInformation($"Signature string: {signatureString}");
            _logger.LogInformation($"Signature (MD5): {signature}");

            // Формируем URL согласно документации FreeKassa
            var baseUrl = "https://pay.fk.money/"; // Официальный URL из документации
            
            // Строим query string согласно документации
            var queryParams = new List<string>
            {
                $"m={merchantId}",        // ID магазина
                $"oa={amount:F2}",        // Сумма заказа
                $"o={orderId}",           // ID заказа
                $"s={signature}",         // Подпись
                "currency=RUB"            // Валюта (обязательный параметр!)
            };

            // Email опционален
            if (!string.IsNullOrEmpty(email) && email.Contains("@"))
            {
                queryParams.Add($"em={HttpUtility.UrlEncode(email)}");
            }

            // Язык интерфейса
            queryParams.Add("lang=ru");

            // Описание заказа (опционально)
            if (!string.IsNullOrEmpty(description))
            {
                queryParams.Add($"us_order_desc={HttpUtility.UrlEncode(description)}");
            }

            var queryString = string.Join("&", queryParams);
            var paymentUrl = $"{baseUrl}?{queryString}";

            _logger.LogInformation($"Generated payment URL: {paymentUrl}");
            _logger.LogInformation($"===================================");

            return paymentUrl;
        }

        public bool VerifySignature(string merchantId, string amount, string secretWord2, string orderId, string sign)
        {
            if (string.IsNullOrEmpty(secretWord2))
            {
                _logger.LogError("SecretWord2 is not configured");
                return false;
            }

            // Вычисляем подпись: MD5(shopId:amount:secret_word_2:order_id)
            var signatureString = $"{merchantId}:{amount}:{secretWord2}:{orderId}";
            var expectedSignature = ComputeMD5(signatureString);

            _logger.LogInformation($"Verifying signature for order {orderId}");
            _logger.LogInformation($"Signature string: {signatureString}");
            _logger.LogInformation($"Expected signature: {expectedSignature}");
            _logger.LogInformation($"Received signature: {sign}");

            var isValid = string.Equals(expectedSignature, sign, StringComparison.OrdinalIgnoreCase);

            if (!isValid)
            {
                _logger.LogWarning($"Signature mismatch for order {orderId}");
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
