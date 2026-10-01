const { app, BrowserWindow, Menu, session, ipcMain, dialog } = require("electron");
const path = require("path");
const fs = require("fs/promises");
const {pathToFileURL} = require("url");
const entry = path.join(__dirname, "..", "index.html");
let saving = false;

// Only the trusted main frame can save the two supported export types.
// No destination comes from the renderer; real users approve the native dialog.
ipcMain.handle("skin-studio:save", async (event, name, bytes) => {
  if (event.senderFrame !== event.sender.mainFrame || event.sender.getURL() !== pathToFileURL(entry).href ||
      !["gameplay-ui.json", "skin-studio-export.zip"].includes(name) || !(bytes instanceof Uint8Array) ||
      bytes.length === 0 || bytes.length > 260 * 1024 * 1024 || saving) throw Error("Invalid export request");
  saving = true;
  try {
    let destination;
    if (process.argv.includes("--smoke-test")) {
      // Tests can save only under a process-specific temporary directory.
      const root=path.join(app.getPath("temp"), "chunithm-skin-studio-smoke-"+process.pid);
      await fs.mkdir(root,{recursive:true});destination=path.join(root,name);
    } else {
      const result=await dialog.showSaveDialog(BrowserWindow.fromWebContents(event.sender), {
        defaultPath:name,filters:[{name:name.endsWith(".zip")?"Skin ZIP":"JSON",extensions:[name.endsWith(".zip")?"zip":"json"]}]
      });
      if(result.canceled || !result.filePath)return false;
      destination=result.filePath;
    }
    await fs.writeFile(destination,bytes);return true;
  } finally {saving=false;}
});

// The desktop shell exposes no Node APIs to the editor page. All package
// import/export remains in the browser file-picker and declarative ZIP writer.
function createWindow() {
  const window = new BrowserWindow({
    // Automated desktop smoke checks can inspect the real isolated renderer
    // without opening a visible window on the user's desktop.
    show: !process.argv.includes("--smoke-test"),
    width: 1440,
    height: 900,
    minWidth: 960,
    minHeight: 640,
    backgroundColor: "#071018",
    autoHideMenuBar: true,
    webPreferences: {
      preload: path.join(__dirname, "preload.cjs"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      webSecurity: true
    }
  });

  // Keep the studio local-only: skins cannot turn the editor into a network
  // browser, and accidental links cannot leave the controlled app window.
  window.webContents.setWindowOpenHandler(() => ({ action: "deny" }));
  window.webContents.on("will-navigate", event => event.preventDefault());
  window.webContents.on("render-process-gone", (_event, details) => {
    console.error("Skin Studio renderer stopped:", details.reason, "exit=" + details.exitCode);
  });
  window.loadFile(entry);
  return window;
}

app.whenReady().then(() => {
  Menu.setApplicationMenu(null);
  session.defaultSession.setPermissionRequestHandler((_webContents, _permission, callback) => callback(false));
  createWindow();
  app.on("activate", () => { if (BrowserWindow.getAllWindows().length === 0) createWindow(); });
});

app.on("window-all-closed", () => { if (process.platform !== "darwin") app.quit(); });
