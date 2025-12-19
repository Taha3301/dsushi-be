using BCrypt.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SushiBE.Data;
using SushiBE.DTOs.Auth;
using SushiBE.Models;
using SushiBE.Services;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace SushiBE.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly SushiDbContext _db;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly IConfiguration _config;
        private readonly IEmailService _emailService;

        public AuthController(SushiDbContext db, IPasswordHasher<User> passwordHasher, IConfiguration config, IEmailService emailService)
        {
            _db = db;
            _passwordHasher = passwordHasher;
            _config = config;
            _emailService = emailService;
        }

        [HttpPost("register/customer")]
        public async Task<IActionResult> RegisterCustomer([FromBody] RegisterDto dto)
        {
            if (await _db.Users.AnyAsync(u => u.Email == dto.Email))
                return Conflict(new { message = "Email already exists" });

            var name = ExtractNameFromEmail(dto.Email);

            var verificationCode = GenerateVerificationCode();
            var expiry = DateTime.UtcNow.AddMinutes(10);

            var customer = new Customer
            {
                Name = name,
                Email = dto.Email,
                Address = "temp",
                Phone = "temp",
                IsVerified = false,
                VerificationCode = verificationCode,
                VerificationExpiry = expiry
            };

            customer.PasswordHash = new PasswordHasher<User>().HashPassword(customer, dto.Password);

            _db.Customers.Add(customer);
            await _db.SaveChangesAsync();

            // Send verification code via email
            var htmlMessage = $@"
<!DOCTYPE html>
<html lang='en'>
<head>
  <meta charset='UTF-8'/>
  <meta name='viewport' content='width=device-width, initial-scale=1.0'/>
  <title>Email Verification</title>
</head>
<body style='margin:0;padding:0;background:#f5f7fa;font-family:Arial,Helvetica,sans-serif;'>

  <table role='presentation' border='0' cellpadding='0' cellspacing='0' width='100%'>
    <tr>
      <td align='center' style='padding:40px 0;background:#f5f7fa;'>
        <table role='presentation' cellpadding='0' cellspacing='0' width='100%' style='max-width:480px;background:#ffffff;border-radius:10px;overflow:hidden;box-shadow:0 4px 12px rgba(0,0,0,0.1);'>
          
          <!-- Header -->
          <tr>
            <td style='background:#d7263d;padding:20px;text-align:center;'>
              <h1 style='margin:0;font-size:22px;color:#ffffff;font-weight:bold;'>
                🍣 SushiBE Verification
              </h1>
            </td>
          </tr>
          
          <!-- Body -->
          <tr>
            <td style='padding:30px 25px;text-align:center;'>
              <p style='margin:0 0 15px 0;font-size:16px;color:#333;'>
                Thanks for registering with <strong>SushiBE</strong>!
              </p>
              <p style='margin:0 0 20px 0;font-size:16px;color:#333;'>
                Please use the following code to verify your email:
              </p>

              <div style='font-size:34px;letter-spacing:6px;font-weight:bold;color:#d7263d;margin:20px 0;'>
                {verificationCode}
              </div>

              <p style='margin:0;font-size:14px;color:#666;'>
                This code will expire in <strong>10 minutes</strong>.
              </p>
            </td>
          </tr>
          
          <!-- Divider -->
          <tr>
            <td style='padding:0 25px;'>
              <hr style='border:none;border-top:1px solid #eee;margin:25px 0;'/>
            </td>
          </tr>
          
          <!-- Footer -->
          <tr>
            <td style='padding:20px;text-align:center;font-size:12px;color:#999;'>
              SushiBE &copy; {DateTime.UtcNow.Year}<br/>
              If you did not create an account, please ignore this email.
            </td>
          </tr>

        </table>
      </td>
    </tr>
  </table>

</body>
</html>
";

            await _emailService.SendEmailAsync(dto.Email, "Your Verification Code", htmlMessage);

            return Ok(new { customer.UserId, customer.Name, customer.Email, message = "Registration successful. Please check your email for the verification code." });
        }

        [HttpPost("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailDto dto)
        {
            var user = await _db.Users.SingleOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null)
                return NotFound(new { message = "User not found." });

            if (user.IsVerified)
                return BadRequest(new { message = "Email already verified." });

            if (user.VerificationCode != dto.Code)
                return BadRequest(new { message = "Invalid verification code." });

            if (!user.VerificationExpiry.HasValue || user.VerificationExpiry.Value < DateTime.UtcNow)
                return BadRequest(new { message = "Verification code expired." });

            user.IsVerified = true;
            user.VerificationCode = null;
            user.VerificationExpiry = null;
            await _db.SaveChangesAsync();

            return Ok(new { message = "Email verified successfully." });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var user = await _db.Users.SingleOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null) return Unauthorized();

            if (!user.IsVerified)
                return Unauthorized(new { message = "Please verify your email before logging in." });

            var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password);
            if (result == PasswordVerificationResult.Failed) return Unauthorized();

            // If user is a customer, ensure they have a cart
            string role;
            if (user is Admin)
            {
                role = "Admin";
            }
            else if (user is Customer customer)
            {
                role = "Customer";
                var cart = await _db.Carts.FirstOrDefaultAsync(c => c.CustomerId == customer.UserId);
                if (cart == null)
                {
                    cart = new Cart
                    {
                        CartId = Guid.NewGuid(),
                        CustomerId = customer.UserId,
                        CreatedAt = DateTime.UtcNow,
                        TotalAmount = 0m,
                        Items = new List<CartItem>()
                    };
                    _db.Carts.Add(cart);
                    await _db.SaveChangesAsync();
                }
            }
            else
            {
                role = "User";
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.Name ?? ""),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, role)
            };

            var token = GenerateJwtToken(claims);
            var jwtSettings = _config.GetSection("Jwt");
            var expires = DateTime.UtcNow.AddMinutes(int.Parse(jwtSettings["ExpireMinutes"] ?? "60"));

            return Ok(new { Token = token, Expires = expires, Role = role });
        }

        private static string ExtractNameFromEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return string.Empty;
            var atIndex = email.IndexOf('@');
            return atIndex > 0 ? email.Substring(0, atIndex) : email;
        }

        private static string GenerateVerificationCode()
        {
            var rnd = new Random();
            return rnd.Next(100000, 999999).ToString();
        }

        private string GenerateJwtToken(Claim[] claims)
        {
            var jwtSettings = _config.GetSection("Jwt");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: jwtSettings["Issuer"],
                audience: jwtSettings["Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(int.Parse(jwtSettings["ExpireMinutes"] ?? "60")),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        [HttpPost("register-admin")]
        public async Task<IActionResult> RegisterAdmin([FromBody] AdminRegisterDto dto)
        {
            if (await _db.Admins.AnyAsync(a => a.Email == dto.Email))
                return BadRequest("Admin with this email already exists.");

            var admin = new Admin
            {
                Name = dto.Name,
                Email = dto.Email,
                // Set other properties as needed
                IsVerified = true // Admins are verified by default
            };

            admin.PasswordHash = _passwordHasher.HashPassword(admin, dto.Password);

            _db.Admins.Add(admin);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Admin registered successfully." });
        }

        [Authorize]
        [HttpPut("update-password")]
        public async Task<IActionResult> UpdatePassword([FromBody] UpdatePasswordDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var user = await _db.Users.FindAsync(Guid.Parse(userId));
            if (user == null) return NotFound("User not found");

            // Verify old password
            var check = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.OldPassword);
            if (check == PasswordVerificationResult.Failed)
                return BadRequest("Old password is incorrect");

            // Set new password
            user.PasswordHash = _passwordHasher.HashPassword(user, dto.NewPassword);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Password updated successfully" });
        }

        [Authorize]
        [HttpPut("update-profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var user = await _db.Users.FindAsync(Guid.Parse(userId));
            if (user == null) return NotFound("User not found");

            if (await _db.Users.AnyAsync(u => u.Email == dto.Email && u.UserId != user.UserId))
                return Conflict("Email already in use by another account");

            user.Name = dto.Name;
            user.Email = dto.Email;

            if (user is Customer customer)
            {
                customer.Address = dto.Address;
                customer.Phone = dto.Phone;
            }

            await _db.SaveChangesAsync();
            return Ok(new { message = "Profile updated successfully" });
        }
    }
}
