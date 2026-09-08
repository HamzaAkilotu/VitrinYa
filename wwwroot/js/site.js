document.addEventListener("submit", event => {
    const form = event.target;
    if (form.dataset.confirm && !confirm(form.dataset.confirm)) {
        event.preventDefault();
        return;
    }
    if (form.action.includes("/Cart/Checkout") && form.checkValidity()) {
        const button = form.querySelector('button[type="submit"]');
        button.dataset.originalLabel = button.textContent;
        button.disabled = true;
        button.textContent = "Siparişin hazırlanıyor…";
    }
});

// A browser back/forward restoration must not leave checkout disabled.
window.addEventListener("pageshow", () => {
    document.querySelectorAll("[data-original-label]").forEach(button => {
        button.disabled = false;
        button.textContent = button.dataset.originalLabel;
        delete button.dataset.originalLabel;
    });
});

document.addEventListener("error", event => {
    if (event.target instanceof HTMLImageElement && !event.target.dataset.fallback) {
        event.target.dataset.fallback = "true";
        event.target.removeAttribute("srcset");
        event.target.src = "/images/placeholder.svg";
    }
}, true);

const filterPanel = document.querySelector(".filter-panel");
if (filterPanel) {
    const narrow = window.matchMedia("(max-width: 800px)");
    const updatePanel = () => { filterPanel.open = !narrow.matches; };
    updatePanel();
    narrow.addEventListener("change", updatePanel);
}
document.querySelectorAll("[data-auto-submit]").forEach(select => {
    select.addEventListener("change", () => select.form.requestSubmit());
});

const photoDialog = document.querySelector(".photo-dialog");
document.querySelector("[data-open-photo]")?.addEventListener("click", () => photoDialog.showModal());
document.querySelector("[data-close-photo]")?.addEventListener("click", () => photoDialog.close());
photoDialog?.addEventListener("click", event => {
    if (event.target === photoDialog) photoDialog.close();
});

const current = new URL(window.location.href);
document.querySelectorAll(".panel-sidebar nav a").forEach(link => {
    const destination = new URL(link.href);
    const path = value => value.replace(/\/Index\/?$/i, "").replace(/\/$/, "").toLowerCase();
    if (path(destination.pathname) === path(current.pathname) &&
        destination.searchParams.get("status") === current.searchParams.get("status")) {
        link.classList.add("active");
        link.setAttribute("aria-current", "page");
    }
});
