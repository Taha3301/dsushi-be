using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SushiBE.Models
{
    public class Order
    {
        [Key]
        public Guid OrderId { get; set; }

        public string Comments { get; set; }
        public DateTime OrderDate { get; set; }

        public string Status { get; set; }

        public decimal TotalAmount { get; set; }

        [Required]
        public Guid CustomerId { get; set; }

        [ForeignKey(nameof(CustomerId))]
        public Customer Customer { get; set; }

        public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    }
}