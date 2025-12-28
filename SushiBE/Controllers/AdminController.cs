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

        // New endpoint: total money of all Pending orders inside a CanOrder interval
        // GET /api/admin/orders/pending/total?canOrderId={guid}
        // If canOrderId is not provided the method chooses the most appropriate CanOrder (prefers enabled latest).
        [HttpGet("orders/pending/total")]
        public async Task<IActionResult> GetPendingOrdersTotal(Guid? canOrderId = null)
        {
            // choose CanOrder entry
            Models.CanOrder canOrder = null;
            if (canOrderId.HasValue)
            {
                canOrder = await _db.CanOrders.FindAsync(canOrderId.Value);
                if (canOrder == null)
                    return NotFound(new { error = "CanOrder not found", canOrderId });
            }
            else
            {
                // prefer enabled latest, otherwise latest record
                canOrder = await _db.CanOrders
                    .Where(c => c.IsEnabled && c.OnDate != null)
                    .OrderByDescending(c => c.OnDate)
                    .FirstOrDefaultAsync();

                if (canOrder == null)
                {
                    canOrder = await _db.CanOrders
                        .OrderByDescending(c => c.OnDate ?? DateTime.MinValue)
                        .FirstOrDefaultAsync();
                }

                if (canOrder == null)
                    return BadRequest(new { error = "No CanOrder entries available. Provide canOrderId or create a CanOrder with OnDate/OffDate." });
            }

            // Resolve interval: use OnDate as start (or very early) and OffDate as end (or now)
            var start = canOrder.OnDate?.Date ?? DateTime.MinValue;
            var end = (canOrder.OffDate ?? DateTime.UtcNow).Date.AddDays(1).AddTicks(-1);

            // Sum total amount of orders with Status == "Pending" in interval
            var totalPendingAmount = await _db.Orders
                .Where(o => o.OrderDate >= start && o.OrderDate <= end && o.Status == "Pending")
                .SumAsync(o => (decimal?)o.TotalAmount) ?? 0m;

            var pendingOrdersCount = await _db.Orders
                .CountAsync(o => o.OrderDate >= start && o.OrderDate <= end && o.Status == "Pending");

            return Ok(new
            {
                CanOrderId = canOrder.CanOrderId,
                Interval = new { From = start, To = end },
                TotalPendingAmount = Math.Round(totalPendingAmount, 2),
                TotalPendingOrders = pendingOrdersCount
            });
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

        // GET /api/admin/stock/requirements
        // Optional query param: canOrderId (GUID). If not provided, uses the most recent CanOrder (prefers enabled).
        // It sums all OrderItem quantities for Orders whose OrderDate is inside the CanOrder interval,
        // then computes ingredient totals using the "per 7" recipe:
        // per 7 items => 300 g rice, 400 ml water, 60 ml vinegar, 15 g sugar, 5 g salt.
        [HttpGet("stock/requirements")]
        public async Task<IActionResult> GetStockRequirements(Guid? canOrderId = null)
        {
            // choose CanOrder entry
            Models.CanOrder canOrder = null;
            if (canOrderId.HasValue)
            {
                canOrder = await _db.CanOrders.FindAsync(canOrderId.Value);
                if (canOrder == null)
                    return NotFound(new { error = "CanOrder not found", canOrderId });
            }
            else
            {
                // prefer enabled latest, otherwise latest record
                canOrder = await _db.CanOrders
                    .Where(c => c.IsEnabled && c.OnDate != null)
                    .OrderByDescending(c => c.OnDate)
                    .FirstOrDefaultAsync();

                if (canOrder == null)
                {
                    canOrder = await _db.CanOrders
                        .OrderByDescending(c => c.OnDate ?? DateTime.MinValue)
                        .FirstOrDefaultAsync();
                }

                if (canOrder == null)
                    return BadRequest(new { error = "No CanOrder entries available. Provide canOrderId or create a CanOrder with OnDate/OffDate." });
            }

            // Resolve interval: use OnDate as start (or very early) and OffDate as end (or now)
            var start = canOrder.OnDate?.Date ?? DateTime.MinValue;
            var end = (canOrder.OffDate ?? DateTime.UtcNow).Date.AddDays(1).AddTicks(-1);

            // Query order items joined with orders and products for Pending orders inside interval
            var itemsQuery = from oi in _db.OrderItems
                             join o in _db.Orders on oi.OrderId equals o.OrderId
                             join p in _db.Products on oi.ProductId equals p.ProductId
                             where o.OrderDate >= start && o.OrderDate <= end && o.Status == "Pending"
                             select new
                             {
                                 oi.ProductId,
                                 ProductName = p.Name,
                                 Quantity = oi.Quantity,
                                 ProductStock = p.Stock
                             };

            // Totals
            var totalItemsOrdered = await itemsQuery.SumAsync(i => (int?)i.Quantity) ?? 0;
            // total rollouts = sum(quantity * product.Stock)
            var totalRolloutsLong = await itemsQuery.SumAsync(i => (long?)(i.Quantity * (long)i.ProductStock)) ?? 0L;
            var totalRollouts = (decimal)totalRolloutsLong;

            // breakdown per product (useful for auditing)
            var perProduct = await itemsQuery
                .GroupBy(i => new { i.ProductId, i.ProductName })
                .Select(g => new
                {
                    ProductId = g.Key.ProductId,
                    ProductName = g.Key.ProductName,
                    TotalQuantity = g.Sum(x => x.Quantity),
                    TotalRollouts = g.Sum(x => (long)x.Quantity * x.ProductStock)
                })
                .ToListAsync();

            // Count only Pending orders in interval
            var totalOrders = await _db.Orders.CountAsync(o => o.OrderDate >= start && o.OrderDate <= end && o.Status == "Pending");

            // Per-7 recipe
            const decimal ricePer7Grams = 300m;
            const decimal waterPer7Ml = 400m;
            const decimal vinegarPer7Ml = 60m;
            const decimal sugarPer7Grams = 15m;
            const decimal saltPer7Grams = 5m;

            // Precise groups (fractional): totalRollouts / 7
            var preciseGroups = totalRollouts / 7.0m;

            // Integer full groups and remainder
            var fullGroups = (long)(totalRolloutsLong / 7L);
            var remainder = (long)(totalRolloutsLong % 7L);

            // Ingredients computed proportionally (precise)
            decimal requiredRiceGramsPrecise = preciseGroups * ricePer7Grams;
            decimal requiredWaterMlPrecise = preciseGroups * waterPer7Ml;
            decimal requiredVinegarMlPrecise = preciseGroups * vinegarPer7Ml;
            decimal requiredSugarGramsPrecise = preciseGroups * sugarPer7Grams;
            decimal requiredSaltGramsPrecise = preciseGroups * saltPer7Grams;

            // Also keep the previous "rounded up" groups and totals for compatibility
            var roundedUpGroups = Math.Ceiling(preciseGroups);
            decimal requiredRiceGramsRounded = roundedUpGroups * ricePer7Grams;
            decimal requiredWaterMlRounded = roundedUpGroups * waterPer7Ml;
            decimal requiredVinegarMlRounded = roundedUpGroups * vinegarPer7Ml;
            decimal requiredSugarGramsRounded = roundedUpGroups * sugarPer7Grams;
            decimal requiredSaltGramsRounded = roundedUpGroups * saltPer7Grams;

            return Ok(new
            {
                CanOrderId = canOrder.CanOrderId,
                Interval = new { From = start, To = end },
                TotalPendingOrders = totalOrders,
                TotalItemsOrdered = totalItemsOrdered,
                TotalRollouts = totalRolloutsLong,        // sum(quantity * product.Stock) as integer
                FullGroups = fullGroups,                  // integer 7-groups
                Remainder = remainder,                    // leftover rollouts after full groups
                PreciseGroups = Math.Round(preciseGroups, 4), // fractional groups (rounded for display)
                // precise proportional ingredient requirements
                IngredientsPrecise = new
                {
                    Rice = new { Amount = Math.Round(requiredRiceGramsPrecise, 2), Unit = "g" },
                    Water = new { Amount = Math.Round(requiredWaterMlPrecise, 2), Unit = "ml" },
                    Vinegar = new { Amount = Math.Round(requiredVinegarMlPrecise, 2), Unit = "ml" },
                    Sugar = new { Amount = Math.Round(requiredSugarGramsPrecise, 2), Unit = "g" },
                    Salt = new { Amount = Math.Round(requiredSaltGramsPrecise, 2), Unit = "g" }
                },
                // legacy/rounded-up totals (if you still want to allocate full groups only)
                IngredientsRoundedUp = new
                {
                    Rice = new { Amount = requiredRiceGramsRounded, Unit = "g" },
                    Water = new { Amount = requiredWaterMlRounded, Unit = "ml" },
                    Vinegar = new { Amount = requiredVinegarMlRounded, Unit = "ml" },
                    Sugar = new { Amount = requiredSugarGramsRounded, Unit = "g" },
                    Salt = new { Amount = requiredSaltGramsRounded, Unit = "g" }
                },
                PerProduct = perProduct
            });
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