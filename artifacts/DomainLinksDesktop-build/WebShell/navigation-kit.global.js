(function () {
  const escapeHtml = (value = "") =>
    String(value)
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;")
      .replaceAll("'", "&#39;");

  const renderLink = (link) => {
    const description = link.description
      ? `<span class="nav-kit__menu-text">${escapeHtml(link.description)}</span>`
      : "";
    const badge = link.badge
      ? `<span class="nav-kit__menu-badge">${escapeHtml(link.badge)}</span>`
      : "";
    const icon = link.icon
      ? `<span class="nav-kit__menu-icon" aria-hidden="true">${escapeHtml(link.icon)}</span>`
      : "";

    return `
      <a class="nav-kit__menu-link" href="${escapeHtml(link.href || "#")}">
        ${icon}
        <span class="nav-kit__menu-copy">
          <span class="nav-kit__menu-heading">${escapeHtml(link.label)}${badge}</span>
          ${description}
        </span>
      </a>
    `;
  };

  const renderPanelGroups = (groups) =>
    (groups || [])
      .map(
        (group) => `
          <section class="nav-kit__menu-group${group.featured ? " nav-kit__menu-group--featured" : ""}">
            ${group.label ? `<p class="nav-kit__menu-label">${escapeHtml(group.label)}</p>` : ""}
            ${(group.links || []).map(renderLink).join("")}
          </section>
        `,
      )
      .join("");

  const renderTopNavItem = (item, index) => {
    if (item.type === "link") {
      return `<a class="nav-kit__nav-link" href="${escapeHtml(item.href || "#")}">${escapeHtml(item.label)}</a>`;
    }

    const panelId = item.id || `panel-${index}`;

    return `
      <div class="nav-kit__nav-group">
        <button
          class="nav-kit__nav-trigger"
          type="button"
          data-nav-trigger="${escapeHtml(panelId)}"
          aria-expanded="false"
        >
          ${escapeHtml(item.label)}
        </button>
      </div>
    `;
  };

  const renderTopPane = (item, index) => {
    if (item.type !== "panel") {
      return "";
    }

    const panelId = item.id || `panel-${index}`;
    return `
      <section class="nav-kit__pane${index === 0 ? " is-active" : ""}" data-nav-pane="${escapeHtml(panelId)}">
        ${renderPanelGroups(item.groups)}
      </section>
    `;
  };

  const renderActions = (actions) =>
    (actions || [])
      .map(
        (action) => `
          <a
            class="nav-kit__action${action.variant === "primary" ? " nav-kit__action--primary" : ""}"
            href="${escapeHtml(action.href || "#")}"
          >
            ${escapeHtml(action.label)}
          </a>
        `,
      )
      .join("");

  const renderTopNavigation = (config) => `
    <header class="nav-kit" data-nav-kit>
      <div class="nav-kit__shell">
        <a class="nav-kit__brand" href="${escapeHtml((config.brand && config.brand.href) || "#")}">
          <span class="nav-kit__brand-mark" aria-hidden="true">${escapeHtml((config.brand && config.brand.mark) || "N")}</span>
          <span>${escapeHtml((config.brand && config.brand.label) || "Navigation Kit")}</span>
        </a>

        <button
          class="nav-kit__mobile-toggle"
          type="button"
          data-nav-mobile-toggle
          aria-expanded="false"
          aria-controls="nav-kit-main-nav"
        >
          Menu
        </button>

        <nav class="nav-kit__nav" id="nav-kit-main-nav" aria-label="Primary navigation">
          ${(config.items || []).map(renderTopNavItem).join("")}
          <div class="nav-kit__surface" data-nav-surface aria-hidden="true">
            <div class="nav-kit__surface-inner">
              ${(config.items || []).map(renderTopPane).join("")}
            </div>
          </div>
        </nav>

        <div class="nav-kit__actions">
          ${renderActions(config.actions)}
        </div>
      </div>
    </header>
  `;

  const renderSideSection = (section, index) => {
    const links = (section.links || [])
      .map(
        (link) => `
          <a class="nav-kit__side-link" href="${escapeHtml(link.href || "#")}">${escapeHtml(link.label)}</a>
        `,
      )
      .join("");

    const groups = (section.groups || [])
      .map(
        (group, groupIndex) => `
          <div class="nav-kit__side-group${groupIndex === 0 ? " is-open" : ""}">
            <button class="nav-kit__side-group-toggle" type="button" aria-expanded="${groupIndex === 0 ? "true" : "false"}">
              ${escapeHtml(group.label)}
            </button>
            <div class="nav-kit__side-group-links">
              ${(group.links || [])
                .map(
                  (link) => `
                    <a class="nav-kit__side-link" href="${escapeHtml(link.href || "#")}">${escapeHtml(link.label)}</a>
                  `,
                )
                .join("")}
            </div>
          </div>
        `,
      )
      .join("");

    return `
      <section class="nav-kit__side-section" data-side-section="${escapeHtml(section.id || `section-${index}`)}">
        ${section.label ? `<p class="nav-kit__side-label">${escapeHtml(section.label)}</p>` : ""}
        ${links}
        ${groups}
      </section>
    `;
  };

  const renderSideNavigation = (config) => `
    <aside class="nav-kit__sidebar" data-side-nav>
      <div class="nav-kit__sidebar-header">
        <a class="nav-kit__brand" href="${escapeHtml((config.brand && config.brand.href) || "#")}">
          <span class="nav-kit__brand-mark" aria-hidden="true">${escapeHtml((config.brand && config.brand.mark) || "N")}</span>
          <span>${escapeHtml((config.brand && config.brand.label) || "Navigation Kit")}</span>
        </a>
        ${
          config.title
            ? `
              <div class="nav-kit__sidebar-intro">
                ${config.eyebrow ? `<p class="nav-kit__side-label">${escapeHtml(config.eyebrow)}</p>` : ""}
                <h1 class="nav-kit__sidebar-title">${escapeHtml(config.title)}</h1>
              </div>
            `
            : ""
        }
        ${
          config.searchPlaceholder
            ? `
              <div class="nav-kit__sidebar-search">
                <input type="text" value="${escapeHtml(config.searchPlaceholder)}" aria-label="Sidebar search" readonly />
              </div>
            `
            : ""
        }
      </div>

      <nav class="nav-kit__side-nav" aria-label="Sidebar navigation">
        ${(config.sections || []).map(renderSideSection).join("")}
      </nav>
    </aside>
  `;

  const resetPane = (pane) => {
    pane.classList.remove("is-active", "is-exiting");
    delete pane.dataset.motion;
    pane.onanimationend = null;
  };

  const wireTopNavigation = (root) => {
    const mobileToggle = root.querySelector("[data-nav-mobile-toggle]");
    const nav = root.querySelector(".nav-kit__nav");
    const actions = root.querySelector(".nav-kit__actions");
    const surface = root.querySelector("[data-nav-surface]");
    const triggers = Array.from(root.querySelectorAll("[data-nav-trigger]"));
    const panes = Array.from(root.querySelectorAll("[data-nav-pane]"));

    let activeMenu = null;
    let closeTimer = null;
    let activeIndex = -1;

    const isMobile = () => window.innerWidth <= 1040;

    const clearCloseTimer = () => {
      if (closeTimer) {
        window.clearTimeout(closeTimer);
        closeTimer = null;
      }
    };

    const positionSurface = (trigger, pane) => {
      if (!surface || !nav) {
        return;
      }

      if (isMobile()) {
        surface.style.left = "";
        surface.style.width = "";
        surface.style.height = "";
        return;
      }

      const navRect = nav.getBoundingClientRect();
      const triggerRect = trigger.getBoundingClientRect();
      const nextWidth = Math.max(620, Math.ceil(pane.scrollWidth + 20));
      const centeredLeft = triggerRect.left - navRect.left + triggerRect.width / 2 - nextWidth / 2;
      const nextLeft = Math.max(0, Math.min(centeredLeft, navRect.width - nextWidth));
      const nextHeight = Math.ceil(pane.scrollHeight + 20);

      surface.style.left = `${nextLeft}px`;
      surface.style.width = `${nextWidth}px`;
      surface.style.height = `${nextHeight}px`;
    };

    const setActivePane = (menuName) => {
      const nextIndex = triggers.findIndex((trigger) => trigger.dataset.navTrigger === menuName);
      if (nextIndex === -1 || nextIndex === activeIndex) {
        return;
      }

      const isFirstOpen = activeIndex === -1;
      const movingRight = nextIndex > activeIndex;
      const nextPane = panes.find((pane) => pane.dataset.navPane === menuName);
      const currentPane = panes.find((pane) => pane.classList.contains("is-active"));
      const enterMotion = movingRight ? "enter-right" : "enter-left";
      const exitMotion = movingRight ? "exit-left" : "exit-right";

      panes.forEach((pane) => {
        if (pane !== currentPane && pane !== nextPane) {
          resetPane(pane);
        }
      });

      if (currentPane && currentPane !== nextPane && !isFirstOpen) {
        currentPane.classList.remove("is-active");
        currentPane.classList.add("is-exiting");
        currentPane.dataset.motion = exitMotion;
        currentPane.onanimationend = () => {
          resetPane(currentPane);
        };
      } else if (currentPane && currentPane !== nextPane) {
        resetPane(currentPane);
      }

      if (nextPane) {
        resetPane(nextPane);
        nextPane.classList.add("is-active");

        if (!isFirstOpen) {
          nextPane.dataset.motion = enterMotion;
          nextPane.onanimationend = () => {
            delete nextPane.dataset.motion;
            nextPane.onanimationend = null;
          };
        }
      }

      triggers.forEach((trigger) => {
        const isActive = trigger.dataset.navTrigger === menuName;
        trigger.classList.toggle("is-open", isActive);
        trigger.setAttribute("aria-expanded", String(isActive));
      });

      activeIndex = nextIndex;
    };

    const openMenu = (menuName) => {
      const trigger = root.querySelector(`[data-nav-trigger="${menuName}"]`);
      const pane = root.querySelector(`[data-nav-pane="${menuName}"]`);

      if (!trigger || !pane || !surface) {
        return;
      }

      clearCloseTimer();
      activeMenu = menuName;
      setActivePane(menuName);
      positionSurface(trigger, pane);
      surface.classList.add("is-open");
      surface.setAttribute("aria-hidden", "false");
    };

    const closeMenu = () => {
      activeMenu = null;
      activeIndex = -1;
      if (surface) {
        surface.classList.remove("is-open");
        surface.setAttribute("aria-hidden", "true");
      }

      panes.forEach((pane, index) => {
        resetPane(pane);
        pane.classList.toggle("is-active", index === 0);
      });

      triggers.forEach((trigger) => {
        trigger.classList.remove("is-open");
        trigger.setAttribute("aria-expanded", "false");
      });
    };

    const scheduleClose = () => {
      clearCloseTimer();
      closeTimer = window.setTimeout(closeMenu, 100);
    };

    if (mobileToggle) {
      mobileToggle.addEventListener("click", () => {
        const isOpen = nav && nav.classList.toggle("is-open");
        if (actions) {
          actions.classList.toggle("is-open", Boolean(isOpen));
        }
        mobileToggle.setAttribute("aria-expanded", String(Boolean(isOpen)));

        if (!isOpen) {
          closeMenu();
        }
      });
    }

    triggers.forEach((trigger) => {
      const menuName = trigger.dataset.navTrigger;

      trigger.addEventListener("mouseenter", () => {
        if (!isMobile()) {
          openMenu(menuName);
        }
      });

      trigger.addEventListener("focus", () => {
        openMenu(menuName);
      });

      trigger.addEventListener("click", () => {
        if (!isMobile()) {
          openMenu(menuName);
          return;
        }

        if (activeMenu === menuName && surface && surface.classList.contains("is-open")) {
          closeMenu();
          return;
        }

        openMenu(menuName);
      });
    });

    if (nav) {
      nav.addEventListener("mouseenter", () => {
        if (!isMobile()) {
          clearCloseTimer();
        }
      });

      nav.addEventListener("mouseleave", () => {
        if (!isMobile()) {
          scheduleClose();
        }
      });
    }

    if (surface) {
      surface.addEventListener("mouseenter", clearCloseTimer);
      surface.addEventListener("mouseleave", () => {
        if (!isMobile()) {
          scheduleClose();
        }
      });
    }

    window.addEventListener("resize", () => {
      if (!activeMenu) {
        return;
      }

      const trigger = root.querySelector(`[data-nav-trigger="${activeMenu}"]`);
      const pane = root.querySelector(`[data-nav-pane="${activeMenu}"]`);

      if (trigger && pane) {
        positionSurface(trigger, pane);
      }
    });

    document.addEventListener("click", (event) => {
      const target = event.target;
      if (!(target instanceof Element)) {
        return;
      }

      if (
        !target.closest("[data-nav-kit] .nav-kit__nav-group") &&
        !target.closest("[data-nav-kit] [data-nav-surface]") &&
        !target.closest("[data-nav-kit] [data-nav-mobile-toggle]")
      ) {
        closeMenu();
      }
    });
  };

  const wireSideNavigation = (root) => {
    const toggles = Array.from(root.querySelectorAll(".nav-kit__side-group-toggle"));

    toggles.forEach((toggle) => {
      toggle.addEventListener("click", () => {
        const group = toggle.closest(".nav-kit__side-group");
        const isOpen = group && group.classList.toggle("is-open");
        toggle.setAttribute("aria-expanded", String(Boolean(isOpen)));
      });
    });
  };

  const createNavigationKit = ({ mount, config, variant }) => {
    if (!mount) {
      throw new Error("createNavigationKit requires a mount element.");
    }

    const nextVariant = variant || "top";

    if (nextVariant === "top") {
      mount.innerHTML = renderTopNavigation(config || {});
      wireTopNavigation(mount);
      return;
    }

    if (nextVariant === "side") {
      mount.innerHTML = renderSideNavigation(config || {});
      wireSideNavigation(mount);
      return;
    }

    throw new Error(`Unsupported navigation variant: ${nextVariant}`);
  };

  window.NavigationKit = {
    createNavigationKit,
  };
})();
