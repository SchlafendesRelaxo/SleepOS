"use strict";

const browserApi = globalThis.browser ?? globalThis.chrome;
const scanButton = document.getElementById("scan");
const closeButton = document.getElementById("close");
const statusText = document.getElementById("status");
const results = document.getElementById("results");
let duplicateTabs = [];

scanButton.addEventListener("click", scanTabs);
closeButton.addEventListener("click", closeSelectedTabs);

function getDuplicateTabs(tabs) {
  const groups = new Map();
  for (const tab of tabs) {
    if (!tab.id || !tab.url || !tab.title || !/^https?:\/\//i.test(tab.url)) {
      continue;
    }

    let normalizedUrl;
    try {
      normalizedUrl = new URL(tab.url).href;
    } catch {
      continue;
    }

    const key = `${tab.title.trim().toLowerCase()}\u0000${normalizedUrl}`;
    const group = groups.get(key) ?? [];
    group.push(tab);
    groups.set(key, group);
  }

  const duplicates = [];
  for (const group of groups.values()) {
    if (group.length < 2) {
      continue;
    }

    group.sort((left, right) => left.windowId - right.windowId || left.index - right.index);
    const keeper = group.find(tab => tab.pinned) ?? group[0];
    for (const tab of group) {
      if (tab.id !== keeper.id) {
        duplicates.push(tab);
      }
    }
  }

  return duplicates.sort((left, right) => left.windowId - right.windowId || left.index - right.index);
}

async function scanTabs() {
  scanButton.disabled = true;
  closeButton.disabled = true;
  results.replaceChildren();
  statusText.textContent = "Reading open web tabs...";

  try {
    const tabs = await browserApi.tabs.query({});
    duplicateTabs = getDuplicateTabs(tabs);
    for (const tab of duplicateTabs) {
      results.append(createTabRow(tab));
    }

    const closable = duplicateTabs.filter(tab => !tab.pinned).length;
    const pinned = duplicateTabs.length - closable;
    statusText.textContent = duplicateTabs.length === 0
      ? "No duplicate web tabs found."
      : `Found ${duplicateTabs.length} duplicate tab(s). ${pinned} pinned tab(s) cannot be closed. Review the selection below.`;
    updateCloseButton();
  } catch (error) {
    duplicateTabs = [];
    statusText.textContent = `Could not read browser tabs: ${error.message}`;
  } finally {
    scanButton.disabled = false;
  }
}

function createTabRow(tab) {
  const item = document.createElement("li");
  item.className = "tab";

  const checkbox = document.createElement("input");
  checkbox.type = "checkbox";
  checkbox.checked = !tab.pinned;
  checkbox.disabled = tab.pinned;
  checkbox.setAttribute("aria-label", `Close duplicate tab ${tab.title}`);
  checkbox.addEventListener("change", updateCloseButton);

  const description = document.createElement("div");
  const title = document.createElement("span");
  title.className = "title";
  title.textContent = tab.title;
  const url = document.createElement("span");
  url.className = "url";
  url.textContent = tab.url;
  description.append(title, url);

  if (tab.pinned) {
    const pinned = document.createElement("span");
    pinned.className = "pinned";
    pinned.textContent = "Pinned — kept open";
    description.append(pinned);
  }

  item.append(checkbox, description);
  item.dataset.tabId = String(tab.id);
  return item;
}

function getSelectedTabIds() {
  return [...results.querySelectorAll(".tab input:checked:not(:disabled)")]
    .map(checkbox => Number(checkbox.closest(".tab").dataset.tabId));
}

function updateCloseButton() {
  const count = getSelectedTabIds().length;
  closeButton.textContent = `Close selected (${count})`;
  closeButton.disabled = count === 0;
}

async function closeSelectedTabs() {
  const tabIds = getSelectedTabIds();
  if (tabIds.length === 0) {
    return;
  }

  if (!globalThis.confirm(`Close ${tabIds.length} selected duplicate tab(s)? The kept tab in each group will stay open.`)) {
    return;
  }

  closeButton.disabled = true;
  statusText.textContent = `Closing ${tabIds.length} selected tab(s)...`;
  try {
    await browserApi.tabs.remove(tabIds);
    await scanTabs();
    statusText.textContent = `Closed ${tabIds.length} selected duplicate tab(s). ${statusText.textContent}`;
  } catch (error) {
    statusText.textContent = `Could not close the selected tabs: ${error.message}`;
    updateCloseButton();
  }
}
