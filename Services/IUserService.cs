using Microsoft.EntityFrameworkCore;
using TerraformLogViewer.Models;
using System.Security.Cryptography;
using System.Text;

namespace TerraformLogViewer.Services
{
    public interface IUserService
    {
        Task<User> Authenticate(string email, string password);
        Task<User> Register(string email, string password);
        Task<bool> UserExists(string email);
    }

    public class UserService : IUserService
    {
        private readonly AppDbContext _context;

        public UserService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<User> Authenticate(string email, string password)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user == null || !VerifyPassword(password, user.PasswordHash))
                return null;

            // Обновляем время последнего входа
            user.LastLogin = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return user;
        }

        public async Task<User> Register(string email, string password)
        {
            if (await UserExists(email))
                throw new Exception("User already exists");

            var user = new User
            {
                Email = email,
                PasswordHash = HashPassword(password),
                CreatedAt = DateTime.UtcNow,
                LastLogin = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return user;
        }

        public async Task<bool> UserExists(string email)
        {
            return await _context.Users.AnyAsync(u => u.Email == email);
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(password);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        private bool VerifyPassword(string password, string storedHash)
        {
            var hash = HashPassword(password);
            return hash == storedHash;
        }
    }
}