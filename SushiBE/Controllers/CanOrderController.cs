using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SushiBE.Data;
using SushiBE.Models;
using System;
using System.Threading.Tasks;

namespace SushiBE.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CanOrderController : ControllerBase
    {
        private readonly SushiDbContext _db;

        public CanOrderController(SushiDbContext db)
        {
            _db = db;
        }

        // Returns stored record (if any) and a computed IsActive flag.
        // Public check endpoint.
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Get()
        {
            var canOrder = await _db.CanOrders.FirstOrDefaultAsync();

            if (canOrder == null)
            {
                // default: allow ordering if not configured
                return Ok(new
                {
                    IsEnabled = true,
                    OnDate = (DateTime?)null,
                    OffDate = (DateTime?)null,
                    IsActive = true
                });
            }

            var now = DateTime.UtcNow;
            bool isActive = canOrder.IsEnabled;
            if (canOrder.OnDate.HasValue && now < canOrder.OnDate.Value) isActive = false;
            if (canOrder.OffDate.HasValue && now > canOrder.OffDate.Value) isActive = false;

            return Ok(new
            {
                canOrder.CanOrderId,
                canOrder.IsEnabled,
                canOrder.OnDate,
                canOrder.OffDate,
                IsActive = isActive
            });
        }

        // Admin endpoint to create or update the single CanOrder record.
        // Body example: { "isEnabled": true, "onDate": "2025-11-01T08:00:00Z", "offDate": null }
        [HttpPut]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update([FromBody] CanOrderUpdateDto dto)
        {
            if (dto == null) return BadRequest("Payload required.");

            var canOrder = await _db.CanOrders.FirstOrDefaultAsync();
            var isNew = false;
            if (canOrder == null)
            {
                canOrder = new CanOrder { CanOrderId = Guid.NewGuid() };
                isNew = true;
            }

            canOrder.IsEnabled = dto.IsEnabled;
            canOrder.OnDate = dto.OnDate;
            canOrder.OffDate = dto.OffDate;

            if (isNew) _db.CanOrders.Add(canOrder);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                canOrder.CanOrderId,
                canOrder.IsEnabled,
                canOrder.OnDate,
                canOrder.OffDate
            });
        }

        // Small DTO used by the Update endpoint
        public class CanOrderUpdateDto
        {
            public bool IsEnabled { get; set; } = true;
            public DateTime? OnDate { get; set; }
            public DateTime? OffDate { get; set; }
        }
    }
}
