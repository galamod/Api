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

            // Генерируем подпись: MD5(shopId:amount:secret_word_1:order_id)
            var signatureString = $"{merchantId}:{amount:F2}:{secretWord1}:{orderId}";
            var signature = ComputeMD5(signatureString);

            _logger.LogInformation($"Generating payment URL for order {orderId}. Signature string: {signatureString}");

            // Формируем URL
            var baseUrl = "https://pay.freekassa.net/";
            var parameters = new Dictionary<string, string>
            {
                ["m"] = merchantId,
                ["oa"] = amount.ToString("F2"),
                ["o"] = orderId,
                ["s"] = signature,
                ["em"] = email,
                ["lang"] = "ru"
            };

            // Добавляем description если есть
            if (!string.IsNullOrEmpty(description))
            {
                parameters["us_order_desc"] = description;
            }

            var queryString = string.Join("&", parameters.Select(kvp => 
                $"{kvp.Key}={HttpUtility.UrlEncode(kvp.Value)}"));

            var paymentUrl = $"{baseUrl}?{queryString}";

            _logger.LogInformation($"Generated payment URL: {paymentUrl}");

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
