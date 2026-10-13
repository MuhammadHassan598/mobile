// Headless checks of wwwroot/js/nav.js (scroll memory, the platform-back flag, Escape): node tests/js/nav.test.js
'use strict';
const fs = require('fs'), vm = require('vm'), path = require('path');
let failures = 0;
const check = (ok, name) => { console.log((ok ? '  PASS ' : '  FAIL ') + name); if (!ok) failures++; };

function load() {
    const listeners = {}, docListeners = {};
    const loc = { pathname: '/', search: '' };
    const scrolls = []; let y = 0;
    const history = {
        pushState(_s, _t, url) { const u = new URL(url, 'http://app'); loc.pathname = u.pathname; loc.search = u.search; },
        replaceState(_s, _t, url) { const u = new URL(url, 'http://app'); loc.pathname = u.pathname; loc.search = u.search; },
    };
    const sandbox = {
        window: { pageYOffset: 0, addEventListener: (n, f) => { (listeners[n] = listeners[n] || []).push(f); }, scrollTo: (_x, yy) => { scrolls.push(yy); y = yy; sandbox.window.pageYOffset = yy; } },
        document: { documentElement: { scrollTop: 0 }, addEventListener: (n, f) => { (docListeners[n] = docListeners[n] || []).push(f); } },
        history, location: loc, console,
        setTimeout: f => f(), requestAnimationFrame: f => f(),
    };
    sandbox.window.history = history;
    vm.createContext(sandbox);
    vm.runInContext(fs.readFileSync(path.join(__dirname, '..', '..', 'src/EmpireSim/wwwroot/js/nav.js'), 'utf8'), sandbox);
    return { nav: sandbox.window.empireNav, sandbox, loc, listeners, docListeners, scrolls, setScroll: v => { sandbox.window.pageYOffset = v; }, popstate: () => (listeners.popstate || []).forEach(f => f()) };
}

console.log('== nav.js ==');
{
    const t = load();
    t.setScroll(420); t.sandbox.history.pushState({}, '', '/colonies');
    check(t.nav.positions.get('/') === 420, 'moving forward remembers how far the screen was scrolled');
    t.nav.navigated(false);
    check(t.scrolls.length > 0 && t.scrolls.every(v => v === 0), 'a screen reached by going forward starts at the top');
    t.setScroll(75); t.sandbox.history.pushState({}, '', '/colonies/colony-java');
    check(t.nav.positions.get('/colonies') === 75, 'every screen on the way is remembered by its own address');
    t.scrolls.length = 0; t.loc.pathname = '/colonies'; t.loc.search = '';
    t.nav.navigated(true);
    check(t.scrolls.length > 0 && t.scrolls.every(v => v === 75), 'returning by Back puts the screen back where it was');
    t.scrolls.length = 0; t.loc.pathname = '/never-seen';
    t.nav.navigated(true);
    check(t.scrolls.every(v => v === 0), 'a screen with nothing remembered opens at the top');
    t.sandbox.history.pushState({}, '', '/x?focus=1'); t.setScroll(10); t.sandbox.history.pushState({}, '', '/x?focus=2');
    check(t.nav.positions.get('/x?focus=1') === 10, 'the query is part of the screen address');
}
{
    const t = load();
    check(t.nav.consumePop() === false, 'no platform back at the start');
    t.sandbox.history.pushState({}, '', '/a');
    t.popstate();
    check(t.nav.consumePop() === true && t.nav.consumePop() === false, 'a popstate (the platform\'s own back) is reported once');
    t.popstate(); t.sandbox.history.pushState({}, '', '/b');
    check(t.nav.consumePop() === false, 'a forward move after it is not a platform back');
}
{
    const t = load(); const calls = [];
    t.nav.init({ invokeMethodAsync: n => calls.push(n) });
    const press = (key, tag, extra) => (t.docListeners.keydown || []).forEach(f => f(Object.assign({ key, target: { tagName: tag || 'DIV' }, defaultPrevented: false }, extra || {})));
    press('Escape'); press('Enter'); press('Escape', 'INPUT'); press('Escape', 'DIV', { defaultPrevented: true }); press('Escape', 'DIV', { target: { tagName: 'DIV', isContentEditable: true } });
    check(calls.length === 1 && calls[0] === 'BackFromKeyboard', 'Escape asks the app to go back; other keys, text fields and handled events do not');
    t.nav.init({ invokeMethodAsync: n => calls.push(n) });
    check((t.docListeners.keydown || []).length === 1, 'the key handler is bound once');
}
console.log(failures ? `\n${failures} CHECK(S) FAILED` : '\nNAV CHECKS PASSED');
process.exit(failures ? 1 : 0);
