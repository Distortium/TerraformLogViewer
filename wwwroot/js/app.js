// Функция для прокрутки к верху страницы
function scrollToTop() {
    window.scrollTo({ top: 0, behavior: 'smooth' });
}

// Функция для клика по элементу (для открытия диалога выбора файла)
function click(element) {
    element.click();
}

// Функция для показа уведомлений
function showToast(type, title, message) {
    // Создаем контейнер для уведомлений если его нет
    let container = document.getElementById('toast-container');
    if (!container) {
        container = document.createElement('div');
        container.id = 'toast-container';
        container.className = 'toast-container position-fixed top-0 end-0 p-3';
        container.style.zIndex = '1060';
        document.body.appendChild(container);
    }

    // Создаем уведомление
    const toastId = 'toast-' + Date.now();
    const toastHtml = `
        <div id="${toastId}" class="toast" role="alert" aria-live="assertive" aria-atomic="true">
            <div class="toast-header">
                <strong class="me-auto">${title}</strong>
                <button type="button" class="btn-close" data-bs-dismiss="toast" aria-label="Close"></button>
            </div>
            <div class="toast-body">
                ${message}
            </div>
        </div>
    `;

    container.insertAdjacentHTML('beforeend', toastHtml);

    // Добавляем классы в зависимости от типа
    const toastElement = document.getElementById(toastId);
    const toastHeader = toastElement.querySelector('.toast-header');

    switch (type) {
        case 'success':
            toastHeader.classList.add('text-white', 'bg-success');
            break;
        case 'error':
            toastHeader.classList.add('text-white', 'bg-danger');
            break;
        case 'warning':
            toastHeader.classList.add('text-dark', 'bg-warning');
            break;
        case 'info':
            toastHeader.classList.add('text-white', 'bg-info');
            break;
    }

    // Инициализируем и показываем уведомление
    const toast = new bootstrap.Toast(toastElement);
    toast.show();

    // Удаляем уведомление из DOM после скрытия
    toastElement.addEventListener('hidden.bs.toast', function () {
        this.remove();
    });
}