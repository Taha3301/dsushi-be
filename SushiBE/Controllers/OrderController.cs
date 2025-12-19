using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SushiBE.Data;
using SushiBE.Models;
using SushiBE.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SushiBE.Controllers
{
    [ApiController]
    [Route("api/order")]
    public class OrderController : ControllerBase
    {
        private readonly SushiDbContext _context;
        private readonly IInvoicePdfService _pdfService;

        public OrderController(SushiDbContext context, IInvoicePdfService pdfService)
        {
            _context = context;
            _pdfService = pdfService;
        }

        [Authorize]
        [HttpPost("confirm/{customerId}")]
        public async Task<IActionResult> ConfirmCart(Guid customerId, [FromBody] string comments)
        {
            var cart = await _context.Carts
                .Include(c => c.Items)
                .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(c => c.CustomerId == customerId);

            if (cart == null || !cart.Items.Any())
                return BadRequest("Cart is empty or not found.");

            var order = new Order
            {
                OrderId = Guid.NewGuid(),
                CustomerId = cart.CustomerId,
                OrderDate = DateTime.UtcNow,
                Status = "Pending",
                Comments = comments,
                TotalAmount = cart.TotalAmount,
                Items = cart.Items.Select(ci => new OrderItem
                {
                    OrderItemId = Guid.NewGuid(),
                    ProductId = ci.ProductId,
                    Quantity = ci.Quantity,
                    Price = ci.Price
                }).ToList()
            };

            _context.Orders.Add(order);
            await _context.OrderItems.AddRangeAsync(order.Items);

            // Create invoice linked to this order/customer
            var invoice = new Invoice
            {
                InvoiceId = Guid.NewGuid(),
                OrderId = order.OrderId,
                CustomerId = order.CustomerId,
                InvoiceDate = DateTime.UtcNow,
                InvoiceNumber = GenerateInvoiceNumber(),
                Amount = order.TotalAmount
            };

            _context.Invoices.Add(invoice);

            // Persist order + invoice and clear cart in a single transaction
            _context.CartItems.RemoveRange(cart.Items);
            cart.Items.Clear();
            cart.TotalAmount = 0;

            await _context.SaveChangesAsync();

            // Try to generate PDF and update invoice.PdfUrl (service updates DB record too)
            string? pdfUrl = null;
            try
            {
                pdfUrl = await _pdfService.GeneratePdfByInvoiceIdAsync(invoice.InvoiceId);
            }
            catch
            {
                // PDF generation failed — we continue but pdfUrl remains null.
                // Consider logging the exception.
            }

            var host = $"{Request.Scheme}://{Request.Host}";

                return Ok(new
            {
                order.OrderId,
                order.CustomerId,
                order.OrderDate,
                order.Status,
                order.Comments,
                order.TotalAmount,
                Items = order.Items.Select(i => new
                {
                    i.OrderItemId,
                    i.ProductId,
                    i.Quantity,
                    i.Price
                }).ToList()
            });
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetAllOrders()
        {
            var orders = await _context.Orders
                .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                .Include(o => o.Customer)
                .ToListAsync();

            var result = orders.Select(order => new
            {
                order.OrderId,
                order.CustomerId,
                CustomerName = order.Customer != null ? order.Customer.Name : null,
                order.OrderDate,
                order.Status,
                order.Comments,
                order.TotalAmount,
                Items = order.Items.Select(i => new
                {
                    i.OrderItemId,
                    i.ProductId,
                    ProductName = i.Product != null ? i.Product.Name : null,
                    i.Quantity,
                    i.Price
                }).ToList()
            });

            return Ok(result);
        }

        [Authorize]
        [HttpPut("{orderId}/status")]
        [Authorize(Roles = "Admin")] // <-- Only Admins can access all actions
        public async Task<IActionResult> UpdateStatus(Guid orderId, [FromBody] string status)
        {
            var order = await _context.Orders.FindAsync(orderId);
            if (order == null)
                return NotFound("Order not found.");

            order.Status = status; // ✅ update only status
            await _context.SaveChangesAsync();

            return Ok(new
            {
                order.OrderId,
                order.Status
            });
        }

        // Existing invoice-by-customerId endpoint (kept) - route: GET /api/order/invoices/customer/{customerId}
        [Authorize]
        [HttpGet("invoices/customer/{customerId}")]
        public async Task<IActionResult> GetInvoicesByCustomer(Guid customerId)
        {
            var invoices = await _context.Invoices
                .Include(i => i.Order)
                    .ThenInclude(o => o.Items)
                        .ThenInclude(oi => oi.Product)
                .Where(i => i.CustomerId == customerId)
                .OrderByDescending(i => i.InvoiceDate)
                .ToListAsync();

            if (!invoices.Any())
                return NotFound("No invoices found for this customer.");

            var host = $"{Request.Scheme}://{Request.Host}";
            var result = invoices.Select(i => new
            {
                i.InvoiceId,
                i.InvoiceNumber,
                i.InvoiceDate,
                i.Amount,
                PdfUrl = i.PdfUrl,
                PdfUrlAbsolute = string.IsNullOrEmpty(i.PdfUrl) ? null : $"{host}{i.PdfUrl}",
                i.OrderId,
                Order = i.Order == null ? null : new
                {
                    i.Order.OrderId,
                    i.Order.OrderDate,
                    i.Order.Status,
                    Items = i.Order.Items.Select(it => new
                    {
                        it.OrderItemId,
                        it.ProductId,
                        ProductName = it.Product != null ? it.Product.Name : null,
                        it.Quantity,
                        it.Price
                    }).ToList()
                }
            });

            return Ok(result);
        }

        // New endpoint: get all invoices grouped by customer name (no input required)
        // Route: GET /api/order/invoices
        [Authorize]
        [HttpGet("invoices")]
        public async Task<IActionResult> GetAllInvoicesGroupedByCustomer()
        {
            var invoices = await _context.Invoices
                .Include(i => i.Customer)
                .Include(i => i.Order)
                    .ThenInclude(o => o.Items)
                        .ThenInclude(oi => oi.Product)
                .OrderByDescending(i => i.InvoiceDate)
                .ToListAsync();

            if (!invoices.Any())
                return NotFound("No invoices found.");

            var host = $"{Request.Scheme}://{Request.Host}";

            var grouped = invoices
                .GroupBy(i => new { i.CustomerId, Name = i.Customer != null ? i.Customer.Name : null })
                .Select(g => new
                {
                    CustomerId = g.Key.CustomerId,
                    CustomerName = string.IsNullOrEmpty(g.Key.Name) ? "Unknown" : g.Key.Name,
                    Invoices = g
                        .OrderByDescending(i => i.InvoiceDate)
                        .Select(i => new
                        {
                            i.InvoiceId,
                            i.InvoiceNumber,
                            i.InvoiceDate,
                            i.Amount,
                            PdfUrl = i.PdfUrl,
                            PdfUrlAbsolute = string.IsNullOrEmpty(i.PdfUrl) ? null : $"{host}{i.PdfUrl}",
                            i.OrderId,
                            Order = i.Order == null ? null : new
                            {
                                i.Order.OrderId,
                                i.Order.OrderDate,
                                i.Order.Status,
                                Items = i.Order.Items.Select(it => new
                                {
                                    it.OrderItemId,
                                    it.ProductId,
                                    ProductName = it.Product != null ? it.Product.Name : null,
                                    it.Quantity,
                                    it.Price
                                }).ToList()
                            }
                        }).ToList()
                })
                .OrderBy(g => g.CustomerName)
                .ToList();

            return Ok(grouped);
        }

        [Authorize] 
        [HttpDelete("{orderId}")]
        public async Task<IActionResult> DeleteOrder(Guid orderId)
        {
            var order = await _context.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
                return NotFound("Order not found.");

            // Supprimer d'abord les items liés
            if (order.Items.Any())
                _context.OrderItems.RemoveRange(order.Items);

            // Puis supprimer la commande
            _context.Orders.Remove(order);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Order and its items deleted successfully.",
                orderId = orderId
            });
        }

        // New endpoint: regenerate / generate invoice PDF by invoiceId
        [Authorize]
        [HttpPost("invoices/{invoiceId:guid}/regenerate")]
        public async Task<IActionResult> RegenerateInvoicePdf(Guid invoiceId)
        {
            // Only authenticated users. Optionally restrict to Admin or resource owner.
            var pdfUrl = await _pdfService.GeneratePdfByInvoiceIdAsync(invoiceId);
            if (pdfUrl == null)
                return NotFound(new { error = "Invoice not found", invoiceId });

            var absolute = string.IsNullOrEmpty(pdfUrl) ? null : $"{Request.Scheme}://{Request.Host}{pdfUrl}";

            return Ok(new
            {
                InvoiceId = invoiceId,
                PdfUrl = pdfUrl,
                PdfUrlAbsolute = absolute
            });
        }

        private string GenerateInvoiceNumber()
        {
            // Example: "INV-2024-000001" (increment logic can be improved as needed)
            var lastInvoice = _context.Invoices
                .OrderByDescending(i => i.InvoiceDate)
                .FirstOrDefault();

            int nextNumber = 1;
            if (lastInvoice != null && !string.IsNullOrEmpty(lastInvoice.InvoiceNumber))
            {
                var parts = lastInvoice.InvoiceNumber.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[2], out int lastNumber))
                {
                    nextNumber = lastNumber + 1;
                }
            }
            return $"INV-{DateTime.UtcNow.Year}-{nextNumber:D6}";
        }
    }
}