namespace Api
{
    public class License
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Key { get; set; } // Уникальный лицензионный ключ
        public Guid? UserId { get; set; } // Пользователь, которому привязана лицензия (если привязана)
        public string? ApplicationName { get; set; } // null = ключ для всех приложений
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ExpirationDate { get; set; } // null = бессрочный
        public User? User { get; set; }
    }

    public class GenerateLicenseDto
    {
        public string? ApplicationName { get; set; } // null если для всех приложений
        public DateTime? ExpirationDate { get; set; }
    }

    public class ActivateLicenseDto
    {
        public string LicenseKey { get; set; }
    }
}
