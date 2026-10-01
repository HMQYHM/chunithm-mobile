const { app, BrowserWindow, Menu, session } = require("electron");
const path = require("path");

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
  window.loadFile(path.join(__dirname, "..", "index.html"));
  return window;
}

app.whenReady().then(() => {
  Menu.setApplicationMenu(null);
  session.defaultSession.setPermissionRequestHandler((_webContents, _permission, callback) => callback(false));
  createWindow();
  app.on("activate", () => { if (BrowserWindow.getAllWindows().length === 0) createWindow(); });
});

app.on("window-all-closed", () => { if (process.platform !== "darwin") app.quit(); });
