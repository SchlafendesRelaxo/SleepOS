# SleepOS duplicate-tab extension

The extension compares the exact HTTP/HTTPS URL and the tab title (case-insensitive).
It skips browser-internal pages, local files, and tabs without a title. It keeps one
tab from each duplicate group, never selects pinned tabs, and does not close anything
until you review the list and confirm. Tab information stays in the browser.

## Chrome and Edge

1. Extract the `Chrome` folder from the ZIP and keep it in a permanent location.
2. Open `chrome://extensions` in Chrome or `edge://extensions` in Edge.
3. Turn on **Developer mode**, choose **Load unpacked**, and select that `Chrome`
   folder.
4. Open the SleepOS Duplicate Tabs toolbar button, scan, review the checked tabs,
   and confirm to close the selected duplicates.

## Firefox

1. Extract the `Firefox` folder from the ZIP and keep it in a permanent location.
2. Open `about:debugging#/runtime/this-firefox`, choose **Load Temporary Add-on**,
   and select `manifest.json` from that folder.
3. Open the SleepOS Duplicate Tabs toolbar button, scan, review, and confirm.

Firefox temporary add-ons are removed when Firefox restarts; installing a
permanently signed Firefox add-on requires publishing it through Mozilla Add-ons.

The extension uses the browser's `tabs` permission to read tab titles and URLs
locally and close only the tabs you select. It has no network access or host
permissions. Other Chromium-based browsers may support loading the Chrome folder,
but are not individually tested.

## Run duplicate matching tests

From the repository root, run `node --test BrowserDuplicateTabs\tests\duplicate-tabs.test.js`.
