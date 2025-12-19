using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SushiBE.Data;
using SushiBE.DTOs;
using SushiBE.DTOs.Auth;
using SushiBE.Models;
using System.Threading.Tasks;

namespace SushiBE.Controllers
{
    [ApiController]
    [Route("api/cart")]
    public class CartController : ControllerBase
    {
        private readonly SushiDbContext _context;

        public CartController(SushiDbContext context)
        {
            _context = context;
        }

        [HttpGet("{customerId}")]
        public async Task<ActionResult<CartDto>> GetCart(Guid customerId)
        {
            var cart = await _context.Carts
                .Include(c => c.Items)
                .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(c => c.CustomerId == customerId);

            if (cart == null)
                return NotFound();

            var dto = new CartDto
            {
                Id = cart.CartId,
                CustomerId = cart.CustomerId,
                TotalAmount = cart.TotalAmount,
                CreatedAt = cart.CreatedAt,
                Items = cart.Items.Select(i => new CartItemDto
                {
                    Id = i.CartItemId,
                    ProductId = i.ProductId,
                    ProductName = i.Product?.Name,
                    Price = i.Price,
                    Quantity = i.Quantity
                }).ToList()
            };

            return dto;
        }

        [HttpPost("{customerId}/items")]
        public async Task<ActionResult<CartDto>> AddItem(Guid customerId, [FromBody] CreateCartItemDto dto)
        {
            // 1. Find or create the cart for the customer
            var cart = await _context.Carts
                .Include(c => c.Items)
                .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(c => c.CustomerId == customerId);

            if (cart == null)
            {
                cart = new Cart
                {
                    CartId = Guid.NewGuid(),
                    CustomerId = customerId,
                    CreatedAt = DateTime.UtcNow,
                    TotalAmount = 0m,
                    Items = new List<CartItem>()
                };
                _context.Carts.Add(cart);
            }

            // 2. Check if the product exists
            var product = await _context.Products.FindAsync(dto.ProductId);
            if (product == null)
                return NotFound("Product not found.");

            // 3. Check if the item already exists in the cart
            var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == dto.ProductId);

            if (existingItem != null)
            {
                // Always add 1 to quantity, ignore dto.Quantity
                existingItem.Quantity += 1;
                existingItem.Price = product.Price; // Always use current product price
                _context.CartItems.Update(existingItem);
            }
            else
            {
                // Add new item with quantity 1
                var cartItem = new CartItem
                {
                    CartItemId = Guid.NewGuid(),
                    CartId = cart.CartId,
                    ProductId = product.ProductId,
                    Price = product.Price,
                    Quantity = 1
                };
                cart.Items.Add(cartItem);
                _context.CartItems.Add(cartItem);
            }

            // 4. Recalculate total
            cart.TotalAmount = cart.Items.Sum(i => i.Price * i.Quantity);

            await _context.SaveChangesAsync();

            // Return updated cart state
            var updatedCart = await _context.Carts
                .Include(c => c.Items)
                .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(c => c.CartId == cart.CartId);

            var dtoResult = new CartDto
            {
                Id = updatedCart.CartId,
                CustomerId = updatedCart.CustomerId,
                TotalAmount = updatedCart.TotalAmount,
                CreatedAt = updatedCart.CreatedAt,
                Items = updatedCart.Items.Select(i => new CartItemDto
                {
                    Id = i.CartItemId,
                    ProductId = i.ProductId,
                    ProductName = i.Product?.Name ?? string.Empty,
                    Price = i.Price,
                    Quantity = i.Quantity
                }).ToList()
            };

            return Ok(dtoResult);
        }

        [HttpDelete("{customerId}/items/{productId}")]
        public async Task<ActionResult<CartDto>> RemoveItem(Guid customerId, Guid productId)
        {
            var cart = await _context.Carts
                .Include(c => c.Items)
                .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(c => c.CustomerId == customerId);

            if (cart == null)
                return NotFound("Cart not found.");

            var item = cart.Items.FirstOrDefault(i => i.ProductId == productId);
            if (item == null)
                return NotFound("Product not found in cart.");

            // Decrease quantity by 1
            item.Quantity -= 1;
            if (item.Quantity <= 0)
            {
                cart.Items.Remove(item);
                _context.CartItems.Remove(item);
            }
            else
            {
                _context.CartItems.Update(item);
            }

            cart.TotalAmount = cart.Items.Sum(i => i.Price * i.Quantity);

            await _context.SaveChangesAsync();

            // Return updated cart state
            var updatedCart = await _context.Carts
                .Include(c => c.Items)
                .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(c => c.CartId == cart.CartId);

            var dtoResult = new CartDto
            {
                Id = updatedCart.CartId,
                CustomerId = updatedCart.CustomerId,
                TotalAmount = updatedCart.TotalAmount,
                CreatedAt = updatedCart.CreatedAt,
                Items = updatedCart.Items.Select(i => new CartItemDto
                {
                    Id = i.CartItemId,
                    ProductId = i.ProductId,
                    ProductName = i.Product?.Name ?? string.Empty,
                    Price = i.Price,
                    Quantity = i.Quantity
                }).ToList()
            };

            return Ok(dtoResult);
        }
        [HttpDelete("{customerId}/products/{productId}")]
        public async Task<ActionResult<CartDto>> RemoveProduct(Guid customerId, Guid productId)
        {
            var cart = await _context.Carts
                .Include(c => c.Items)
                .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(c => c.CustomerId == customerId);

            if (cart == null)
                return NotFound("Cart not found.");

            var item = cart.Items.FirstOrDefault(i => i.ProductId == productId);
            if (item == null)
                return NotFound("Product not found in cart.");

            // Remove the item completely
            cart.Items.Remove(item);
            _context.CartItems.Remove(item);

            // Update cart total
            cart.TotalAmount = cart.Items.Sum(i => i.Price * i.Quantity);

            await _context.SaveChangesAsync();

            // Return updated cart
            var updatedCart = await _context.Carts
                .Include(c => c.Items)
                .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(c => c.CartId == cart.CartId);

            var dtoResult = new CartDto
            {
                Id = updatedCart.CartId,
                CustomerId = updatedCart.CustomerId,
                TotalAmount = updatedCart.TotalAmount,
                CreatedAt = updatedCart.CreatedAt,
                Items = updatedCart.Items.Select(i => new CartItemDto
                {
                    Id = i.CartItemId,
                    ProductId = i.ProductId,
                    ProductName = i.Product?.Name ?? string.Empty,
                    Price = i.Price,
                    Quantity = i.Quantity
                }).ToList()
            };

            return Ok(dtoResult);
        }


    }
}