using Microsoft.AspNetCore.Mvc;
using SushiBE.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;
using System.Security.Claims;
using System;

namespace SushiBE.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly SushiDbContext _db;
        public UsersController(SushiDbContext db) { _db = db; }

        // Admin only: list all users
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAll()
        {
            var users = await _db.Users.ToListAsync();
            return Ok(users);
        }

        // Get current user
        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> Me()
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (id == null) return Unauthorized();
            var guid = Guid.Parse(id);
            var user = await _db.Users.FindAsync(guid);
            return Ok(user);
        }
    }
}
