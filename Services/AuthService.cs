using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using TerraformLogViewer.Models;

namespace TerraformLogViewer.Services
{
    public class AuthService
    {
        private readonly AppDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuthService(AppDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<User?> RegisterAsync(string email, string password)
        {
            if (await _context.Users.AnyAsync(u => u.Email == email))
                return null;

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

        public async Task<User?> LoginAsync(string email, string password)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null || !VerifyPassword(password, user.PasswordHash))
                return null;

            user.LastLogin = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            SetUserSession(user.Id);

            await SignInUserAsync(user.Id.ToString());
            return user;
        }

        public async Task<User?> GetCurrentUserAsync()
        {
            var userId = GetCurrentUserId();
            if (userId == null) return null;

            return await _context.Users.FindAsync(userId);
        }

        public void Logout()
        {
            SignOutAsync(_httpContextAccessor.HttpContext?.Session.GetString("UserId"));

            _httpContextAccessor.HttpContext?.Session.Remove("UserId");
        }

        private void SetUserSession(Guid userId)
        {
            _httpContextAccessor.HttpContext?.Session.SetString("UserId", userId.ToString());
        }

        private Guid? GetCurrentUserId()
        {
            var userIdString = _httpContextAccessor.HttpContext?.Session.GetString("UserId");
            return Guid.TryParse(userIdString, out var userId) ? userId : null;
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(password);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        private bool VerifyPassword(string password, string passwordHash)
        {
            return HashPassword(password) == passwordHash;
        }

        public async Task SignInUserAsync(string userId)
        {
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
            var identity = new ClaimsIdentity(claims, "Cookies");
            var principal = new ClaimsPrincipal(identity);
            await _httpContextAccessor.HttpContext.SignInAsync("Cookies", principal);
            _httpContextAccessor.HttpContext.Session.SetString("UserId", userId);  // Если нужно сохранить в сессии
        }

        public async Task SignOutAsync(string userId)
        {
            await _httpContextAccessor.HttpContext.SignOutAsync("Cookies");
        }
    }
}