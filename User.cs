using System.Text.Json.Serialization;

namespace Api
{
    public class User
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Username { get; set; }
        public string PasswordHash { get; set; }

        [JsonIgnore] // игнорируем User при сериализации
        public List<License> Licenses { get; set; } = new();
    }
}
