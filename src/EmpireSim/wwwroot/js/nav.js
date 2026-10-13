/* EmpireSim navigation helpers (the page side of the shared Back button; the history itself is NavigationHistory in the core).
   - Scroll memory: the scroll position of each screen is remembered when the player moves on, and put back when they return by Back;
     a screen reached by going forward starts at the top.
   - Escape: on a desktop keyboard, Escape does what Back does (close the topmost popup, else return to the previous screen), unless a text
     field has the focus. The device's own Back (Android) is handled natively by MainPage.OnBackButtonPressed.
   Everything here is optional: without it Back still works, only scroll positions are not restored. */
window.empireNav = (() => {
    const positions = new Map();   // screen key -> scrollY when it was left
    let dotNetRef = null;
    let installed = false;
    let poppedLast = false;   // the last page change was the platform's own back (history popstate), not a forward move by the app

    const scrollY = () => window.pageYOffset || (document.documentElement && document.documentElement.scrollTop) || 0;
    const keyOf = () => (location.pathname || '/') + (location.search || '');
    let key = keyOf();

    // Remember where a screen was scrolled to at the moment the app moves forward from it (the router uses history.pushState).
    function install() {
        if (installed || typeof history === 'undefined' || !history.pushState) return;
        installed = true;
        const push = history.pushState.bind(history), replace = history.replaceState.bind(history);
        history.pushState = function () { positions.set(key, scrollY()); poppedLast = false; const r = push.apply(null, arguments); key = keyOf(); return r; };
        history.replaceState = function () { const r = replace.apply(null, arguments); key = keyOf(); return r; };
        window.addEventListener('popstate', () => { key = keyOf(); poppedLast = true; });
    }

    // Called by the app after every page change: back = the screen is returned to, so its scroll position is restored; otherwise it starts at the top.
    // The page may still be growing as it renders, so the position is applied a few times.
    function navigated(back) {
        key = keyOf();
        const y = back ? (positions.get(key) || 0) : 0;
        const go = () => window.scrollTo(0, y);
        go();
        if (typeof requestAnimationFrame === 'function') requestAnimationFrame(go);
        setTimeout(go, 80);
        setTimeout(go, 250);
    }

    // True when the page change just made was the platform's own back; read once.
    function consumePop() { const p = poppedLast; poppedLast = false; return p; }

    function onKey(e) {
        if (e.key !== 'Escape' || e.defaultPrevented || !dotNetRef) return;
        const t = e.target, tag = t && t.tagName ? t.tagName.toUpperCase() : '';
        if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || (t && t.isContentEditable)) return;
        dotNetRef.invokeMethodAsync('BackFromKeyboard');
    }

    function init(ref) {
        dotNetRef = ref;
        install();
        if (!init.keyBound && typeof document !== 'undefined') { document.addEventListener('keydown', onKey); init.keyBound = true; }
    }

    install();
    return { init, navigated, consumePop, install, positions, keyOf };
})();
