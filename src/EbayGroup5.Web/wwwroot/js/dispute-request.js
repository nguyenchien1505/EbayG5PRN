// E03 request details: character counter and a single submit per reply (the server still rejects repeats via the E01 rules).
for (const form of document.querySelectorAll("form.dispute-reply[method=post]")) {
    for (const box of form.querySelectorAll("[data-count-target]")) {
        const count = document.getElementById(box.dataset.countTarget);
        if (count) box.addEventListener("input", () => { count.textContent = box.value.length; });
    }
    form.addEventListener("submit", () => {
        for (const button of form.querySelectorAll("button[type=submit]")) button.disabled = true;
    });
}
