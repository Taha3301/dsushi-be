using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SushiBE.Models
{
    public class Category
    {
        [Key]
        public Guid CategoryId { get; set; }
        [Required, MaxLength(100)]
        public string Name { get; set; }

        public ICollection<Product> Products { get; set; }
    }
}