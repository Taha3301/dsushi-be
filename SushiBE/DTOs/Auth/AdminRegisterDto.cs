namespace SushiBE.DTOs.Auth
{
    public class AdminRegisterDto
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        // Add any admin-specific fields here if needed
    }
}