using System;
using System.ComponentModel.DataAnnotations;

namespace SushiBE.Models
{
    public class CanOrder
    {
        [Key]
        public Guid CanOrderId { get; set; }

        // Manual global enable/disable
        public bool IsEnabled { get; set; } = true;

        // Optional: when ordering becomes allowed (UTC)
        public DateTime? OnDate { get; set; }

        // Optional: when ordering becomes disallowed (UTC)
        public DateTime? OffDate { get; set; }
    }
}
