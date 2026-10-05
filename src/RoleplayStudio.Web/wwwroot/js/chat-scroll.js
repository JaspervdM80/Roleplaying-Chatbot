const nearBottom = 48;

export function track(element) {
    const update = () => {
        element.dataset.atBottom = element.scrollHeight - element.scrollTop - element.clientHeight < nearBottom ? "1" : "0";
    };
    element.addEventListener("scroll", update, { passive: true });
    update();
}

export function follow(element, force) {
    if (element && (force || element.dataset.atBottom !== "0")) {
        element.scrollTop = element.scrollHeight;
    }
}
