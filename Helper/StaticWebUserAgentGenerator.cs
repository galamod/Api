using System.Text.RegularExpressions;

namespace Api.Helper
{
    public class StaticWebUserAgentGenerator
    {
        private readonly Random random = new Random();

        private readonly string[] osVersions =
        {
            "Windows NT 10.0; Win64; x64",
            "Windows NT 6.1; Win64; x64",
            "Windows NT 6.1; WOW64",
            "Macintosh; Intel Mac OS X 10_15_7",
            "Linux x86_64"
        };

        // Массив разрешений экранов и диапазонов DPI
        private readonly (int Width, int Height, int MinDpi, int MaxDpi)[] screenResolutions = new (int, int, int, int)[]
        {
            (720, 1280, 200, 300),   // HD
            (1080, 1920, 300, 450),  // Full HD
            (1440, 2560, 400, 600),  // Quad HD
            (1080, 2280, 300, 450),  // FHD+ (18:9)
            (1440, 3040, 400, 600),  // QHD+
            (1080, 2400, 300, 450),  // FHD+ (20:9)
            (1440, 3200, 400, 600),  // WQHD+
            (720, 1520, 200, 300),   // HD+ (18.5:9)
            (1080, 2340, 300, 450),  // FHD+ (19.5:9)
            (1080, 2460, 300, 450)   // FHD+ (19.8:9)
        };

        // Сгенерированный User-Agent (статичный)
        public string GeneratedUserAgent { get; private set; }

        // Сгенерированное разрешение экрана (статичное)
        public (int Width, int Height, int Dpi) GeneratedScreenResolution { get; private set; }

        public StaticWebUserAgentGenerator()
        {
            // Генерация User-Agent и разрешения экрана при создании экземпляра класса
            GeneratedUserAgent = GetRandomWebUserAgent();
            GeneratedScreenResolution = GenerateScreenResolution();
        }

        private string GetRandomWebUserAgent()
        {
            string osVersion = osVersions[random.Next(osVersions.Length)];
            string version = $"{random.Next(100, 200)}.{random.Next(0, 10)}.{random.Next(0, 10)}.{random.Next(0, 10)}";
            string appleVersion = $"{random.Next(500, 600)}.{random.Next(1, 50)}";
            return $"Mozilla/5.0 ({osVersion}) AppleWebKit/{appleVersion} (KHTML, like Gecko) Chrome/{version} Safari/{appleVersion}";
        }

        private (int Width, int Height, int MinDpi, int MaxDpi) GetRandomResolution()
        {
            int index = random.Next(screenResolutions.Length);
            return screenResolutions[index];
        }

        private int GetRandomDpi(int minDpi, int maxDpi)
        {
            return random.Next(minDpi, maxDpi + 1); // Генерируем случайный DPI в пределах заданного диапазона
        }

        private (int Width, int Height, int Dpi) GenerateScreenResolution()
        {
            var resolution = GetRandomResolution();
            int dpi = GetRandomDpi(resolution.MinDpi, resolution.MaxDpi);
            return (resolution.Width, resolution.Height, dpi);
        }

        public string GetPhoneUserAgent(string userAgent)
        {
            string chromeVersion = GetChromeVersion(userAgent);
            var resolution = GeneratedScreenResolution;
            return $"PHONE {resolution.Width} {resolution.Height} 0 2 :chrome {chromeVersion}";
        }

        public string GetDeviceNameAndModel(string userAgent)
        {
            Regex regex = new Regex(@"Chrome/(?<version>\d+\.\d+\.\d+\.\d+)\s");
            Match match = regex.Match(userAgent);
            if (match.Success)
            {
                string chromeVersion = match.Groups["version"].Value;
                return $"chrome {chromeVersion}";
            }
            return string.Empty;
        }

        public string GetChromeVersion(string userAgent)
        {
            int index = userAgent.IndexOf("Chrome/");
            if (index != -1)
            {
                int endIndex = userAgent.IndexOf(' ', index);
                if (endIndex != -1)
                {
                    return userAgent.Substring(index + "Chrome/".Length, endIndex - index - "Chrome/".Length);
                }
            }
            return string.Empty;
        }
    }
}
