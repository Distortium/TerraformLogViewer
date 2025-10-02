// wwwroot/js/infiniteScroll.js
let observer;
let currentDotNetHelper;
let currentContainer;

export function initializeInfiniteScroll(dotNetHelper, container) {
    currentDotNetHelper = dotNetHelper;
    currentContainer = container;

    // Способ 1: Intersection Observer (более современный)
    setupIntersectionObserver(container);

    // Способ 2: Обработчик скролла (запасной вариант)
    setupScrollHandler(container);
}

function setupIntersectionObserver(container) {
    // Создаем элемент-наблюдатель
    const sentinel = document.createElement('div');
    sentinel.className = 'scroll-sentinel';
    sentinel.style.height = '1px';
    sentinel.style.background = 'transparent';

    // Добавляем элемент в конец контейнера
    if (container.lastChild) {
        container.insertBefore(sentinel, container.lastChild.nextSibling);
    } else {
        container.appendChild(sentinel);
    }

    observer = new IntersectionObserver(async (entries) => {
        for (let entry of entries) {
            if (entry.isIntersecting) {
                try {
                    await currentDotNetHelper.invokeMethodAsync('LoadMoreData');
                } catch (error) {
                    console.error('Error loading more data:', error);
                }
            }
        }
    }, {
        root: container,
        rootMargin: '100px',
        threshold: 0.1
    });

    observer.observe(sentinel);
}

function setupScrollHandler(container) {
    const scrollHandler = async () => {
        const scrollTop = container.scrollTop;
        const scrollHeight = container.scrollHeight;
        const clientHeight = container.clientHeight;

        // Загружаем больше когда осталось 200px до конца
        if (scrollTop + clientHeight >= scrollHeight - 200) {
            try {
                await currentDotNetHelper.invokeMethodAsync('LoadMoreData');
            } catch (error) {
                console.error('Error in scroll handler:', error);
            }
        }
    };

    container.addEventListener('scroll', scrollHandler);

    // Сохраняем ссылку для cleanup
    container._scrollHandler = scrollHandler;
}

export function getScrollInfo(container) {
    return {
        scrollTop: container.scrollTop,
        scrollHeight: container.scrollHeight,
        clientHeight: container.clientHeight
    };
}

export function dispose() {
    if (observer) {
        observer.disconnect();
    }

    if (currentContainer && currentContainer._scrollHandler) {
        currentContainer.removeEventListener('scroll', currentContainer._scrollHandler);
    }

    currentDotNetHelper = null;
    currentContainer = null;
}