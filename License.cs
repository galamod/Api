using System.Text.Json.Serialization;

namespace Api
{
    public class License
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Key { get; set; } // Уникальный лицензионный ключ
        public Guid? UserId { get; set; } // Пользователь, которому привязана лицензия (если привязана)
        public string? ApplicationName { get; set; } // null = ключ для всех приложений

        private DateTime _createdAt = DateTime.UtcNow;
        public DateTime CreatedAt
        {
            get => _createdAt;
            set => _createdAt = value.ToUniversalTime();
        }

        private DateTime? _expirationDate;
        public DateTime? ExpirationDate
        {
            get => _expirationDate;
            set => _expirationDate = value?.ToUniversalTime();
        }

        [JsonIgnore] // игнорируем User при сериализации
        public User? User { get; set; }
    }

    public class LicenseDto
    {
        public Guid Id { get; set; }
        public string Key { get; set; }
        public string? ApplicationName { get; set; }

        private DateTime? _expirationDate;
        public DateTime? ExpirationDate
        {
            get => _expirationDate;
            set => _expirationDate = value?.ToUniversalTime();
        }

        public Guid? UserId { get; set; }
        public string? UserUsername { get; set; }
    }

    public class CreateLicenseForUserDto
    {
        public Guid UserId { get; set; }
        public string? ApplicationName { get; set; } // null = для всех

        private DateTime? _expirationDate;
        public DateTime? ExpirationDate
        {
            get => _expirationDate;
            set => _expirationDate = value?.ToUniversalTime();
        }
    }

    public class GenerateLicenseDto
    {
        public string? ApplicationName { get; set; } // null если для всех приложений

        private DateTime? _expirationDate;
        public DateTime? ExpirationDate
        {
            get => _expirationDate;
            set => _expirationDate = value?.ToUniversalTime();
        }
    }

    public class ActivateLicenseDto
    {
        public string LicenseKey { get; set; }
    }

}
