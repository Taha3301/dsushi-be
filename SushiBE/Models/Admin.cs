namespace SushiBE.Models
{
    public class Admin : User
    {
        public bool Editor { get; set; } = true;
        public string Role { get; set; } = "Admin";
        public string Permission { get; set; } // or use a better structure later
    }
}
