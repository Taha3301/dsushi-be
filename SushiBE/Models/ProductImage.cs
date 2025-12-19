using System;
using SushiBE.Models; // Ensure this using directive is present at the top

namespace SushiBE.Models
{
    public class ProductImage
    {
        public Guid ProductImageId { get; set; }
        public string ImageUrl { get; set; }
        public Guid ProductId { get; set; }
        public Product Product { get; set; }
    }
}
