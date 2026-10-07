// D02 review/feedback form: character counters (F04: external file so the CSP needs no inline script).
document.querySelectorAll("[data-count-target]").forEach(box => {
    const count = document.getElementById(box.dataset.countTarget);
    if (count) box.addEventListener("input", () => { count.textContent = box.value.length; });
});
