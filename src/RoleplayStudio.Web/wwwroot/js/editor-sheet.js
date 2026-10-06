const spyOffset = 32;

export function attach(root, editor) {
    const body = root.querySelector(".app-editor-body");
    const sections = () => [...body.querySelectorAll(".app-editor-section")];

    const markCurrent = () => {
        const reached = sections().filter(s => s.offsetTop <= body.scrollTop + spyOffset);
        const atEnd = body.scrollHeight - body.scrollTop - body.clientHeight < 2;
        const current = atEnd ? sections().at(-1) : reached.at(-1) ?? sections()[0];
        for (const link of root.querySelectorAll("[data-section]")) {
            const isCurrent = link.dataset.section === current?.id;
            link.toggleAttribute("aria-current", isCurrent);
            if (isCurrent && link.closest(".app-editor-chips")) {
                link.scrollIntoView({ block: "nearest", inline: "nearest" });
            }
        }
    };

    const isTopmost = () => [...document.querySelectorAll(".mud-dialog-container")].at(-1)?.contains(root);

    // Blurring first commits a field still being typed in, so the dirty check sees the last edit.
    const onKeyDown = event => {
        if (event.key === "Escape" && !event.repeat && !event.defaultPrevented && isTopmost() && !document.querySelector(".mud-popover-open")) {
            event.preventDefault();
            document.activeElement?.blur();
            editor.invokeMethodAsync("RequestCloseAsync");
        }
    };

    body.addEventListener("scroll", markCurrent, { passive: true });
    document.addEventListener("keydown", onKeyDown);
    markCurrent();

    return {
        dispose() {
            body.removeEventListener("scroll", markCurrent);
            document.removeEventListener("keydown", onKeyDown);
        }
    };
}

export function showSection(root, id) {
    root.querySelector(`#${CSS.escape(id)}`)?.scrollIntoView({ behavior: "smooth", block: "start" });
}

export function showField(root, field) {
    const target = root.querySelector(`[data-field="${CSS.escape(field)}"]`);
    if (target) {
        target.scrollIntoView({ behavior: "smooth", block: "center" });
        target.querySelector("input, textarea")?.focus({ preventScroll: true });
    }
}
