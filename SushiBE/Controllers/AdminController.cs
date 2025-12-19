using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SushiBE.Data;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SushiBE.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        private readonly SushiDbContext _db;

        public AdminController(SushiDbContext db) => _db = db;

        // GET /api/admin/summary
        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            var totalRevenue = await _db.Orders.SumAsync(o => (decimal?)o.TotalAmount) ?? 0m;
            var totalOrders = await _db.Orders.CountAsync();
            var totalCustomers = await _db.Users.OfType<Models.Customer>().CountAsync();
            var totalProducts = await _db.Products.CountAsync();
            var lowStockCount = await _db.Products.CountAsync(p => p.Stock <= 5);
            var totalInvoices = await _db.Invoices.CountAsync();
            var unpaidOrders = await _db.Orders.CountAsync(o => o.Status != "Completed" && o.Status != "Paid");

            return Ok(new
            {
                TotalRevenue = totalRevenue,
                TotalOrders = totalOrders,
                TotalCustomers = totalCustomers,
                TotalProducts = totalProducts,
                LowStockCount = lowStockCount,
                TotalInvoices = totalInvoices,
                UnpaidOrders = unpaidOrders
            });
        }

        // GET /api/admin/revenue?from=2025-11-01&to=2025-11-30
        // returns total and daily breakdown
        [HttpGet("revenue")]
        public async Task<IActionResult> GetRevenue(DateTime? from = null, DateTime? to = null)
        {
            var end = (to ?? DateTime.UtcNow).Date.AddDays(1).AddTicks(-1);
            var start = (from ?? DateTime.UtcNow.AddDays(-30)).Date;

            var query = await _db.Orders
                .Where(o => o.OrderDate >= start && o.OrderDate <= end)
                .ToListAsync();

            var total = query.Sum(o => o.TotalAmount);

            var daily = query
                .GroupBy(o => o.OrderDate.Date)
                .Select(g => new { Date = g.Key, Revenue = g.Sum(x => x.TotalAmount), Orders = g.Count() })
                .OrderBy(x => x.Date)
                .ToList();

            // fill missing days
            var days = (end.Date - start.Date).Days + 1;
            var filled = Enumerable.Range(0, days)
                .Select(i =>
                {
                    var d = start.AddDays(i);
                    var item = daily.FirstOrDefault(x => x.Date == d);
                    return new { Date = d, Revenue = item?.Revenue ?? 0m, Orders = item?.Orders ?? 0 };
                });

            return Ok(new { From = start, To = end, Total = total, Daily = filled });
        }

        // GET /api/admin/orders/recent?limit=10
        [HttpGet("orders/recent")]
        public async Task<IActionResult> GetRecentOrders(int limit = 10)
        {
            if (limit <= 0) limit = 10;
            var orders = await _db.Orders
                .Include(o => o.Items)
                    .ThenInclude(i => i.Product)
                .Include(o => o.Customer)
                .OrderByDescending(o => o.OrderDate)
                .Take(limit)
                .ToListAsync();

            var result = orders.Select(o => new
            {
                o.OrderId,
                o.OrderDate,
                o.Status,
                o.TotalAmount,
                CustomerId = o.CustomerId,
                CustomerName = o.Customer?.Name,
                Items = o.Items.Select(i => new
                {
                    i.OrderItemId,
                    i.ProductId,
                    ProductName = i.Product?.Name,
                    i.Quantity,
                    i.Price
                })
            });

            return Ok(result);
        }

        // GET /api/admin/orders/status-count
        [HttpGet("orders/status-count")]
        public async Task<IActionResult> GetOrdersByStatus()
        {
            var data = await _db.Orders
                .GroupBy(o => o.Status)
                .Select(g => new { Status = g.Key, Count = g.Count(), Revenue = g.Sum(x => (decimal?)x.TotalAmount) ?? 0m })
                .ToListAsync();

            return Ok(data);
        }

        // GET /api/admin/top-products?limit=10
        [HttpGet("top-products")]
        public async Task<IActionResult> GetTopProducts(int limit = 10)
        {
            if (limit <= 0) limit = 10;

            var top = await _db.OrderItems
                .Include(oi => oi.Product)
                .GroupBy(oi => new { oi.ProductId, ProductName = oi.Product.Name })
                .Select(g => new
                {
                    ProductId = g.Key.ProductId,
                    ProductName = g.Key.ProductName,
                    QuantitySold = g.Sum(x => x.Quantity),
                    Revenue = g.Sum(x => x.Price * x.Quantity)
                })
                .OrderByDescending(x => x.QuantitySold)
                .ThenByDescending(x => x.Revenue)
                .Take(limit)
                .ToListAsync();

            return Ok(top);
        }

        // GET /api/admin/low-stock?threshold=5
        [HttpGet("low-stock")]
        public async Task<IActionResult> GetLowStock(int threshold = 5)
        {
            var items = await _db.Products
                .Where(p => p.Stock <= threshold)
                .Select(p => new { p.ProductId, p.Name, p.Stock, p.ImageUrl })
                .ToListAsync();

            return Ok(items);
        }

        // GET /api/admin/customers/top?limit=10
        [HttpGet("customers/top")]
        public async Task<IActionResult> GetTopCustomers(int limit = 10)
        {
            if (limit <= 0) limit = 10;

            var top = await _db.Orders
                .GroupBy(o => o.CustomerId)
                .Select(g => new
                {
                    CustomerId = g.Key,
                    Orders = g.Count(),
                    Revenue = g.Sum(x => (decimal?)x.TotalAmount) ?? 0m
                })
                .OrderByDescending(x => x.Revenue)
                .Take(limit)
                .ToListAsync();

            // attach customer names
            var result = top.Select(t =>
            {
                var c = _db.Customers.Find(t.CustomerId);
                return new
                {
                    t.CustomerId,
                    CustomerName = c?.Name,
                    t.Orders,
                    t.Revenue
                };
            });

            return Ok(result);
        }

        // GET /api/admin/invoices/summary
        [HttpGet("invoices/summary")]
        public async Task<IActionResult> GetInvoiceSummary()
        {
            var count = await _db.Invoices.CountAsync();
            var total = await _db.Invoices.SumAsync(i => (decimal?)i.Amount) ?? 0m;
            var recent = await _db.Invoices
                .OrderByDescending(i => i.InvoiceDate)
                .Take(10)
                .Select(i => new { i.InvoiceId, i.InvoiceNumber, i.InvoiceDate, i.Amount, i.PdfUrl })
                .ToListAsync();

            return Ok(new { Count = count, TotalAmount = total, Recent = recent });
        }

        // GET /api/admin/users/count
        [HttpGet("users/count")]
        public async Task<IActionResult> GetUsersCount()
        {
            var customers = await _db.Users.OfType<Models.Customer>().CountAsync();
            var admins = await _db.Users.OfType<Models.Admin>().CountAsync();
            return Ok(new { Customers = customers, Admins = admins, Total = customers + admins });
        }

        // GET /api/admin/export/orders.csv?from=2025-01-01&to=2025-12-31
        [HttpGet("export/orders.csv")]
        public async Task<IActionResult> ExportOrdersCsv(DateTime? from = null, DateTime? to = null)
        {
            var start = (from ?? DateTime.UtcNow.AddMonths(-1)).Date;
            var end = (to ?? DateTime.UtcNow).Date.AddDays(1).AddTicks(-1);

            var orders = await _db.Orders
                .Include(o => o.Items)
                    .ThenInclude(i => i.Product)
                .Include(o => o.Customer)
                .Where(o => o.OrderDate >= start && o.OrderDate <= end)
                .OrderBy(o => o.OrderDate)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("OrderId,OrderDate,CustomerId,CustomerName,Status,TotalAmount,ItemCount,Items");

            foreach (var o in orders)
            {
                var items = string.Join("|", o.Items.Select(it => $"{it.Product?.Name ?? it.ProductId.ToString()} x{it.Quantity}@{it.Price:F2}"));
                var line = string.Join(",",
                    o.OrderId,
                    o.OrderDate.ToString("o", CultureInfo.InvariantCulture),
                    o.CustomerId,
                    (o.Customer?.Name ?? "").Replace(",", " "),
                    o.Status,
                    o.TotalAmount.ToString(CultureInfo.InvariantCulture),
                    o.Items.Count,
                    $"\"{items}\"");
                sb.AppendLine(line);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", $"orders_{start:yyyyMMdd}_{end:yyyyMMdd}.csv");
        }
    }
}