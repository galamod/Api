using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Api.Models
{
    public class Payment
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid UserId { get; set; }

        [Required]
        [MaxLength(100)]
        public string OrderId { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string ApplicationName { get; set; } = string.Empty;

        [Required]
        public int PlanIndex { get; set; } // 0=week, 1=month, 2=3months, 3=6months, 4=year, 5=forever

        [Required]
        public decimal Amount { get; set; }

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = PaymentStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? PaidAt { get; set; }

        // Navigation property
        [JsonIgnore]
        public User? User { get; set; }
    }

    public static class PaymentStatus
    {
        public const string Pending = "Pending";
        public const string Paid = "Paid";
        public const string Failed = "Failed";
        public const string Cancelled = "Cancelled";
    }
}
