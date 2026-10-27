// H05 item page: gallery thumbnails swap the main picture; Quantity is carried into the Buy It Now checkout link.
(() => {
    const main = document.querySelector('[data-gallery-main]');
    document.querySelectorAll('[data-gallery-src]').forEach((thumb, index, thumbs) => thumb.addEventListener('click', () => {
        main.src = thumb.dataset.gallerySrc;
        main.alt = main.alt.replace(/picture \d+ of/, `picture ${index + 1} of`);
        thumbs.forEach(other => other.setAttribute('aria-pressed', String(other === thumb)));
    }));

    const input = document.querySelector('[data-quantity-input]');
    const link = document.querySelector('[data-quantity-link]');
    if (!input || !link) return;
    input.addEventListener('input', () => {
        const url = new URL(link.href);
        const quantity = Math.trunc(Number(input.value));
        // Out-of-range values fall back to 1; checkout rechecks stock anyway.
        url.searchParams.set('quantity', quantity >= 1 && quantity <= Number(input.max) ? String(quantity) : '1');
        link.href = url.toString();
    });
})();
