using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SushiBE.Data;
using SushiBE.DTOs;
using SushiBE.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.IO;

namespace SushiBE.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")] // <-- Only Admins can access all actions
    public class ProductController : ControllerBase
    {
        private readonly SushiDbContext _db;
        public ProductController(SushiDbContext db) { _db = db; }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetAll()
        {
            var products = await _db.Products
                .Include(p => p.Category)
                .Include(p => p.Images)
                .ToListAsync();

            var productDtos = products.Select(p => new
            {
                ProductId = p.ProductId,
                Name = p.Name,
                Description = p.Description,
                Price = p.Price,
                Stock = p.Stock,
                Disponible = p.Disponible,              // <-- added
                CategoryId = p.CategoryId,
                Category = p.Category == null ? null : new
                {
                    CategoryId = p.Category.CategoryId,
                    Name = p.Category.Name
                },
                ImageUrls = p.Images.Select(i => i.ImageUrl).ToList()
            }).ToList();

            return Ok(productDtos);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var product = await _db.Products
                .Include(p => p.Category)
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.ProductId == id);

            if (product == null) return NotFound();

            var result = new
            {
                ProductId = product.ProductId,
                Name = product.Name,
                Description = product.Description,
                Price = product.Price,
                Stock = product.Stock,
                Disponible = product.Disponible,         // <-- added
                CategoryId = product.CategoryId,
                Category = product.Category == null ? null : new
                {
                    CategoryId = product.Category.CategoryId,
                    Name = product.Category.Name
                },
                ImageUrls = product.Images.Select(i => i.ImageUrl).ToList()
            };

            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromForm] ProductCreateDto dto)
        {
            if (dto.Images == null || dto.Images.Count == 0)
                return BadRequest("At least one image is required.");
            if (dto.Images.Count > 8)
                return BadRequest("A maximum of 8 images is allowed.");

            var product = new Product
            {
                ProductId = Guid.NewGuid(),
                Name = dto.Name,
                Description = dto.Description,
                Price = dto.Price,
                Stock = dto.Stock,
                CategoryId = dto.CategoryId,
                Images = new List<ProductImage>()
            };

            // Save images into <ContentRoot>/wwwroot/Images so ASP.NET Core serves them at /Images/...

            var webroot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var imagesFolder = Path.Combine(webroot, "Images");
            if (!Directory.Exists(imagesFolder))
                Directory.CreateDirectory(imagesFolder);

            foreach (var image in dto.Images)
            {
                if (image.Length > 0)
                {
                    var fileName = $"{Guid.NewGuid()}{Path.GetExtension(image.FileName)}";
                    var filePath = Path.Combine(imagesFolder, fileName);

                    await using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                    {
                        await image.CopyToAsync(stream);
                    }

                    product.Images.Add(new ProductImage
                    {
                        ProductImageId = Guid.NewGuid(),
                        ImageUrl = $"SushiBE/wwwroot/Images/{fileName}"
                    });
                }
            }

            if (product.Images.Count > 0)
            {
                product.ImageUrl = product.Images.First().ImageUrl;
            }

            _db.Products.Add(product);
            await _db.SaveChangesAsync();

            // After saving the product
            var productDto = new ProductDto
            {
                ProductId = product.ProductId,
                Name = product.Name,
                Description = product.Description,
                Price = product.Price,
                Stock = product.Stock,
                CategoryId = product.CategoryId,
                Category = product.Category == null ? null : new CategoryDto
                {
                    CategoryId = product.Category.CategoryId,
                    Name = product.Category.Name
                },
                ImageUrls = product.Images.Select(i => ToAbsoluteUrl(i.ImageUrl)).Where(u => u != null).ToList()
            };
            return CreatedAtAction(nameof(Get), new { id = product.ProductId }, productDto);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromForm] ProductUpdateDto dto)
        {
            try
            {
                // 1️⃣ Load product including images
                var product = await _db.Products
                    .Include(p => p.Images)
                    .FirstOrDefaultAsync(p => p.ProductId == id);

                if (product == null)
                    return NotFound(new { error = "Product not found", productId = id });

                // 2️⃣ Update main fields
                product.Name = dto.Name;
                product.Description = dto.Description;
                product.Price = dto.Price;
                product.Stock = dto.Stock;
                product.CategoryId = dto.CategoryId;

                // 3️⃣ Handle images if provided
                if (dto.Images != null && dto.Images.Count > 0)
                {
                    if (dto.Images.Count > 8)
                        return BadRequest(new { error = "A maximum of 8 images is allowed." });

                    // Determine images folder once
                    var imagesFolder = GetImagesFolder();

                    // Remove old images from DB and disk
                    var oldImages = product.Images.ToList();
                    if (oldImages.Any())
                    {
                        // Remove DB records
                        _db.ProductImages.RemoveRange(oldImages);

                        // Delete physical files (use only file name to avoid path issues)
                        foreach (var img in oldImages)
                        {
                            var fileName = Path.GetFileName(img.ImageUrl ?? string.Empty);
                            if (!string.IsNullOrEmpty(fileName))
                            {
                                var filePath = Path.Combine(imagesFolder, fileName);
                                try
                                {
                                    if (System.IO.File.Exists(filePath))
                                        System.IO.File.Delete(filePath);
                                }
                                catch
                                {
                                    // ignore file delete errors (log if you have a logger)
                                }
                            }
                        }
                    }

                    // Ensure images folder exists
                    if (!Directory.Exists(imagesFolder))
                        Directory.CreateDirectory(imagesFolder);

                    // Add new images
                    var newImages = new List<ProductImage>();
                    foreach (var image in dto.Images)
                    {
                        if (image.Length > 0)
                        {
                            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(image.FileName)}";
                            var filePath = Path.Combine(imagesFolder, fileName);

                            await using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                            {
                                await image.CopyToAsync(stream);
                            }

                            newImages.Add(new ProductImage
                            {
                                ProductImageId = Guid.NewGuid(),
                                ProductId = product.ProductId,
                                ImageUrl = $"/images/{fileName}"
                            });
                        }
                    }

                    product.Images = newImages;
                    product.ImageUrl = product.Images.FirstOrDefault()?.ImageUrl;
                }

                // 4️⃣ Save changes
                await _db.SaveChangesAsync();

                return NoContent();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                return StatusCode(500, new
                {
                    error = "DbUpdateConcurrencyException",
                    message = ex.Message,
                    stackTrace = ex.StackTrace
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    error = "Exception",
                    message = ex.Message,
                    stackTrace = ex.StackTrace
                });
            }
        }

        [HttpPut("{id}/disponible")]

        public async Task<IActionResult> ToggleDisponible(Guid id)
        {
            try
            {
                var product = await _db.Products.FindAsync(id);
                if (product == null)
                    return NotFound(new { error = "Product not found", productId = id });

                product.Disponible = !product.Disponible;
                await _db.SaveChangesAsync();

                return Ok(new
                {
                    product.ProductId,
                    product.Disponible
                });
            }
            catch (DbUpdateConcurrencyException ex)
            {
                return StatusCode(500, new
                {
                    error = "DbUpdateConcurrencyException",
                    message = ex.Message,
                    stackTrace = ex.StackTrace
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    error = "Exception",
                    message = ex.Message,
                    stackTrace = ex.StackTrace
                });
            }
        }
        [HttpGet("{id}/images")]
        [AllowAnonymous] // ou [Authorize] si tu veux restreindre
        public async Task<IActionResult> GetImages(Guid id)
        {
            var product = await _db.Products
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.ProductId == id);

            if (product == null)
                return NotFound(new { error = "Produit introuvable", productId = id });

            var imageUrls = product.Images.Select(i => i.ImageUrl).ToList();

            return Ok(imageUrls);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var product = await _db.Products.FindAsync(id);
            if (product == null) return NotFound();
            _db.Products.Remove(product);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // Add this private helper method inside the ProductController class
        private string GetImagesFolder()
        {
            var imagesFolder = Path.Combine(Directory.GetCurrentDirectory(), "images");
            if (!Directory.Exists(imagesFolder))
                Directory.CreateDirectory(imagesFolder);
            return imagesFolder;
        }

        // Add this private helper method inside the ProductController class
        private string ToAbsoluteUrl(string relativeUrl)
        {
            if (string.IsNullOrEmpty(relativeUrl))
                return null;

            var request = HttpContext?.Request;
            if (request == null)
                return relativeUrl;

            var baseUrl = $"{request.Scheme}://{request.Host}";
            return $"{baseUrl}{relativeUrl}";
        }
    }
}