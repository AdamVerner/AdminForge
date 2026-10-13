// Loaded blocking in <head>: sets data-theme before the first paint, so a dark page never flashes light.
(() => {
  const key = "adminforge-theme";
  const dark = matchMedia("(prefers-color-scheme: dark)");
  const mode = () => {
    try {
      return localStorage.getItem(key) || "system";
    } catch {
      return "system";
    }
  };
  const apply = () => {
    const m = mode();
    document.documentElement.dataset.theme = m === "system" ? (dark.matches ? "dark" : "light") : m;
  };
  apply();
  dark.addEventListener("change", apply);

  window.adminforge = {
    theme: mode,
    setTheme(m) {
      try {
        localStorage.setItem(key, m);
      } catch {}
      apply();
    },
    // After the dialog's own focus handling, which runs once it has opened and takes focus to its container.
    focusFirst(container, selector = "input:not([type=hidden]), select, textarea") {
      setTimeout(() => (container?.matches(selector) ? container : container?.querySelector(selector))?.focus(), 100);
    },
  };

  // "/" jumps to global search unless the admin is typing somewhere.
  document.addEventListener("keydown", (e) => {
    if (e.key !== "/" || e.ctrlKey || e.metaKey || e.altKey) return;
    if (e.target instanceof Element && e.target.closest("input, textarea, select, [contenteditable]")) return;
    const box = document.querySelector(".af-search input");
    if (box) {
      e.preventDefault();
      box.focus();
    }
  });

  // Menus are native popovers: the browser closes them on Esc and outside clicks and returns focus.
  const menuItems = (menu) => [...menu.querySelectorAll("[role=menuitem], [role=radio][tabindex='0']")];
  document.addEventListener(
    "toggle",
    (e) => {
      const menu = e.target;
      if (!(menu instanceof HTMLElement) || !menu.matches(".af-menu[popover]") || e.newState !== "open") return;
      const at = document.querySelector(`[popovertarget="${menu.id}"]`).getBoundingClientRect();
      menu.style.top = `${at.bottom + 4}px`;
      menu.style.right = `${document.documentElement.clientWidth - at.right}px`;
      menuItems(menu)[0]?.focus();
    },
    true,
  );
  document.addEventListener("keydown", (e) => {
    const radio = e.target instanceof Element && e.target.closest("[role=radio]");
    if (radio && (e.key === "ArrowLeft" || e.key === "ArrowRight")) {
      const all = [...radio.parentElement.querySelectorAll("[role=radio]")];
      const next = all[(all.indexOf(radio) + (e.key === "ArrowRight" ? 1 : all.length - 1)) % all.length];
      e.preventDefault();
      next.focus();
      next.click();
      return;
    }
    const menu = e.target instanceof Element && e.target.closest(".af-menu[popover]");
    if (!menu) return;
    const items = menuItems(menu);
    const at = items.indexOf(document.activeElement);
    const to = { ArrowDown: at + 1, ArrowUp: at - 1, Home: 0, End: items.length - 1 }[e.key];
    if (to === undefined) return;
    e.preventDefault();
    items[(to + items.length) % items.length].focus();
  });
  document.addEventListener("focusout", (e) => {
    const menu = e.target instanceof Element && e.target.closest(".af-menu[popover]:popover-open");
    // Only a keyboard move names where focus went; a click outside is the browser's light dismiss.
    if (menu && e.relatedTarget && !menu.contains(e.relatedTarget)) menu.hidePopover();
  });
  document.addEventListener("click", (e) => {
    if (e.target instanceof Element) e.target.closest(".af-menu [role=menuitem]")?.closest("[popover]")?.hidePopover();
  });

  // Markdown renders as markup, so its code blocks' copy buttons carry no Blazor handler.
  document.addEventListener("click", (e) => {
    const button = e.target instanceof Element && e.target.closest("[data-af=copy-code]");
    if (button) navigator.clipboard.writeText(button.parentElement.querySelector("pre").textContent);
  });

  // The filter popover closes on an outside click only; a dialog handles Esc itself.
  document.addEventListener("keydown", (e) => {
    if (e.key !== "Escape" || document.querySelector(".mud-dialog")) return;
    document.querySelector(".mud-overlay")?.click();
  });
})();
