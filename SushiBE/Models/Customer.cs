using System.ComponentModel.DataAnnotations.Schema;

namespace SushiBE.Models
{
    public class Customer : User
    {
        // The 'Name' property is inherited from User.
        public string Address { get; set; }    // "adresse" in your UML -> use Address
        public string Phone { get; set; }

        public Cart Cart { get; set; }
    }
}
