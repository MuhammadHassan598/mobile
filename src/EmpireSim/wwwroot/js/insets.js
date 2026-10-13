/* Bottom safe area. The page is drawn edge to edge (viewport-fit=cover), so on a phone the system navigation buttons (or the gesture bar)
   lie over the bottom of the page. Anything pinned to the bottom (the map's country card with its Attack button, the dialogs that open from
   it) must stay above them. This sets the CSS variable --safe-bottom, in pixels:
     - the browser's own env(safe-area-inset-bottom) when it reports one (iOS, current Android WebViews, notches and home bars);
     - otherwise, on an Android WebView that is evidently drawn edge to edge (its viewport is as tall as the screen) but reports no inset,
       a navigation-bar allowance (48 px, the height of the three-button bar; more than the gesture bar needs, never less);
     - otherwise 0 (desktop, or a window that already sits above the system bars).
   It is recomputed when the window is resized or rotated. */
window.empireInsets = (() => {
    const ANDROID_NAV_ALLOWANCE = 48;
    const EDGE_TO_EDGE_TOLERANCE = 40;   // CSS px: the viewport counts as full-screen when it is within this of the screen's height

    // Pure decision, exported so it can be tested without a browser.
    function compute({ reported, isAndroid, screenHeight, viewportHeight }) {
        if (reported > 0) return reported;
        if (isAndroid && screenHeight > 0 && Math.abs(screenHeight - viewportHeight) < EDGE_TO_EDGE_TOLERANCE) return ANDROID_NAV_ALLOWANCE;
        return 0;
    }

    function reportedInset() {
        try {
            const probe = document.createElement('div');
            probe.style.cssText = 'position:fixed;left:0;bottom:0;width:0;height:0;visibility:hidden;pointer-events:none;padding-bottom:env(safe-area-inset-bottom,0px)';
            document.body.appendChild(probe);
            const px = parseFloat(getComputedStyle(probe).paddingBottom) || 0;
            probe.remove();
            return px;
        } catch (e) { return 0; }
    }

    function apply() {
        const px = compute({
            reported: reportedInset(),
            isAndroid: /Android/i.test(navigator.userAgent || ''),
            screenHeight: (window.screen && window.screen.height) || 0,
            viewportHeight: window.innerHeight || 0,
        });
        document.documentElement.style.setProperty('--safe-bottom', px + 'px');
        return px;
    }

    if (typeof window.addEventListener === 'function' && typeof document !== 'undefined' && document.body) {
        window.addEventListener('resize', apply);
        window.addEventListener('orientationchange', apply);
        apply();
    }
    return { compute, apply };
})();
