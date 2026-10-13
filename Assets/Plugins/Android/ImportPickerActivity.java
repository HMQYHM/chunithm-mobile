package hmqyhm.chumobile;

import android.app.Activity;
import android.content.Intent;
import android.database.Cursor;
import android.content.res.AssetFileDescriptor;
import android.net.Uri;
import android.os.Bundle;
import android.os.StatFs;
import android.provider.OpenableColumns;
import android.util.Log;

import com.unity3d.player.UnityPlayer;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

public final class ImportPickerActivity extends Activity {
    private static final int PICK_ZIP = 7812;
    private static final int PICK_IMAGES = 7813;
    private static final int PICK_SKIN = 7814;
    private static final String UNITY_OBJECT = "CHUNITHM Mobile Runtime";
    private static final String EXTRA_PICK_MODE = "pick_mode";
    private static boolean pickerOpen;
    private boolean resultDelivered;
    private volatile boolean copyInProgress;

    public static synchronized void openPicker() {
        openPickerActivity("zip");
    }

    public static synchronized void openBackgroundImagePicker() {
        openPickerActivity("images");
    }

    public static synchronized void openGameplaySkinPicker() {
        openPickerActivity("skin");
    }

    private static synchronized void openPickerActivity(String mode) {
        final Activity unity = UnityPlayer.currentActivity;
        if (unity == null || pickerOpen) return;
        pickerOpen = true;
        unity.runOnUiThread(() -> {
            try {
                Intent proxy = new Intent(unity, ImportPickerActivity.class);
                proxy.putExtra(EXTRA_PICK_MODE, mode);
                unity.startActivity(proxy);
            } catch (Exception exception) {
                synchronized (ImportPickerActivity.class) { pickerOpen = false; }
                Log.e("CHUNITHM_IMPORT", "Unable to open import picker", exception);
                if ("images".equals(mode))
                    UnityPlayer.UnitySendMessage(UNITY_OBJECT, "OnBackgroundImageCopyFailed", "无法打开图片选择器");
                else
                    UnityPlayer.UnitySendMessage(UNITY_OBJECT, "OnImportFilePicked", "");
            }
        });
    }

    @Override protected void onCreate(Bundle state) {
        super.onCreate(state);
        if (state != null) return;
        Intent picker = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        picker.addCategory(Intent.CATEGORY_OPENABLE);
        String mode = getIntent().getStringExtra(EXTRA_PICK_MODE);
        boolean images = "images".equals(mode);
        boolean skin = "skin".equals(mode);
        if (images) {
            picker.setType("image/*");
            picker.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
        } else if (skin) {
            picker.setType("application/octet-stream");
            picker.putExtra(Intent.EXTRA_MIME_TYPES, new String[] {
                "application/octet-stream", "application/zip", "application/x-zip-compressed"
            });
        } else {
            picker.setType("application/zip");
            picker.putExtra(Intent.EXTRA_MIME_TYPES, new String[] {
                "application/zip", "application/x-zip-compressed", "application/octet-stream"
            });
        }
        try {
            startActivityForResult(picker, images ? PICK_IMAGES : skin ? PICK_SKIN : PICK_ZIP);
        } catch (Exception exception) {
            Log.e("CHUNITHM_IMPORT", "No document picker is available", exception);
            if (images) finishWithBackgroundImagesFailure("无法打开图片选择器");
            else finishWithResult("");
        }
    }

