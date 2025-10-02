// wwwroot/js/app.js

// Scroll to top function
function scrollToTop() {
    window.scrollTo({
        top: 0,
        behavior: 'smooth'
    });
}

// Initialize cosmic theme
function initializeCosmicTheme() {
    // Add cosmic class to body for consistent styling
    document.body.classList.add('cosmic-theme');

    // Update any existing elements with cosmic styling
    const mainContainer = document.querySelector('.container, main');
    if (mainContainer && !mainContainer.classList.contains('cosmic-container')) {
        mainContainer.classList.add('cosmic-container');
    }
}

// Handle Blazor navigation and apply cosmic styles
function handleBlazorNavigation() {
    // This would be called on Blazor navigation events
    setTimeout(initializeCosmicTheme, 100);
}

// Export functions for Blazor
window.CosmicTheme = {
    scrollToTop: scrollToTop,
    initialize: initializeCosmicTheme,
    handleNavigation: handleBlazorNavigation
};

// Initialize when DOM is ready
document.addEventListener('DOMContentLoaded', function () {
    initializeCosmicTheme();

    // Re-initialize when Blazor finishes loading
    if (window.Blazor) {
        setTimeout(initializeCosmicTheme, 1000);
    }
});