// The renderer can request an export, never choose an arbitrary filesystem
// path or gain access to Node. The main process owns validation and save UI.
"use strict";
const {contextBridge, ipcRenderer} = require("electron");
contextBridge.exposeInMainWorld("studioDesktop", Object.freeze({
  saveFile: (name, bytes) => ipcRenderer.invoke("skin-studio:save", name, bytes)
}));