    @Override protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if ((requestCode == PICK_ZIP || requestCode == PICK_SKIN) && resultCode == RESULT_OK && data != null && data.getData() != null) {
            final Uri uri = data.getData();
            final String displayName = getDisplayName(uri);
            final long totalBytes = getDocumentSize(uri);
            copyInProgress = true;
            UnityPlayer.UnitySendMessage(UNITY_OBJECT, "OnImportStarted", displayName);
            UnityPlayer.UnitySendMessage(UNITY_OBJECT, "OnImportCopyProgress", "0|" + totalBytes);
            new Thread(() -> copyAndNotify(uri, totalBytes), "CHUNITHM Import Copy").start();
            // Return to Unity immediately. The document is copied by the worker
            // while the in-game import overlay remains visible.
            finish();
            return;
        }
        if (requestCode == PICK_IMAGES) {
            if (resultCode != RESULT_OK || data == null) {
                finishWithBackgroundImages("");
                return;
            }
            List<Uri> uris = collectUris(data);
            if (uris.isEmpty()) {
                finishWithBackgroundImages("");
                return;
            }
            if (uris.size() > 50) {
                finishWithBackgroundImagesFailure("最多只能导入 50 张图片");
                return;
            }
            final List<Uri> selected = uris;
            copyInProgress = true;
            UnityPlayer.UnitySendMessage(UNITY_OBJECT, "OnBackgroundImagesStarted",
                Integer.toString(selected.size()));
            new Thread(() -> copyBackgroundImages(selected), "CHUNITHM Background Image Copy").start();
            finish();
            return;
        }
        finishWithResult("");
    }

    private List<Uri> collectUris(Intent data) {
        List<Uri> result = new ArrayList<>();
        if (data.getClipData() != null) {
            for (int index = 0; index < data.getClipData().getItemCount(); index++)
                result.add(data.getClipData().getItemAt(index).getUri());
        } else if (data.getData() != null) {
            result.add(data.getData());
        }
        return result;
    }

    private void copyBackgroundImages(List<Uri> uris) {
        File imageCache = new File(getCacheDir(), "BackgroundImageImports");
        File outputDirectory = new File(imageCache, "import-" + System.currentTimeMillis());
        StringBuilder paths = new StringBuilder();
        try {
            cleanupImportCache(imageCache);
            if (!outputDirectory.mkdirs() && !outputDirectory.isDirectory())
                throw new IOException("Unable to create image import cache");
            for (int index = 0; index < uris.size(); index++) {
                Uri uri = uris.get(index);
                String displayName = getDisplayName(uri);
                long totalBytes = getDocumentSize(uri);
                String extension = imageExtension(uri, displayName);
                File output = new File(outputDirectory,
                    "source_" + String.format(Locale.US, "%02d", index) + extension);
                copyBackgroundImage(uri, output, index, uris.size(), displayName, totalBytes);
                if (paths.length() > 0) paths.append('\n');
                paths.append(output.getAbsolutePath());
            }
            finishWithBackgroundImages(paths.toString());
        } catch (Exception exception) {
            if (!deleteRecursively(outputDirectory))
                Log.w("CHUNITHM_IMPORT", "Unable to remove incomplete image cache " + outputDirectory);
            Log.e("CHUNITHM_IMPORT", "Failed to copy background images", exception);
            finishWithBackgroundImagesFailure(exception.getMessage());
        }
    }

    private void copyBackgroundImage(Uri uri, File output, int index, int count,
        String displayName, long totalBytes) throws IOException {
        try (InputStream input = getContentResolver().openInputStream(uri)) {
            if (input == null) throw new IOException("无法读取图片：" + displayName);
            try (FileOutputStream stream = new FileOutputStream(output)) {
                byte[] buffer = new byte[256 * 1024];
                long written = 0;
                long lastReportedBytes = 0;
                long lastReportTime = 0;
                int read;
                while ((read = input.read(buffer)) != -1) {
                    if (read == 0) continue;
                    stream.write(buffer, 0, read);
                    written += read;
                    long now = android.os.SystemClock.elapsedRealtime();
                    if (written - lastReportedBytes >= 1024L * 1024L || now - lastReportTime >= 350) {
                        sendBackgroundImageProgress(index, count, written, totalBytes, displayName);
                        lastReportedBytes = written;
                        lastReportTime = now;
                    }
                }
                stream.getFD().sync();
                if (written <= 0) throw new IOException("图片为空：" + displayName);
                sendBackgroundImageProgress(index, count, written,
                    totalBytes > 0 ? totalBytes : written, displayName);
            }
        }
    }

    private void sendBackgroundImageProgress(int index, int count, long written,
        long totalBytes, String displayName) {
        final String payload = index + "|" + count + "|" + written + "|" + totalBytes
            + "|" + (displayName == null ? "" : displayName.replace("|", "_"));
        runOnUiThread(() -> UnityPlayer.UnitySendMessage(
            UNITY_OBJECT, "OnBackgroundImageCopyProgress", payload));
    }

    private String imageExtension(Uri uri, String displayName) {
        String name = displayName == null ? "" : displayName.toLowerCase(Locale.US);
        if (name.endsWith(".png")) return ".png";
        if (name.endsWith(".jpeg")) return ".jpeg";
        if (name.endsWith(".jpg")) return ".jpg";
        String mime = getContentResolver().getType(uri);
        if ("image/png".equalsIgnoreCase(mime)) return ".png";
        return ".jpg";
    }

    private void copyAndNotify(Uri uri, long totalBytes) {
        File importCache = new File(getCacheDir(), "Imports");
        File output = new File(importCache, "song-import-" + System.currentTimeMillis() + ".zip");
        try {
            if (!importCache.exists() && !importCache.mkdirs())
                throw new IOException("Unable to create app import cache");
            cleanupImportCache(importCache);
            ensureCopySpace(importCache, totalBytes);
            try (InputStream input = getContentResolver().openInputStream(uri)) {
                if (input == null) throw new IOException("The selected document could not be opened");
                try (FileOutputStream stream = new FileOutputStream(output)) {
                    byte[] buffer = new byte[1024 * 1024];
                    long written = 0;
                    long lastReportedBytes = 0;
                    long lastReportTime = 0;
                    int read;
                    while ((read = input.read(buffer)) != -1) {
                        if (read == 0) continue;
                        stream.write(buffer, 0, read);
                        written += read;
                        long now = android.os.SystemClock.elapsedRealtime();
                        if (written - lastReportedBytes >= 8L * 1024 * 1024 || now - lastReportTime >= 350) {
                            sendCopyProgress(written, totalBytes);
                            lastReportedBytes = written;
                            lastReportTime = now;
                        }
                    }
                    stream.getFD().sync();
                    if (written <= 0) throw new IOException("The selected document is empty");
                    sendCopyProgress(written, totalBytes > 0 ? totalBytes : written);
                }
            }
            finishWithResult(output.getAbsolutePath());
        } catch (Exception exception) {
            if (output.exists() && !output.delete())
                Log.w("CHUNITHM_IMPORT", "Unable to remove incomplete cache file " + output);
            Log.e("CHUNITHM_IMPORT", "Failed to copy selected ZIP " + uri, exception);
            finishWithFailure(exception.getMessage());
        }
    }

    private void cleanupImportCache(File importCache) {
        File[] children = importCache.listFiles();
        if (children == null) return;
        for (File child : children) {
            if (!deleteRecursively(child))
                Log.w("CHUNITHM_IMPORT", "Unable to remove stale import cache " + child);
        }
    }

    private boolean deleteRecursively(File file) {
        if (file.isDirectory()) {
            File[] children = file.listFiles();
            if (children != null) for (File child : children) deleteRecursively(child);
        }
        return !file.exists() || file.delete();
    }

    private void ensureCopySpace(File importCache, long totalBytes) throws IOException {
        if (totalBytes <= 0) return;
        StatFs stats = new StatFs(importCache.getAbsolutePath());
        long available = stats.getAvailableBytes();
        long margin = Math.max(256L * 1024 * 1024, totalBytes / 10);
        if (available < totalBytes + margin) {
            throw new IOException("存储空间不足，复制需要约 "
                + ((totalBytes + margin) / (1024 * 1024)) + " MB，可用 "
                + (available / (1024 * 1024)) + " MB");
        }
    }

    private void sendCopyProgress(long written, long totalBytes) {
        final String payload = written + "|" + totalBytes;
        runOnUiThread(() -> UnityPlayer.UnitySendMessage(
            UNITY_OBJECT, "OnImportCopyProgress", payload));
    }

    private String getDisplayName(Uri uri) {
        try (Cursor cursor = getContentResolver().query(uri,
                new String[] { OpenableColumns.DISPLAY_NAME }, null, null, null)) {
            if (cursor != null && cursor.moveToFirst()) {
                String value = cursor.getString(0);
                if (value != null && !value.trim().isEmpty()) return value;
            }
        } catch (Exception exception) {
            Log.w("CHUNITHM_IMPORT", "Unable to read document name", exception);
        }
        String fallback = uri.getLastPathSegment();
        return fallback == null || fallback.trim().isEmpty() ? "所选 ZIP" : fallback;
    }

    private long getDocumentSize(Uri uri) {
        try (Cursor cursor = getContentResolver().query(uri,
                new String[] { OpenableColumns.SIZE }, null, null, null)) {
            if (cursor != null && cursor.moveToFirst() && !cursor.isNull(0)) {
                long value = cursor.getLong(0);
                if (value >= 0) return value;
            }
        } catch (Exception exception) {
            Log.w("CHUNITHM_IMPORT", "Unable to query document size", exception);
        }
        try (AssetFileDescriptor descriptor = getContentResolver().openAssetFileDescriptor(uri, "r")) {
            return descriptor == null ? -1 : descriptor.getLength();
        } catch (Exception exception) {
            return -1;
        }
    }

    private void finishWithResult(String path) {
        if (resultDelivered) return;
        resultDelivered = true;
        final String safePath = path == null ? "" : path;
        runOnUiThread(() -> {
            copyInProgress = false;
            synchronized (ImportPickerActivity.class) { pickerOpen = false; }
            UnityPlayer.UnitySendMessage(UNITY_OBJECT, "OnImportFilePicked", safePath);
            if (!isFinishing()) finish();
        });
    }

    private void finishWithFailure(String message) {
        if (resultDelivered) return;
        resultDelivered = true;
        final String safeMessage = message == null || message.trim().isEmpty()
            ? "无法读取所选文件" : message;
        runOnUiThread(() -> {
            copyInProgress = false;
            synchronized (ImportPickerActivity.class) { pickerOpen = false; }
            UnityPlayer.UnitySendMessage(UNITY_OBJECT, "OnImportCopyFailed", safeMessage);
            if (!isFinishing()) finish();
        });
    }

    private void finishWithBackgroundImages(String paths) {
        if (resultDelivered) return;
        resultDelivered = true;
        final String safePaths = paths == null ? "" : paths;
        runOnUiThread(() -> {
            copyInProgress = false;
            synchronized (ImportPickerActivity.class) { pickerOpen = false; }
            UnityPlayer.UnitySendMessage(UNITY_OBJECT, "OnBackgroundImagesPicked", safePaths);
            if (!isFinishing()) finish();
        });
    }

    private void finishWithBackgroundImagesFailure(String message) {
        if (resultDelivered) return;
        resultDelivered = true;
        final String safeMessage = message == null || message.trim().isEmpty()
            ? "无法读取所选图片" : message;
        runOnUiThread(() -> {
            copyInProgress = false;
            synchronized (ImportPickerActivity.class) { pickerOpen = false; }
            UnityPlayer.UnitySendMessage(UNITY_OBJECT, "OnBackgroundImageCopyFailed", safeMessage);
            if (!isFinishing()) finish();
        });
    }

    @Override protected void onDestroy() {
        if (isFinishing() && !copyInProgress) {
            synchronized (ImportPickerActivity.class) { pickerOpen = false; }
        }
        super.onDestroy();
    }
}
