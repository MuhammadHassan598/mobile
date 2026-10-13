// Headless check of wwwroot/js/insets.js (the bottom safe-area decision). Run: node tests/js/insets.test.js
'use strict';
const fs = require('fs'), vm = require('vm'), path = require('path');
const sandbox = { window: {}, document: undefined, navigator: { userAgent: '' } };   // no document: the script must load without a browser
vm.createContext(sandbox);
vm.runInContext(fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'EmpireSim', 'wwwroot', 'js', 'insets.js'), 'utf8'), sandbox);
const { compute } = sandbox.window.empireInsets;
let failures = 0;
const check = (ok, name) => { console.log((ok ? '  PASS ' : '  FAIL ') + name); if (!ok) failures++; };

check(compute({ reported: 34, isAndroid: true, screenHeight: 900, viewportHeight: 900 }) === 34, 'a browser that reports an inset is believed (34 px)');
check(compute({ reported: 21, isAndroid: false, screenHeight: 844, viewportHeight: 790 }) === 21, 'the iPhone home bar inset is used as reported');
check(compute({ reported: 0, isAndroid: true, screenHeight: 892, viewportHeight: 892 }) === 48, 'an Android WebView drawn edge to edge that reports 0 gets the navigation-bar allowance (48 px), so the card clears the system buttons');
check(compute({ reported: 0, isAndroid: true, screenHeight: 892, viewportHeight: 870 }) === 48, '... also when a few pixels of the screen are not in the viewport');
check(compute({ reported: 0, isAndroid: true, screenHeight: 892, viewportHeight: 790 }) === 0, 'an Android window that already sits above the system bars needs no allowance');
check(compute({ reported: 0, isAndroid: false, screenHeight: 1080, viewportHeight: 1080 }) === 0, 'desktop (full-screen or not) gets no allowance');
check(compute({ reported: 0, isAndroid: true, screenHeight: 0, viewportHeight: 800 }) === 0, 'without a screen height nothing is guessed');
console.log(failures ? `\n${failures} CHECK(S) FAILED` : '\nINSETS CHECKS PASSED');
process.exit(failures ? 1 : 0);
