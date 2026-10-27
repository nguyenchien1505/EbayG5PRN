// B05 checkout summary: live total for the chosen quantity (F04: external file so the CSP needs no inline script).
const quantity = document.getElementById("quantity");
if (quantity) {
    const unitPrice = Number(quantity.dataset.unitPrice);
    quantity.addEventListener("input", () => {
        const count = Number(quantity.value);
        document.getElementById("summary-quantity").textContent = quantity.value;
        const total = "US $" + (count * unitPrice).toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        document.querySelectorAll("[data-summary-total]").forEach(x => { x.textContent = total; });
        if (Number.isInteger(count) && count >= 1 && count <= 99) {
            const addressLink = document.getElementById("add-address");
            const addressUrl = new URL(addressLink.href);
            addressUrl.searchParams.set("quantity", String(count));
            addressLink.href = addressUrl.toString();
        }
    });
}
