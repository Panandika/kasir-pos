namespace Kasir.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string PasswordHash { get; set; }
        public string PasswordSalt { get; set; }
        public string DisplayName { get; set; }
        public string Alias { get; set; }
        public int RoleId { get; set; }
        public int IsActive { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
    }
}
