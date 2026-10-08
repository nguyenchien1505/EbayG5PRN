// H03 header menus (<details data-menu>): one open at a time, closed by an outside click or Escape. External file for the CSP.
const menus = [...document.querySelectorAll("details[data-menu]")];
menus.forEach(menu => menu.addEventListener("toggle", () => {
    if (menu.open) menus.filter(other => other !== menu).forEach(other => { other.open = false; });
}));
document.addEventListener("click", event => {
    menus.filter(menu => menu.open && !menu.contains(event.target)).forEach(menu => { menu.open = false; });
});
document.addEventListener("keydown", event => {
    if (event.key !== "Escape") return;
    const open = menus.find(menu => menu.open);
    if (open) { open.open = false; open.querySelector("summary")?.focus(); }
});
