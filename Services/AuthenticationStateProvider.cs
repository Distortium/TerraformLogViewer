using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace TerraformLogViewer.Services
{
    public class CustomAuthStateProvider : AuthenticationStateProvider
    {
        private readonly AuthService _authService;
        private bool _isInitialized = false;
        private ClaimsPrincipal _cachedUser = new ClaimsPrincipal(new ClaimsIdentity());

        public CustomAuthStateProvider(AuthService authService)
        {
            _authService = authService;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            if (!_isInitialized)
            {
                _cachedUser = await _authService.GetUserAsync();
                _isInitialized = true;
            }

            return new AuthenticationState(_cachedUser);
        }

        public async void NotifyAuthenticationStateChanged()
        {
            _cachedUser = await _authService.GetUserAsync();
            _isInitialized = true;
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_cachedUser)));
        }
    }
}