// Site-wide behaviour.

// Scrolling the page while a number field has focus makes the browser change its value
// instead of scrolling. On a price or payment box that means money moving with no click,
// no keystroke and nothing on screen to say it happened - you scroll past a form and the
// amount is silently different.
//
// Blurring the field on wheel is the fix rather than blocking the event: the value stops
// changing, and the page still scrolls the way the user expected. Browsers only apply
// wheel changes to a focused number input, so removing focus is the whole of it.
//
// Delegated from the document so it covers fields that arrive later - the order-detail
// popup and the edit-item dialog are both fetched after page load.
(function () {
    document.addEventListener("wheel", function (e) {
        var el = document.activeElement;
        if (!el || el.type !== "number") return;
        if (el !== e.target) return;
        el.blur();
    }, { passive: true });
})();
