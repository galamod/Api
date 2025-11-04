using System;
using System.Security.Cryptography;
using System.Text;

// ТЕСТОВЫЙ СКРИПТ ДЛЯ ПРОВЕРКИ ПОДПИСИ FREEKASSA
// Запустите этот скрипт отдельно для проверки подписи

class FreeKassaSignatureTest
{
    static void Main()
    {
        Console.WriteLine("=== FreeKassa Signature Test ===\n");

        // ЗАМЕНИТЕ THESE VALUES НА СВОИ!
        string merchantId = "66885";
        string amount = "1500.00";
        string orderId = "ORDER_test123";
        string secret1 = "Vi4JtRTdjNDYLF%";  // ?? Скопируйте ТОЧНО из appsettings.json
        string currency = "RUB";

        Console.WriteLine($"Merchant ID: {merchantId}");
        Console.WriteLine($"Amount: {amount}");
        Console.WriteLine($"Order ID: {orderId}");
        Console.WriteLine($"Currency: {currency}");
        Console.WriteLine($"Secret1: {secret1.Substring(0, 2)}...{secret1.Substring(secret1.Length - 2)}\n");

        // Генерируем все возможные подписи
        var formulas = new[]
        {
            ($"{merchantId}:{amount}:{secret1}:{orderId}", "m:oa:secret1:o"),
            ($"{merchantId}:{amount}:{secret1}:{currency}:{orderId}", "m:oa:secret1:currency:o"),
            ($"{merchantId}:{amount}:{secret1}:{orderId}:{currency}", "m:oa:secret1:o:currency"),
        };

        Console.WriteLine("Testing ALL possible signature formulas:\n");

        int index = 1;
        foreach (var (signatureString, formula) in formulas)
        {
            var md5 = ComputeMD5(signatureString);
            var url = $"https://pay.freekassa.net/?m={merchantId}&oa={amount}&o={orderId}&s={md5}&currency={currency}";
            
            Console.WriteLine($"--- Formula {index}: {formula} ---");
            Console.WriteLine($"String: {signatureString}");
            Console.WriteLine($"MD5: {md5}");
            Console.WriteLine($"URL: {url}");
            Console.WriteLine();
            
            index++;
        }

        Console.WriteLine("\n=== ACTION REQUIRED ===");
        Console.WriteLine("1. Copy each URL above");
        Console.WriteLine("2. Open in browser");
        Console.WriteLine("3. Find which one opens FreeKassa payment page");
        Console.WriteLine("4. Report back which formula worked!");
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    static string ComputeMD5(string input)
    {
        using var md5 = MD5.Create();
        var inputBytes = Encoding.ASCII.GetBytes(input);
        var hashBytes = md5.ComputeHash(inputBytes);
        var sb = new StringBuilder();
        for (int i = 0; i < hashBytes.Length; i++)
        {
            sb.Append(hashBytes[i].ToString("X2"));
        }
        return sb.ToString();
    }
}
