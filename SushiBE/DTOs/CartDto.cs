using System;
using System.Collections.Generic;

namespace SushiBE.DTOs
{
    public class CartDto
    {
        public Guid Id { get; set; }
        public Guid CustomerId { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<CartItemDto> Items { get; set; }
    }
}