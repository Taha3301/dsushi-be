using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SushiBE.Models
{
    public abstract class User
    {
        [Key]
        public Guid UserId { get; set; } = Guid.NewGuid();

        [Required, MaxLength(100)]
        public string Name { get; set; }

        [Required, EmailAddress]
        public string Email { get; set; }

        [Required]
        public string PasswordHash { get; set; }

        public bool IsVerified { get; set; } = false;

        [Column(TypeName = "nvarchar(255)")]
        public string? VerificationCode { get; set; }

        public DateTime? VerificationExpiry { get; set; }
    }
}
