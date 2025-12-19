namespace SushiBE.DTOs.Auth
{
    public class UpdateProfileDto
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string Address { get; set; }   // Only for Customer
        public string Phone { get; set; }     // Only for Customer
    }
}
