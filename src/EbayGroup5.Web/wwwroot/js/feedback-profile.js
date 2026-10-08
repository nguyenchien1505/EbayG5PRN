// H02 feedback profile: eBay applies Period and Sort as soon as they change (external file: CSP allows only 'self' scripts).
document.querySelectorAll("form[data-autosubmit] select").forEach(select => {
    select.addEventListener("change", () => select.form.requestSubmit());
});
