using System.ComponentModel.DataAnnotations;

namespace Api.Models
{
    public class CreatePaymentRequest
    {
        [Required(ErrorMessage = "ApplicationName ����������")]
        [MaxLength(200)]
        public string ApplicationName { get; set; } = string.Empty;

        [Required(ErrorMessage = "PlanIndex ����������")]
        [Range(0, 5, ErrorMessage = "PlanIndex ������ ���� �� 0 �� 5")]
        public int PlanIndex { get; set; }

        [Required(ErrorMessage = "Amount ����������")]
        [Range(0.01, 1000000, ErrorMessage = "Amount ������ ���� �������������")]
        public decimal Amount { get; set; }
    }

    public class PaymentUrlResponse
    {
        public string PaymentUrl { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
    }

    public class PaymentStatusResponse
    {
        public string OrderId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? PaidAt { get; set; }
    }

    public class FreeKassaWebhookRequest
    {
        public string MERCHANT_ID { get; set; } = string.Empty;
        public string AMOUNT { get; set; } = string.Empty;
        public string MERCHANT_ORDER_ID { get; set; } = string.Empty;
        public string SIGN { get; set; } = string.Empty;
    }
}
