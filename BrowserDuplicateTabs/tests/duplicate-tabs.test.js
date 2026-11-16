"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const popupSource = fs.readFileSync(
  path.join(__dirname, "..", "Chrome", "popup.js"),
  "utf8"
);
const elements = new Map();
const document = {
  getElementById(id) {
    if (!elements.has(id)) {
      elements.set(id, {
        addEventListener() {},
        replaceChildren() {},
        querySelectorAll() { return []; }
      });
    }
    return elements.get(id);
  }
};
const sandbox = {
  URL,
  document,
  chrome: { tabs: { query: async () => [], remove: async () => {} } },
  confirm: () => true
};
vm.runInNewContext(
  `${popupSource}\nglobalThis.testGetDuplicateTabs = getDuplicateTabs;`,
  sandbox
);
const getDuplicateTabs = sandbox.testGetDuplicateTabs;

test("keeps one same-title same-URL tab and returns only duplicate tabs", () => {
  const tabs = [
    { id: 8, windowId: 1, index: 4, title: "Docs", url: "https://example.com/docs" },
    { id: 4, windowId: 1, index: 1, title: "docs", url: "https://example.com/docs" },
    { id: 9, windowId: 1, index: 5, title: "Docs", url: "https://example.com/other" }
  ];

  assert.deepEqual(Array.from(getDuplicateTabs(tabs), tab => tab.id), [8]);
});

test("preserves a pinned tab and ignores browser-internal pages", () => {
  const tabs = [
    { id: 2, windowId: 1, index: 0, title: "Pinned", url: "https://example.com", pinned: true },
    { id: 3, windowId: 1, index: 1, title: "Pinned", url: "https://example.com", pinned: false },
    { id: 4, windowId: 1, index: 2, title: "Settings", url: "chrome://settings" },
    { id: 5, windowId: 1, index: 3, title: "Settings", url: "chrome://settings" }
  ];

  assert.deepEqual(Array.from(getDuplicateTabs(tabs), tab => tab.id), [3]);
});

test("does not match tabs with the same title but different URL fragments", () => {
  const tabs = [
    { id: 1, windowId: 1, index: 0, title: "Page", url: "https://example.com/page#one" },
    { id: 2, windowId: 1, index: 1, title: "Page", url: "https://example.com/page#two" }
  ];

  assert.equal(getDuplicateTabs(tabs).length, 0);
});
