using System;
using System.Collections.Generic;

namespace SushiBE.DTOs
{
    public class ProductDto
    {
        public Guid ProductId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public Guid CategoryId { get; set; }
        public CategoryDto Category { get; set; }
        public List<string> ImageUrls { get; set; }
    }
}