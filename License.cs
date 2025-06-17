namespace Api
{
    public class License
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Key { get; set; } = Guid.NewGuid().ToString("N");
        public string? Application { get; set; } // null = для всех приложений
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresAt { get; set; }
        public Guid? UserId { get; set; } // null = ещё не активирована
        public User? User { get; set; }
    }
}
