using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using System.Security.Claims;

namespace TerraformLogViewer.Services
{
    public class AuthService
    {
        private readonly IUserService _userService;
        private readonly IJSRuntime _jsRuntime;

        public AuthService(IUserService userService, IJSRuntime jsRuntime)
        {
            _userService = userService;
            _jsRuntime = jsRuntime;
        }

        public async Task<bool> LoginAsync(string email, string password)
        {
            try
            {
                var user = await _userService.Authenticate(email, password);
                if (user != null)
                {
                    // Используем комбинацию sessionStorage и проверку на сервере
                    await _jsRuntime.InvokeVoidAsync("sessionStorage.setItem", "auth_email", user.Email);
                    await _jsRuntime.InvokeVoidAsync("sessionStorage.setItem", "auth_userId", user.Id.ToString());
                    await _jsRuntime.InvokeVoidAsync("sessionStorage.setItem", "auth_authenticated", "true");
                    await _jsRuntime.InvokeVoidAsync("sessionStorage.setItem", "auth_timestamp", DateTime.UtcNow.Ticks.ToString());
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public async Task LogoutAsync()
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", "auth_email");
                await _jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", "auth_userId");
                await _jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", "auth_authenticated");
                await _jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", "auth_timestamp");
            }
            catch
            {
                // Игнорируем ошибки при logout
            }
        }

        public async Task<bool> IsAuthenticatedAsync()
        {
            try
            {
                var isAuth = await _jsRuntime.InvokeAsync<string>("sessionStorage.getItem", "auth_authenticated");
                var timestamp = await _jsRuntime.InvokeAsync<string>("sessionStorage.getItem", "auth_timestamp");

                if (isAuth == "true" && !string.IsNullOrEmpty(timestamp))
                {
                    // Проверяем, что аутентификация не слишком старая (максимум 24 часа)
                    var authTime = new DateTime(long.Parse(timestamp));
                    return (DateTime.UtcNow - authTime).TotalHours < 24;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public async Task<Guid?> GetCurrentUserIdAsync()
        {
            try
            {
                var userIdString = await _jsRuntime.InvokeAsync<string>("sessionStorage.getItem", "auth_userId");
                if (!string.IsNullOrEmpty(userIdString) && Guid.TryParse(userIdString, out var userId))
                {
                    return userId;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<string?> GetCurrentUserEmailAsync()
        {
            try
            {
                return await _jsRuntime.InvokeAsync<string>("sessionStorage.getItem", "auth_email");
            }
            catch
            {
                return null;
            }
        }

        public async Task<ClaimsPrincipal> GetUserAsync()
        {
            var isAuthenticated = await IsAuthenticatedAsync();
            if (isAuthenticated)
            {
                try
                {
                    var email = await _jsRuntime.InvokeAsync<string>("sessionStorage.getItem", "auth_email");
                    var userId = await _jsRuntime.InvokeAsync<string>("sessionStorage.getItem", "auth_userId");

                    if (!string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(userId))
                    {
                        var claims = new List<Claim>
                        {
                            new Claim(ClaimTypes.Name, email),
                            new Claim(ClaimTypes.NameIdentifier, userId),
                            new Claim(ClaimTypes.Email, email)
                        };

                        var identity = new ClaimsIdentity(claims, "CustomAuth");
                        return new ClaimsPrincipal(identity);
                    }
                }
                catch
                {
                    // Если произошла ошибка, считаем неавторизованным
                }
            }

            return new ClaimsPrincipal(new ClaimsIdentity());
        }
    }
}