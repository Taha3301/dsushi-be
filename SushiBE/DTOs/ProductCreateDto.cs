using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;

public class ProductCreateDto
{
    public string Name { get; set; }
    public string Description { get; set; }
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public Guid CategoryId { get; set; }
    public List<IFormFile> Images { get; set; } // Add this property
}