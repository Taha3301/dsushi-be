using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SushiBE.Data;
using SushiBE.Models;
using SushiBE.DTOs;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace SushiBE.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")] // <-- Only Admins can access all actions
    public class CategoryController : ControllerBase    
    {
        private readonly SushiDbContext _db;
        public CategoryController(SushiDbContext db) { _db = db; }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetAll() =>
            Ok(await _db.Categories.ToListAsync());

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var category = await _db.Categories.FindAsync(id);
            if (category == null) return NotFound();
            return Ok(category);
        }

        [HttpPost]
        public async Task<IActionResult> Create(CategoryDto dto)
        {
            var category = new Category { CategoryId = Guid.NewGuid(), Name = dto.Name };
            _db.Categories.Add(category);
            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(Get), new { id = category.CategoryId }, category);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, CategoryDto dto)
        {
            var category = await _db.Categories.FindAsync(id);
            if (category == null) return NotFound();
            category.Name = dto.Name;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var category = await _db.Categories.FindAsync(id);
            if (category == null) return NotFound();
            _db.Categories.Remove(category);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}